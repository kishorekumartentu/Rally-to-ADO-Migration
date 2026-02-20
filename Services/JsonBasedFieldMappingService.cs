using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rally_to_ADO_Migration.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Rally_to_ADO_Migration.Services.JsonBasedFieldMappingService;
using System.Net;

namespace Rally_to_ADO_Migration.Services
{
    public class JsonBasedFieldMappingService
    {
        private readonly LoggingService _loggingService;
        private readonly string _basePath;
        private FieldMappingConfiguration _mappingConfig;
        private FieldTransformationService _transformationService;

        // System fields we will apply ONLY after creation (requires bypass rules)
        // CRITICAL: System.State is now in post-creation to avoid validation errors
        // Different ADO process templates have different valid state values
        // Safer to create with default state, then update if needed
        // 
        // SPECIAL HANDLING:
        // - User Stories (HierarchicalRequirement): Create with "New", then update to ScheduleState mapping
        // - Tasks: Create with "New", then update to ScheduleState mapping  
        private static readonly HashSet<string> PostCreationSystemFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.State", "System.CreatedDate", "System.ChangedDate", "System.CreatedBy"
        };

        public JsonBasedFieldMappingService(LoggingService loggingService)
        {
            _loggingService = loggingService;
            _basePath = AppDomain.CurrentDomain.BaseDirectory;
            _transformation_service_reset();
        }

        private void _transformation_service_reset() => _transformationService = new FieldTransformationService(_loggingService, _mappingConfig, null);

        public bool LoadMappingConfiguration(string jsonFilePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(jsonFilePath))
                {
                    var configPath = Path.Combine(_basePath, "Config");
                    if (!Directory.Exists(configPath)) Directory.CreateDirectory(configPath);
                    jsonFilePath = Path.Combine(configPath, "FieldMappingConfiguration.json");
                    if (!File.Exists(jsonFilePath))
                    {
                        var baseConfigPath = Path.Combine(_basePath, "FieldMappingConfiguration.json");
                        if (File.Exists(baseConfigPath)) jsonFilePath = baseConfigPath;
                    }
                }

                if (!File.Exists(jsonFilePath))
                {
                    _loggingService.LogError($"Field mapping configuration file not found: {jsonFilePath}");
                    return false;
                }

                _loggingService.LogInfo($"Loading field mapping configuration from: {jsonFilePath}");
                var jsonContent = File.ReadAllText(jsonFilePath, Encoding.UTF8);
                _mappingConfig = JsonConvert.DeserializeObject<FieldMappingConfiguration>(jsonContent);

                if (_mappingConfig?.WorkItemTypeMappings?.Count > 0)
                {
                    _loggingService.LogInfo($"Loaded {_mappingConfig.WorkItemTypeMappings.Count} work item type mappings");
                    _transformationService = new FieldTransformationService(_loggingService, _mappingConfig, null);
                    return true;
                }

