using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Robust field mapping generator based on jira-rally-export patterns
    /// Intelligently maps Rally fields to ADO fields with confidence scoring
    /// </summary>
    public class AutomatedFieldMappingGenerator
    {
        private readonly LoggingService _loggingService;

        public AutomatedFieldMappingGenerator(LoggingService loggingService = null)
        {
            _loggingService = loggingService ?? new LoggingService();
        }

        /// <summary>
        /// Rally field definition from Rally_Field_Discovery JSON
        /// </summary>
        public class RallyFieldDefinition
        {
            public string ElementName { get; set; }
            public string Name { get; set; }
            public string AttributeType { get; set; }
            public bool Required { get; set; }
            public bool ReadOnly { get; set; }
            public bool Custom { get; set; }
            public bool Hidden { get; set; }
            public List<string> AllowedValues { get; set; }
            public string TypeDefinition { get; set; } // Which TypeDefinition this belongs to

            public RallyFieldDefinition()
            {
                AllowedValues = new List<string>();
            }
        }

        /// <summary>
        /// ADO field definition from ADO_Field_Discovery JSON
        /// </summary>
        public class AdoFieldDefinition
        {
            public string ReferenceName { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string WorkItemType { get; set; } // Which work item type this belongs to
        }

        /// <summary>
        /// Field mapping with confidence and transformation
        /// </summary>
        public class FieldMappingResult
        {
            public RallyFieldDefinition RallyField { get; set; }
            public AdoFieldDefinition AdoField { get; set; }
            public string Confidence { get; set; } // High, Medium, Low, None
            public string MappingReason { get; set; }
            public string SuggestedTransformation { get; set; }
            public bool RequiresReview { get; set; }
        }

        /// <summary>
        /// Parse Rally Field Discovery JSON (output from Rally Field Discovery tool)
        /// </summary>
        public Dictionary<string, List<RallyFieldDefinition>> ParseRallyFieldDiscoveryJson(string rallyJsonPath)
        {
            try
            {
                _loggingService.LogInfo($"Parsing Rally Field Discovery JSON: {rallyJsonPath}");
                
                var json = File.ReadAllText(rallyJsonPath);
                var root = JObject.Parse(json);
                
                var typeDefinitions = root["TypeDefinitions"] as JArray;
                if (typeDefinitions == null)
                {
                    _loggingService.LogError("No TypeDefinitions found in Rally JSON");
                    return new Dictionary<string, List<RallyFieldDefinition>>();
                }

                var result = new Dictionary<string, List<RallyFieldDefinition>>();

                foreach (var typeDef in typeDefinitions)
                {
                    var typeName = typeDef["TypeName"]?.ToString();
                    var attributes = typeDef["Attributes"] as JArray;

                    if (string.IsNullOrEmpty(typeName) || attributes == null)
                        continue;

                    var fields = new List<RallyFieldDefinition>();

                    foreach (var attr in attributes)
                    {
                        var field = new RallyFieldDefinition
                        {
                            ElementName = attr["ElementName"]?.ToString(),
                            Name = attr["Name"]?.ToString(),
                            AttributeType = attr["AttributeType"]?.ToString(),
                            Required = attr["Required"]?.ToObject<bool>() ?? false,
                            ReadOnly = attr["ReadOnly"]?.ToObject<bool>() ?? false,
                            Custom = attr["Custom"]?.ToObject<bool>() ?? false,
                            Hidden = attr["Hidden"]?.ToObject<bool>() ?? false,
                            TypeDefinition = typeName
                        };

                        var allowedValues = attr["AllowedValues"] as JArray;
                        if (allowedValues != null)
                        {
                            field.AllowedValues = allowedValues.Select(v => v.ToString()).ToList();
                        }

                        fields.Add(field);
                    }

                    result[typeName] = fields;
                    _loggingService.LogInfo($"Parsed {fields.Count} fields for Rally type: {typeName}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to parse Rally Field Discovery JSON: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Parse ADO Field Discovery JSON (output from ADO Field Discovery tool)
        /// </summary>
        public Dictionary<string, List<AdoFieldDefinition>> ParseAdoFieldDiscoveryJson(string adoJsonPath)
        {
            try
            {
                _loggingService.LogInfo($"Parsing ADO Field Discovery JSON: {adoJsonPath}");
                
                var json = File.ReadAllText(adoJsonPath);
                var root = JObject.Parse(json);
                
                var workItemTypes = root["WorkItemTypes"] as JObject;
                if (workItemTypes == null)
                {
                    _loggingService.LogError("No WorkItemTypes found in ADO JSON");
                    return new Dictionary<string, List<AdoFieldDefinition>>();
                }

                var result = new Dictionary<string, List<AdoFieldDefinition>>();

                foreach (var kvp in workItemTypes)
                {
                    var workItemType = kvp.Key;
                    var fieldsArray = kvp.Value as JArray;

                    if (fieldsArray == null)
                        continue;

                    var fields = new List<AdoFieldDefinition>();

                    foreach (var field in fieldsArray)
                    {
                        fields.Add(new AdoFieldDefinition
                        {
                            ReferenceName = field["ReferenceName"]?.ToString(),
                            Name = field["Name"]?.ToString(),
                            Type = field["Type"]?.ToString(),
                            WorkItemType = workItemType
                        });
                    }

                    result[workItemType] = fields;
                    _loggingService.LogInfo($"Parsed {fields.Count} fields for ADO type: {workItemType}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to parse ADO Field Discovery JSON: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Create intelligent field mappings between Rally and ADO types
        /// Based on jira-rally-export mapping logic
        /// </summary>
        public List<FieldMappingResult> CreateIntelligentMappings(
            string rallyTypeName,
            List<RallyFieldDefinition> rallyFields,
            string adoTypeName,
            List<AdoFieldDefinition> adoFields)
        {
            _loggingService.LogInfo($"Creating mappings: {rallyTypeName} -> {adoTypeName}");
            
            var mappings = new List<FieldMappingResult>();

            // Standard field mappings (high confidence - based on jira-rally-export patterns)
            var standardMappings = GetStandardFieldMappings();

            foreach (var rallyField in rallyFields)
            {
                var mapping = new FieldMappingResult
                {
                    RallyField = rallyField,
                    Confidence = "None",
                    RequiresReview = true
                };

                // Skip system/hidden fields
                if (rallyField.Hidden || rallyField.ReadOnly && IsSystemField(rallyField.ElementName))
                {
                    mapping.Confidence = "Skip";
                    mapping.MappingReason = "System/Hidden field - handled automatically";
                    mapping.SuggestedTransformation = "SKIP";
                    mappings.Add(mapping);
                    continue;
                }

                // 1. Try standard mapping (exact match)
                if (standardMappings.ContainsKey(rallyField.ElementName))
                {
                    var standardAdoRef = standardMappings[rallyField.ElementName];
                    var adoField = adoFields.FirstOrDefault(f => f.ReferenceName == standardAdoRef);
                    
                    if (adoField != null)
                    {
                        mapping.AdoField = adoField;
                        mapping.Confidence = "High";
                        mapping.MappingReason = "Standard field mapping";
                        mapping.SuggestedTransformation = GetTransformationType(rallyField, adoField);
                        mapping.RequiresReview = false;
                    }
                }

                // 2. Try name-based matching
                if (mapping.AdoField == null)
                {
                    var adoField = FindAdoFieldByName(rallyField, adoFields);
                    if (adoField != null)
                    {
                        mapping.AdoField = adoField;
                        mapping.Confidence = "Medium";
                        mapping.MappingReason = "Name similarity match";
                        mapping.SuggestedTransformation = GetTransformationType(rallyField, adoField);
                        mapping.RequiresReview = true;
                    }
                }

                // 3. Try type-based matching for custom fields
                if (mapping.AdoField == null && rallyField.Custom)
                {
                    var adoField = FindAdoFieldByTypeAndSemantic(rallyField, adoFields);
                    if (adoField != null)
                    {
                        mapping.AdoField = adoField;
                        mapping.Confidence = "Low";
                        mapping.MappingReason = "Type-based match (custom field)";
                        mapping.SuggestedTransformation = GetTransformationType(rallyField, adoField);
                        mapping.RequiresReview = true;
                    }
                }

                // 4. No match found
                if (mapping.AdoField == null)
                {
                    mapping.Confidence = "None";
                    mapping.MappingReason = $"No suitable ADO field found - consider custom field or description append";
                    mapping.SuggestedTransformation = "APPEND_TO_DESCRIPTION";
                    mapping.RequiresReview = true;
                }

                mappings.Add(mapping);
            }

            var highConfidence = mappings.Count(m => m.Confidence == "High");
            var mediumConfidence = mappings.Count(m => m.Confidence == "Medium");
            var lowConfidence = mappings.Count(m => m.Confidence == "Low");
            var noMatch = mappings.Count(m => m.Confidence == "None");

            _loggingService.LogInfo($"Mapping results: High={highConfidence}, Medium={mediumConfidence}, Low={lowConfidence}, None={noMatch}");

            return mappings;
        }

        /// <summary>
        /// Standard Rally to ADO field mappings
        /// Based on common patterns and official Rally/ADO standards
        /// </summary>
        private Dictionary<string, string> GetStandardFieldMappings()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Core fields - Common to all work item types
                { "FormattedID", "System.Title" },
                { "Name", "System.Title" },
                { "Description", "System.Description" },
                { "State", "System.State" },
                { "Owner", "System.AssignedTo" },
                { "ScheduleState", "System.State" },  // User Stories use ScheduleState
                
                // Dates - Common fields
                { "CreationDate", "System.CreatedDate" },
                { "LastUpdateDate", "System.ChangedDate" },
                { "AcceptedDate", "Microsoft.VSTS.Common.ClosedDate" },
                
                // Effort/Size fields
                { "PlanEstimate", "Microsoft.VSTS.Scheduling.StoryPoints" },  // User Stories, Defects
                { "TaskEstimateTotal", "Microsoft.VSTS.Scheduling.OriginalEstimate" },
                { "TaskRemainingTotal", "Microsoft.VSTS.Scheduling.RemainingWork" },
                { "TaskActuals", "Microsoft.VSTS.Scheduling.CompletedWork" },
                { "Estimate", "Microsoft.VSTS.Scheduling.OriginalEstimate" },  // Tasks
                { "ToDo", "Microsoft.VSTS.Scheduling.RemainingWork" },  // Tasks
                { "Actuals", "Microsoft.VSTS.Scheduling.CompletedWork" },  // Tasks
                
                // Priority/Severity
                { "Priority", "Microsoft.VSTS.Common.Priority" },
                { "Severity", "Microsoft.VSTS.Common.Severity" },  // Defects
                { "Rank", "Microsoft.VSTS.Common.StackRank" },
                
                // Agile/Hierarchy fields
                { "Iteration", "System.IterationPath" },
                { "Release", "System.IterationPath" },
                { "Project", "System.AreaPath" },
                { "Parent", "System.Parent" },
                { "Feature", "System.Parent" },
                { "PortfolioItem", "System.Parent" },
                
                // Tags
                { "Tags", "System.Tags" },
                
                // Defect-specific fields
                { "FoundInBuild", "Microsoft.VSTS.Build.FoundIn" },
                { "c_FoundInBuild", "Microsoft.VSTS.Build.FoundIn" },
                { "IntegratedInBuild", "Microsoft.VSTS.Build.IntegrationBuild" },
                { "c_IntegratedInBuild", "Microsoft.VSTS.Build.IntegrationBuild" },
                { "Environment", "Microsoft.VSTS.TCM.SystemInfo" },
                { "Resolution", "Microsoft.VSTS.Common.Resolution" },
                
                // User Story specific fields
                { "AcceptanceCriteria", "Microsoft.VSTS.Common.AcceptanceCriteria" },
                { "Notes", "System.History" },
                { "Discussion", "System.History" },
                
                // Test Case specific fields
                { "Method", "Microsoft.VSTS.TCM.AutomationStatus" },
                { "Type", "Microsoft.VSTS.TCM.AutomationStatus" },
                { "ValidationInput", "Microsoft.VSTS.TCM.Steps" },
                { "ValidationExpectedResult", "Microsoft.VSTS.TCM.Steps" },
                { "ValidationOutput", "Microsoft.VSTS.TCM.Steps" },
                { "Objective", "System.Description" },
                { "PreConditions", "Microsoft.VSTS.TCM.ReproSteps" },
                { "PostConditions", "System.Description" },
                
                // Epic/Feature specific fields
                { "c_ValueStream", "Custom.ValueStream" },
                { "ValueStream", "Custom.ValueStream" },
                { "Category", "Custom.Category" },
                { "InvestmentCategory", "Custom.Category" },
                
                // Additional common fields
                { "Blocked", "Microsoft.VSTS.CMMI.Blocked" },
                { "BlockedReason", "Microsoft.VSTS.CMMI.BlockedReason" },
                { "Ready", "Microsoft.VSTS.Common.ValueArea" },
                { "CreatedBy", "System.CreatedBy" },
                { "ChangedBy", "System.ChangedBy" }
            };
        }

        /// <summary>
        /// Check if a field is a system field
        /// </summary>
        private bool IsSystemField(string elementName)
        {
            var systemFields = new[]
            {
                "ObjectID", "ObjectUUID", "_ref", "_refObjectUUID", "_refObjectName",
                "_objectVersion", "Subscription", "Workspace", "CreationDate", 
                "LastUpdateDate", "_type", "_CreatedAt", "RevisionHistory"
            };

            return systemFields.Contains(elementName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Find ADO field by name similarity
        /// </summary>
        private AdoFieldDefinition FindAdoFieldByName(RallyFieldDefinition rallyField, List<AdoFieldDefinition> adoFields)
        {
            // Exact match
            var exact = adoFields.FirstOrDefault(f => 
                f.Name.Equals(rallyField.Name, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(rallyField.ElementName, StringComparison.OrdinalIgnoreCase));
            
            if (exact != null) return exact;

            // Contains match (.NET Framework 4.8 compatible)
            var contains = adoFields.FirstOrDefault(f => 
                f.Name.IndexOf(rallyField.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                rallyField.Name.IndexOf(f.Name, StringComparison.OrdinalIgnoreCase) >= 0);

            return contains;
        }

        /// <summary>
        /// Find ADO field by type compatibility
        /// </summary>
        private AdoFieldDefinition FindAdoFieldByTypeAndSemantic(RallyFieldDefinition rallyField, List<AdoFieldDefinition> adoFields)
        {
            // Type mapping
            var compatibleTypes = GetCompatibleAdoTypes(rallyField.AttributeType);
            
            return adoFields.FirstOrDefault(f => 
                compatibleTypes.Contains(f.Type, StringComparer.OrdinalIgnoreCase) &&
                f.ReferenceName.StartsWith("Custom.", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get compatible ADO types for Rally attribute type
        /// </summary>
        private List<string> GetCompatibleAdoTypes(string rallyType)
        {
            var typeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "STRING", new List<string> { "string", "text" } },
                { "TEXT", new List<string> { "html", "text", "string" } },
                { "INTEGER", new List<string> { "integer", "double" } },
                { "DECIMAL", new List<string> { "double", "integer" } },
                { "BOOLEAN", new List<string> { "boolean" } },
                { "DATE", new List<string> { "dateTime" } },
                { "OBJECT", new List<string> { "string", "identity" } },
                { "COLLECTION", new List<string> { "plainText", "html" } }
            };

            return typeMap.ContainsKey(rallyType) ? typeMap[rallyType] : new List<string> { "string" };
        }

        /// <summary>
        /// Determine transformation type needed
        /// </summary>
        private string GetTransformationType(RallyFieldDefinition rallyField, AdoFieldDefinition adoField)
        {
            // State fields need transformation
            if (rallyField.ElementName.Equals("State", StringComparison.OrdinalIgnoreCase) ||
                rallyField.ElementName.Equals("ScheduleState", StringComparison.OrdinalIgnoreCase))
            {
                return "STATE_TRANSFORM";
            }

            // Owner/User fields need identity transformation
            if (rallyField.AttributeType == "OBJECT" && 
                (rallyField.ElementName.Contains("Owner") || rallyField.ElementName.Contains("User")))
            {
                return "USER_TRANSFORM";
            }

            // Date fields might need format transformation
            if (rallyField.AttributeType == "DATE")
            {
                return "DATE_TRANSFORM";
            }

            // Collection fields need special handling
            if (rallyField.AttributeType == "COLLECTION")
            {
                return "COLLECTION_TRANSFORM";
            }

            // Type mismatch needs conversion
            if (!AreTypesCompatible(rallyField.AttributeType, adoField.Type))
            {
                return "TYPE_CONVERSION";
            }

            return "DIRECT";
        }

        /// <summary>
        /// Check if Rally and ADO types are compatible
        /// </summary>
        private bool AreTypesCompatible(string rallyType, string adoType)
        {
            var compatibleTypes = GetCompatibleAdoTypes(rallyType);
            return compatibleTypes.Contains(adoType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Generate comprehensive mapping JSON file that matches the exact structure of FieldMappingConfiguration.json
        /// Based on existing configuration patterns and intelligent field mapping
        /// </summary>
        public void GenerateComprehensiveMappingJson(
            string rallyJsonPath,
            string adoJsonPath,
            string outputPath)
        {
            try
            {
                _loggingService.LogInfo("=== Starting Field Mapping Configuration Generation ===");

                // If the provided paths don't exist, attempt to locate discovery JSONs in the application directory
                if (string.IsNullOrEmpty(rallyJsonPath) || !File.Exists(rallyJsonPath))
                {
                    try
                    {
                        var appDir = AppDomain.CurrentDomain.BaseDirectory;
                        rallyJsonPath = Path.Combine(appDir, "RallyFieldDiscovery.json");
                        
                        if (File.Exists(rallyJsonPath))
                        {
                            _loggingService.LogInfo($"Found Rally discovery JSON: {rallyJsonPath}");
                        }
                        else
                        {
                            _loggingService.LogWarning("RallyFieldDiscovery.json not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Failed to locate Rally discovery JSON: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(adoJsonPath) || !File.Exists(adoJsonPath))
                {
                    try
                    {
                        var appDir = AppDomain.CurrentDomain.BaseDirectory;
                        adoJsonPath = Path.Combine(appDir, "ADOFieldDiscovery.json");
                        
                        if (File.Exists(adoJsonPath))
                        {
                            _loggingService.LogInfo($"Found ADO discovery JSON: {adoJsonPath}");
                        }
                        else
                        {
                            _loggingService.LogWarning("ADOFieldDiscovery.json not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Failed to locate ADO discovery JSON: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(rallyJsonPath) || string.IsNullOrEmpty(adoJsonPath))
                {
                    throw new FileNotFoundException("Rally or ADO discovery JSON not found. Run Field Discovery first.");
                }

                if (!File.Exists(rallyJsonPath) || !File.Exists(adoJsonPath))
                {
                    throw new FileNotFoundException($"Discovery files not found:\nRally: {rallyJsonPath}\nADO: {adoJsonPath}");
                }

                // Parse input JSONs
                var rallyTypes = ParseRallyFieldDiscoveryJson(rallyJsonPath);
                var adoTypes = ParseAdoFieldDiscoveryJson(adoJsonPath);

                // Common work item type mappings (Rally -> ADO)
                // NOTE: Rally type names may have spaces or different formats than API TypePath
                var typeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Exact matches from Rally discovery JSON
                    { "Task", "Task" },
                    { "Defect", "Bug" },
                    { "HierarchicalRequirement", "User Story" },
                    { "Hierarchical Requirement", "User Story" },  // Rally may use spaces
                    { "PortfolioItem/Feature", "Feature" },
                    { "Feature", "Feature" },  // Rally may use shortened name
                    { "PortfolioItem/Epic", "Epic" },
                    { "Epic", "Epic" },  // Rally may use shortened name
                    { "TestCase", "Test Case" },
                    { "Test Case", "Test Case" }  // Rally may use spaces
                };

                // Build the configuration in the EXACT format expected by JsonBasedFieldMappingService
                var config = new JObject
                {
                    ["Version"] = "1.0",
                    ["DefaultAdoProject"] = "Acute Meds Management",
                    ["AreaPathMappings"] = new JObject
                    {
                        ["EMIS | MM Migration"] = "Acute Meds Management\\Emerson\\Rally Migration",
                        ["EMIS"] = "Acute Meds Management\\Emerson\\Rally Migration",
                        ["Emerson"] = "Acute Meds Management\\Emerson\\Rally Migration"
                    },
                    ["WorkItemTypeMappings"] = new JArray()
                };

                var workItemTypeMappings = (JArray)config["WorkItemTypeMappings"];

                foreach (var typeMapping in typeMappings)
                {
                    var rallyTypeName = typeMapping.Key;
                    var adoTypeName = typeMapping.Value;

                    if (!rallyTypes.ContainsKey(rallyTypeName))
                    {
                        _loggingService.LogWarning($"Rally type not found in discovery: {rallyTypeName}");
                        continue;
                    }

                    if (!adoTypes.ContainsKey(adoTypeName))
                    {
                        _loggingService.LogWarning($"ADO type not found in discovery: {adoTypeName}");
                        continue;
                    }

                    var rallyFields = rallyTypes[rallyTypeName];
                    var adoFields = adoTypes[adoTypeName];

                    // Create intelligent mappings
                    var mappings = CreateIntelligentMappings(rallyTypeName, rallyFields, adoTypeName, adoFields);

                    // Convert to the EXACT format expected by JsonBasedFieldMappingService
                    var typeMappingJson = new JObject
                    {
                        ["RallyWorkItemType"] = rallyTypeName,
                        ["AdoWorkItemType"] = adoTypeName,
                        ["FieldMappings"] = new JArray(
                            mappings
                                .Where(m => m.Confidence != "Skip" || IsImportantSystemField(m.RallyField.ElementName))
                                .Select(m => CreateFieldMappingJson(m, rallyTypeName))
                        )
                    };

                    workItemTypeMappings.Add(typeMappingJson);
                    
                    _loggingService.LogInfo($"Generated mapping for {rallyTypeName} -> {adoTypeName}: {mappings.Count(m => m.AdoField != null)} fields mapped");
                }

                // Write output in the exact format
                var outputJson = config.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(outputPath, outputJson, Encoding.UTF8);

                _loggingService.LogInfo($"? Field mapping configuration generated: {outputPath}");
                _loggingService.LogInfo($"   File size: {new FileInfo(outputPath).Length / 1024} KB");
                _loggingService.LogInfo($"   Work item types: {workItemTypeMappings.Count}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to generate field mapping configuration: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Create field mapping JSON in the EXACT format expected by JsonBasedFieldMappingService
        /// </summary>
        private JObject CreateFieldMappingJson(FieldMappingResult mapping, string rallyTypeName)
        {
            var rallyField = mapping.RallyField;
            var adoField = mapping.AdoField;
            
            // Determine the correct transformation based on field type and mapping
            var transformation = DetermineTransformationType(rallyField, adoField, mapping.SuggestedTransformation);
            
            // Determine if this field should be skipped
            var shouldSkip = ShouldSkipField(rallyField, adoField, mapping.Confidence);
            
            // Get default value if applicable
            var defaultValue = GetDefaultValue(rallyField.ElementName, rallyTypeName);
            
            // Get mapping notes
            var mappingNotes = GetMappingNotes(rallyField, adoField, transformation);

            var fieldMapping = new JObject
            {
                ["RallyFieldName"] = rallyField.ElementName,
                ["AdoFieldReference"] = adoField?.ReferenceName ?? "",
                ["CustomTransformation"] = transformation,
                ["RallyRequired"] = rallyField.Required
            };

            // Add Skip property with proper value
            if (shouldSkip)
            {
                fieldMapping["Skip"] = true;
            }

            // Add MappingNotes if present
            if (!string.IsNullOrEmpty(mappingNotes))
            {
                fieldMapping["MappingNotes"] = mappingNotes;
            }

            // Add DefaultValue if present
            if (!string.IsNullOrEmpty(defaultValue))
            {
                fieldMapping["DefaultValue"] = defaultValue;
            }

            return fieldMapping;
        }

        /// <summary>
        /// Determine the correct transformation type based on field analysis
        /// </summary>
        private string DetermineTransformationType(RallyFieldDefinition rallyField, AdoFieldDefinition adoField, string suggestedTransformation)
        {
            if (rallyField == null) return "SKIP";

            var fieldName = rallyField.ElementName;

            // Special handling for specific fields based on existing configuration patterns
            if (fieldName == "FormattedID" || fieldName == "Name")
                return "RALLY_ID_FORMAT";

            if (fieldName == "State" || fieldName == "ScheduleState")
                return "STATE_MAPPING";

            if (fieldName == "Owner" || fieldName == "SubmittedBy" || fieldName == "CreatedBy" || fieldName == "ChangedBy")
                return "USER_LOOKUP";

            if (fieldName == "Priority" || fieldName == "Severity")
                return "ENUM_MAPPING";

            if (fieldName == "CreationDate" || fieldName == "LastUpdateDate" || fieldName == "AcceptedDate")
                return "DATE_FORMAT";

            if (fieldName == "Tags")
                return "COLLECTION_TO_STRING";

            if (fieldName == "Project")
                return "PROJECT_TO_AREA";

            if (fieldName == "Iteration" || fieldName == "Release" || fieldName == "IterationPath")
                return "CONSTANT";

            // Direct mappings for common fields
            if (fieldName == "Description" || 
                fieldName == "AcceptanceCriteria" ||
                fieldName == "PlanEstimate" ||
                fieldName == "Estimate" ||
                fieldName == "ToDo" ||
                fieldName == "Actuals" ||
                fieldName == "Resolution" ||
                fieldName == "Environment" ||
                fieldName == "FoundInBuild" ||
                fieldName == "c_FoundInBuild" ||
                fieldName == "IntegratedInBuild" ||
                fieldName == "c_IntegratedInBuild" ||
                fieldName == "Blocked" ||
                fieldName == "TaskEstimateTotal" ||
                fieldName == "TaskRemainingTotal" ||
                fieldName == "TaskActuals")
                return "DIRECT";

            // Test Case specific
            if (fieldName == "ValidationInput" || 
                fieldName == "ValidationExpectedResult" ||
                fieldName == "ValidationOutput" ||
                fieldName == "PreConditions" ||
                fieldName == "PostConditions" ||
                fieldName == "Method" ||
                fieldName == "Type")
                return "DIRECT";

            // Epic/Feature specific - use CONSTANT for Category and ValueStream
            if (fieldName == "Category" || 
                fieldName == "ValueStream" ||
                fieldName == "c_ValueStream")
                return "CONSTANT";

            // Use suggested transformation if it's valid
            if (!string.IsNullOrEmpty(suggestedTransformation) && 
                suggestedTransformation != "TYPE_CONVERSION" &&
                suggestedTransformation != "SKIP")
            {
                return suggestedTransformation;
            }

            return "DIRECT";
        }

        /// <summary>
        /// Determine if a field should be skipped based on the existing configuration patterns
        /// </summary>
        private bool ShouldSkipField(RallyFieldDefinition rallyField, AdoFieldDefinition adoField, string confidence)
        {
            if (rallyField == null) return true;
            if (confidence == "Skip") return true;

            var fieldName = rallyField.ElementName;

            // System fields that should be skipped based on existing configuration
            var skipFields = new[]
            {
                "State",  // Handled in post-creation
                "CreationDate",  // ADO sets automatically
                "LastUpdateDate",  // ADO sets automatically
                "CreatedBy"  // ADO sets based on PAT user
            };

            return skipFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get default value for specific fields based on existing configuration
        /// </summary>
        private string GetDefaultValue(string rallyFieldName, string rallyTypeName)
        {
            // IterationPath always gets the default iteration path
            if (rallyFieldName.Equals("Iteration", StringComparison.OrdinalIgnoreCase) ||
                rallyFieldName.Equals("Release", StringComparison.OrdinalIgnoreCase) ||
                rallyFieldName.Equals("IterationPath", StringComparison.OrdinalIgnoreCase))
            {
                return "Acute Meds Management\\\\Emerson\\\\Rally Migration";
            }

            // Epic and Feature specific defaults
            if (rallyTypeName.IndexOf("Epic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (rallyFieldName.Equals("Category", StringComparison.OrdinalIgnoreCase))
                {
                    return "Epic";
                }
                if (rallyFieldName.Equals("ValueStream", StringComparison.OrdinalIgnoreCase) || 
                    rallyFieldName.Equals("c_ValueStream", StringComparison.OrdinalIgnoreCase))
                {
                    return "Clinical - Medicines Management";
                }
            }
            
            if (rallyTypeName.IndexOf("Feature", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (rallyFieldName.Equals("Category", StringComparison.OrdinalIgnoreCase))
                {
                    return "Feature";
                }
                if (rallyFieldName.Equals("ValueStream", StringComparison.OrdinalIgnoreCase) || 
                    rallyFieldName.Equals("c_ValueStream", StringComparison.OrdinalIgnoreCase))
                {
                    return "Clinical - Medicines Management";
                }
            }

            return null;
        }

        /// <summary>
        /// Get mapping notes based on field and transformation
        /// </summary>
        private string GetMappingNotes(RallyFieldDefinition rallyField, AdoFieldDefinition adoField, string transformation)
        {
            if (rallyField == null) return null;

            var fieldName = rallyField.ElementName;

            // Specific notes based on existing configuration
            if (transformation == "STATE_MAPPING")
                return "SKIPPED: Cannot set State during work item creation - ADO automatically sets to default. Update State after creation if needed.";

            if (transformation == "DATE_FORMAT" && (fieldName.Contains("Creation") || fieldName.Contains("LastUpdate")))
                return "SKIPPED: Cannot set CreatedDate/ChangedDate during work item creation - ADO sets this automatically.";

            if (transformation == "USER_LOOKUP" && fieldName == "CreatedBy")
                return "SKIPPED: Cannot set CreatedBy during work item creation - ADO sets this based on PAT user.";

            if (transformation == "CONSTANT")
                return $"Hardcoded {(fieldName.Contains("Iteration") ? "iteration" : "area")} path for all {rallyField.TypeDefinition}";

            if (transformation == "RALLY_ID_FORMAT")
                return "Maps Rally Plan Estimate to ADO Story Points";

            if (transformation == "ENUM_MAPPING" && fieldName == "Priority")
                return "Maps Rally priority values to ADO 1-4 scale";

            if (transformation == "ENUM_MAPPING" && fieldName == "Severity")
                return "Maps Rally severity to ADO severity levels";

            return null;
        }

        /// <summary>
        /// Check if this is an important system field that should be included even if marked as Skip
        /// </summary>
        private bool IsImportantSystemField(string fieldName)
        {
            var importantFields = new[]
            {
                "FormattedID", "Name", "Description", "State", "Owner",
                "Priority", "Severity", "CreationDate", "LastUpdateDate",
                "Project", "Iteration", "Tags"
            };

            return importantFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