                _loggingService.LogError("Failed to parse mapping configuration");
                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error loading field mapping configuration", ex);
                return false;
            }
        }

        /// <summary>
        /// Returns two dictionaries: fields for creation and fields for post-creation enrichment (historical preservation)
        /// </summary>
        public (Dictionary<string, object> creationFields, Dictionary<string, object> postCreationFields) TransformRallyWorkItemToAdoFieldsSplit(RallyWorkItem rallyItem)
        {
            var creationFields = new Dictionary<string, object>();
            var postFields = new Dictionary<string, object>();

            if (rallyItem == null)
            {
                _loggingService.LogWarning("Rally item is null");
                return (creationFields, postFields);
            }

            // Track Rally users for tagging on ALL items regardless of state (user requirement)
            _transformationService.TrackRallyUsersForTagging(rallyItem);
            if (_mappingConfig == null)
            {
                _loggingService.LogError("Field mapping configuration not loaded");
                return (creationFields, postFields);
            }

            var rallyType = NormalizeRallyType(rallyItem.Type);
            var workItemTypeMapping = _mappingConfig.WorkItemTypeMappings
                .FirstOrDefault(m => string.Equals(m.RallyWorkItemType, rallyType, StringComparison.OrdinalIgnoreCase))
                ?? _mappingConfig.WorkItemTypeMappings.FirstOrDefault();
            if (workItemTypeMapping == null)
            {
                _loggingService.LogError($"No mapping found for Rally type: {rallyType}");
                return (creationFields, postFields);
            }

            creationFields["System.WorkItemType"] = workItemTypeMapping.AdoWorkItemType;

            foreach (var fieldMapping in workItemTypeMapping.FieldMappings)
            {
                // Determine if field should be processed despite Skip (we override skip for post-creation system fields)
                bool isPostSystem = PostCreationSystemFields.Contains(fieldMapping.AdoFieldReference ?? "");
                if (fieldMapping.Skip && !isPostSystem) continue;

                try
                {
                    var rallyValue = GetRallyFieldValue(rallyItem, fieldMapping.RallyFieldName);
                    
                    // Enhanced logging for PlanEstimate field
                    if (fieldMapping.RallyFieldName == "PlanEstimate" && rallyValue != null)
                    {
                        _loggingService.LogInfo($"[PLANESTIMATE] Mapping for {rallyItem.FormattedID}:");
                        _loggingService.LogInfo($"   Rally PlanEstimate value: {rallyValue}");
                        _loggingService.LogInfo($"   Rally TaskEstimateTotal: {rallyItem.TaskEstimateTotal}");
                        _loggingService.LogInfo($"   Will map to ADO: Microsoft.VSTS.Scheduling.StoryPoints");
                        
                        // Warning if values match (indicates possible Rally rollup issue)
                        if (rallyItem.TaskEstimateTotal.HasValue)
                        {
                            var planEst = Convert.ToDouble(rallyValue);
                            if (Math.Abs(planEst - rallyItem.TaskEstimateTotal.Value) < 0.001)
                            {
                                _loggingService.LogWarning($"[WARNING] PlanEstimate ({planEst}) equals TaskEstimateTotal ({rallyItem.TaskEstimateTotal})!");
                                _loggingService.LogWarning($"   This may indicate Rally API returned rolled-up child task estimates instead of user-entered value.");
                                _loggingService.LogWarning($"   Verify in Rally UI that Plan Estimate field shows the correct value.");
                            }
                        }
                    }
                    
                    if (rallyValue == null && !fieldMapping.RallyRequired && string.IsNullOrEmpty(fieldMapping.DefaultValue))
                    {
                        if (isPostSystem && !string.IsNullOrEmpty(fieldMapping.DefaultValue)) rallyValue = fieldMapping.DefaultValue; else continue;
                    }

                    var transformedValue = ApplyTransformations(rallyValue, rallyItem, fieldMapping);
                    if (transformedValue == null) continue;

                    if (isPostSystem)
                    {
                        postFields[fieldMapping.AdoFieldReference] = transformedValue;
                        _loggingService.LogDebug($"(Post) Mapped {fieldMapping.RallyFieldName} -> {fieldMapping.AdoFieldReference}: {transformedValue}");
                    }
                    else
                    {
                        creationFields[fieldMapping.AdoFieldReference] = transformedValue;
                        _loggingService.LogDebug($"Mapped {fieldMapping.RallyFieldName} -> {fieldMapping.AdoFieldReference}: {transformedValue}");
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Error mapping field {fieldMapping.RallyFieldName}: {ex.Message}");
                }
            }

            // Rally ID tag for traceability (creation phase only)
            var rallyTag = $"Rally-{rallyItem.FormattedID}";
            var objectIdTag = $"RallyObjectID-{rallyItem.ObjectID}";
            
            // Add Rally user tags for ALL Rally users (user's requirement)
            var rallyUsers = _transformationService.GetRallyUsers();
            var rallyUserTags = new List<string>();
            foreach (var rallyUser in rallyUsers)
            {
                if (!string.IsNullOrWhiteSpace(rallyUser))
                {
                    // Extract just the username part if it's an email
                    var cleanUsername = rallyUser.Contains("@") ? rallyUser.Split('@')[0] : rallyUser;
                    var userTag = $"RallyUser-{cleanUsername}";
                    rallyUserTags.Add(userTag);
                    _loggingService.LogInfo($"🏷️ Adding Rally user tag: {userTag} (from Rally user: {rallyUser})");
                }
            }
            
            if (creationFields.TryGetValue("System.Tags", out var existingTags))
            {
                var tagStr = existingTags.ToString();
                var newTags = new List<string>(tagStr.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries));
                if (!newTags.Contains(rallyTag)) newTags.Add(rallyTag);
                if (!newTags.Contains(objectIdTag)) newTags.Add(objectIdTag);
                
                // Add Rally user tags for invalid users
                foreach (var userTag in rallyUserTags)
                {
                    if (!newTags.Contains(userTag)) newTags.Add(userTag);
                }
                
                creationFields["System.Tags"] = string.Join(";", newTags);
            }
            else
            {
                var allTags = new List<string> { rallyTag, objectIdTag };
                allTags.AddRange(rallyUserTags);
                creationFields["System.Tags"] = string.Join(";", allTags);
            }
            
            // Clear invalid users for next work item
            _transformationService.ClearRallyUsers();

            EnsureRequiredFields(creationFields, rallyItem);
            return (creationFields, postFields);
        }

        public Dictionary<string, object> TransformRallyWorkItemToAdoFields(RallyWorkItem rallyItem)
        {
            var split = TransformRallyWorkItemToAdoFieldsSplit(rallyItem);
            // merge for backward compatibility (creation + post)
            foreach (var kv in split.postCreationFields)
                if (!split.creationFields.ContainsKey(kv.Key))
                    split.creationFields[kv.Key] = kv.Value;
            return split.creationFields;
        }

        private object ApplyTransformations(object rallyValue, RallyWorkItem rallyItem, FieldMapping fieldMapping)
        {
            if (!string.IsNullOrEmpty(fieldMapping.CustomTransformation) && fieldMapping.CustomTransformation.IndexOf("CONSTANT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!string.IsNullOrEmpty(fieldMapping.DefaultValue))
                {
                    _loggingService.LogDebug($"Applying CONSTANT transformation for {fieldMapping.AdoFieldReference}: {fieldMapping.DefaultValue}");
                    return fieldMapping.DefaultValue;
                }
            }
            if (rallyValue == null && !string.IsNullOrEmpty(fieldMapping.DefaultValue)) rallyValue = fieldMapping.DefaultValue;
            if (rallyValue == null) return null;

            // Decode Unicode escapes for all string values FIRST, before any transformation
            if (rallyValue is string strValue && !string.IsNullOrEmpty(strValue))
            {
                rallyValue = System.Text.RegularExpressions.Regex.Unescape(strValue);
            }

            var transformations = (fieldMapping.CustomTransformation ?? "DIRECT")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToUpperInvariant());

            object transformedValue = rallyValue;
            foreach (var transformation in transformations)
            {
                switch (transformation)
                {
                    case "DATE_FORMAT": transformedValue = _transformationService.TransformDate(transformedValue); break;
                    case "USER_LOOKUP": transformedValue = _transformationService.TransformUser(transformedValue); break;
                    case "STATE_MAPPING": transformedValue = _transformationService.TransformState(transformedValue, rallyItem.Type); break;
                    case "ENUM_MAPPING": transformedValue = _transformationService.TransformEnum(transformedValue, fieldMapping.RallyFieldName); break;
                    case "COLLECTION_TO_STRING": transformedValue = _transformationService.TransformCollection(transformedValue); break;
                    case "RALLY_ID_FORMAT": transformedValue = _transformationService.TransformRallyId(rallyItem); break;
                    case "PROJECT_TO_AREA": transformedValue = _transformationService.TransformProjectToAreaPath(transformedValue, fieldMapping); break;
                    case "ITERATION_TO_PATH": transformedValue = _transformationService.TransformIterationToPath(transformedValue, fieldMapping); break;
                    case "CONSTANT": break;
                    case "HTML_PRESERVE":
                        transformedValue = _transformationService.TransformHtmlPreserve(transformedValue);
                        break;
                    case "HTML_APPEND":
                        // Append Notes beneath existing Description; rely on creationFields later to merge.
                        transformedValue = _transformationService.TransformHtmlPreserve(transformedValue);
                        break;
                    case "DIRECT": break;
                }
            }
            return transformedValue;
        }

        private object GetRallyFieldValue(RallyWorkItem rallyItem, string fieldName)
        {
            try
            {
                if (string.IsNullOrEmpty(fieldName) || rallyItem == null) return null;
                var property = typeof(RallyWorkItem).GetProperty(fieldName);
                if (property != null) return property.GetValue(rallyItem);
                if (rallyItem.CustomFields?.ContainsKey(fieldName) == true) return rallyItem.CustomFields[fieldName];
                return null;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error getting Rally field {fieldName}: {ex.Message}");
                return null;
            }
        }

        private void EnsureRequiredFields(Dictionary<string, object> adoFields, RallyWorkItem rallyItem)
        {
            if (!adoFields.ContainsKey("System.Title") || string.IsNullOrWhiteSpace(adoFields["System.Title"]?.ToString()))
                adoFields["System.Title"] = $"[{rallyItem.FormattedID}] {rallyItem.Name}";

            if (!adoFields.ContainsKey("System.AreaPath") && !string.IsNullOrEmpty(_mappingConfig?.DefaultAdoProject))
            {
                adoFields["System.AreaPath"] = _mappingConfig.DefaultAdoProject;
                _loggingService.LogDebug($"Applied fallback AreaPath: {_mappingConfig.DefaultAdoProject}");
            }
            else if (adoFields.ContainsKey("System.AreaPath"))
            {
                var areaVal = adoFields["System.AreaPath"];
                if (areaVal != null && !(areaVal is string))
                {
                    var converted = areaVal.ToString();
                    if (converted.StartsWith("{") && converted.Contains("_refObjectName"))
                    {
                        var fm = new FieldMapping { DefaultValue = _mappingConfig.DefaultAdoProject + "\\Emerson" };
                        adoFields["System.AreaPath"] = _transformationService.TransformProjectToAreaPath(converted, fm);
                        _loggingService.LogDebug($"Converted JSON project object to AreaPath: {adoFields["System.AreaPath"]}");
                    }
                    else adoFields["System.AreaPath"] = converted;
                }
            }

            // CRITICAL: For User Stories, Defects, and Tasks, ALWAYS create with "New" state
            // The actual Rally state will be applied post-creation via bypass rules (or fallback)
            // This prevents ADO validation errors during creation
            //
            // User Stories: Use ScheduleState field (Defined, In-Progress, Completed, Accepted)
            // Defects: Use ScheduleState field (Submitted, Open, Fixed, Closed)
            // Tasks: Use State field (Defined, In-Progress, Completed)
            if (string.Equals(rallyItem.Type, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase))
            {
                // Remove System.State from creation fields if it exists (will be added to post-creation)
                if (adoFields.ContainsKey("System.State"))
                {
                    _loggingService.LogDebug($"Removed System.State from creation fields for User Story {rallyItem.FormattedID} (will be set post-creation)");
                    adoFields.Remove("System.State");
                }
                
                _loggingService.LogInfo($"[STATE] User Story {rallyItem.FormattedID} will be created with State='New', then updated to actual ScheduleState: '{rallyItem.State}'");
            }
            else if (string.Equals(rallyItem.Type, "Defect", StringComparison.OrdinalIgnoreCase))
            {
                // Remove System.State from creation fields if it exists (will be added to post-creation)
                if (adoFields.ContainsKey("System.State"))
                {
                    _loggingService.LogDebug($"Removed System.State from creation fields for Defect {rallyItem.FormattedID} (will be set post-creation)");
                    adoFields.Remove("System.State");
                }
                
                // For Defects, rallyItem.State contains the ScheduleState value (similar to User Stories)
                _loggingService.LogInfo($"[STATE] Defect {rallyItem.FormattedID} will be created with State='New', then updated to actual ScheduleState: '{rallyItem.State}'");
            }
            else if (string.Equals(rallyItem.Type, "Task", StringComparison.OrdinalIgnoreCase))
            {
                // Remove System.State from creation fields if it exists (will be added to post-creation)
                if (adoFields.ContainsKey("System.State"))
                {
                    _loggingService.LogDebug($"Removed System.State from creation fields for Task {rallyItem.FormattedID} (will be set post-creation)");
                    adoFields.Remove("System.State");
                }
                
                // For Tasks, rallyItem.State contains the State field value
                _loggingService.LogInfo($"[STATE] Task {rallyItem.FormattedID} will be created with State='New', then updated to actual State: '{rallyItem.State}'");
            }
            
            // DO NOT add System.State here for other types - it's a PostCreationSystemField
            // Different ADO process templates have different valid state values
            // Let ADO use its default initial state during creation
            // State will be updated in post-creation phase if needed
            
            // SPECIAL HANDLING FOR TEST CASES: Always ensure State is set to "Ready" post-creation
            // This overrides any Rally state mapping to ensure all Test Cases are Ready by default
            if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
            {
                // Don't add to creation fields - will be set post-creation
                if (adoFields.ContainsKey("System.State"))
                {
                    _loggingService.LogDebug($"Removed System.State from creation fields for Test Case {rallyItem.FormattedID} (will be set to Ready post-creation)");
                    adoFields.Remove("System.State");
                }
                
                _loggingService.LogInfo($"[STATE] Test Case {rallyItem.FormattedID} will be created with default state, then updated to State='Ready'");
            }
        }

        private string NormalizeRallyType(string rallyType)
        {
            if (string.IsNullOrEmpty(rallyType)) return "HierarchicalRequirement";
            
            // DO NOT strip PortfolioItem/ prefix - it's needed to match the configuration
            // The configuration has entries like "PortfolioItem/Feature" and "PortfolioItem/Epic"
            return rallyType;
        }

        public List<string> GetMappedRallyFieldsForType(string rallyType)
        {
            var list = new List<string>();
            if (_mappingConfig == null) return list;
            var mapping = _mappingConfig.WorkItemTypeMappings
                .FirstOrDefault(m => string.Equals(m.RallyWorkItemType, rallyType, StringComparison.OrdinalIgnoreCase));
            if (mapping == null) return list;
            foreach (var f in mapping.FieldMappings)
            {
                if (!string.IsNullOrEmpty(f.RallyFieldName)) list.Add(f.RallyFieldName);
            }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Gets the configured migration user email from the transformation service.
        /// This is used when Rally users are not found in ADO and need to be assigned to a fallback user.
        /// </summary>
        /// <returns>The migration user email or null if not configured</returns>
        public string GetMigrationUserEmail()
        {
            return _transformationService?.GetMigrationUserEmail();
        }
    }
}
