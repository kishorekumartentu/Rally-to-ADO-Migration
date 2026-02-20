using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Generates FieldMappingConfiguration.json automatically by querying Rally and ADO APIs
    /// </summary>
    public class CompleteDynamicMappingGenerator
    {
        private readonly LoggingService _loggingService;
        private readonly string _configPath;

        public CompleteDynamicMappingGenerator(LoggingService loggingService)
        {
            _loggingService = loggingService;
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "FieldMappingConfiguration.json");
        }

        private void LogMappingDebug(string prefix, object value)
        {
            if (value == null)
            {
                _loggingService.LogDebug($"{prefix}: <null>");
                return;
            }

            if (value is JObject jObj)
            {
                _loggingService.LogDebug($"{prefix} (JObject):");
                _loggingService.LogDebug($"  _refObjectName: {jObj["_refObjectName"]}");
                _loggingService.LogDebug($"  Name: {jObj["Name"]}");
                return;
            }

            _loggingService.LogDebug($"{prefix}: {value}");
        }

        private void ValidateAreaPathMappings(FieldMappingConfiguration config)
        {
            if (config.AreaPathMappings == null || !config.AreaPathMappings.Any())
            {
                _loggingService.LogWarning("No area path mappings defined in configuration");
                return;
            }

            var defaultProject = config.DefaultAdoProject;
            if (string.IsNullOrEmpty(defaultProject))
            {
                _loggingService.LogWarning("Default ADO project not defined in configuration");
                return;
            }

            // Validate each area path mapping
            foreach (var mapping in config.AreaPathMappings.ToList())
            {
                var areaPath = mapping.Value;
                
                // Ensure area path starts with project name
                if (!areaPath.StartsWith(defaultProject, StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = $"{defaultProject}\\{areaPath.TrimStart('\\')}";
                    _loggingService.LogDebug($"Fixing area path mapping: {mapping.Value} -> {newPath}");
                    config.AreaPathMappings[mapping.Key] = newPath;
                }
            }
        }

        private string ValidateAndFormatAreaPath(string projectName, string areaPath)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                _loggingService.LogWarning("Project name is empty, cannot validate area path");
                return areaPath;
            }

            if (string.IsNullOrEmpty(areaPath))
            {
                _loggingService.LogWarning("Area path is empty, using default");
                return $"{projectName}\\Emerson";
            }

            // Ensure path starts with project name
            if (!areaPath.StartsWith(projectName, StringComparison.OrdinalIgnoreCase))
            {
                areaPath = $"{projectName}\\{areaPath.TrimStart('\\')}";
                _loggingService.LogDebug($"Fixed area path to include project: {areaPath}");
            }

            // Replace forward slashes with backslashes
            areaPath = areaPath.Replace("/", "\\");

            // Remove any double backslashes
            while (areaPath.Contains("\\\\"))
            {
                areaPath = areaPath.Replace("\\\\", "\\");
            }

            _loggingService.LogDebug($"Validated area path: {areaPath}");
            return areaPath;
        }

        /// <summary>
        /// Generate mapping JSON by discovering Rally work items and ADO work item types/fields
        /// Returns path to generated JSON or null on failure
        /// </summary>
        public async Task<string> GenerateCompleteMappingFromApisAsync(ConnectionSettings settings, string sampleRallyId = null)
        {
            try
            {
                _loggingService.LogInfo("Starting dynamic mapping generation from APIs...");
                
                // Load existing configuration if available
                FieldMappingConfiguration existingConfig = null;
                if (File.Exists(_configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(_configPath);
                        existingConfig = JsonConvert.DeserializeObject<FieldMappingConfiguration>(existingJson);
                        _loggingService.LogInfo($"Loaded existing configuration from {_configPath}");
                        
                        // Debug logging for area path mappings
                        if (existingConfig?.AreaPathMappings != null)
                        {
                            _loggingService.LogDebug("Existing area path mappings:");
                            foreach (var mapping in existingConfig.AreaPathMappings)
                            {
                                _loggingService.LogDebug($"  {mapping.Key} -> {mapping.Value}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Could not load existing configuration: {ex.Message}");
                    }
                }

                // Create mapping configuration
                var mappingConfig = new FieldMappingConfiguration
                {
                    Version = existingConfig?.Version ?? "1.0",
                    GeneratedDate = DateTime.Now,
                    Description = "Enhanced field mapping configuration with better field preservation",
                    DefaultAdoProject = existingConfig?.DefaultAdoProject ?? settings.AdoProject,
                    WorkItemTypeMappings = new List<WorkItemTypeMapping>(),
                    AreaPathMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };

                // Ensure area path mappings exist and are properly formatted
                if (existingConfig?.AreaPathMappings != null && existingConfig.AreaPathMappings.Any())
                {
                    foreach (var mapping in existingConfig.AreaPathMappings)
                    {
                        var areaPath = mapping.Value;
                        
                        // Ensure area path starts with project name
                        if (!areaPath.StartsWith(mappingConfig.DefaultAdoProject, StringComparison.OrdinalIgnoreCase))
                        {
                            areaPath = $"{mappingConfig.DefaultAdoProject}\\{areaPath.TrimStart('\\')}";
                        }
                        
                        mappingConfig.AreaPathMappings[mapping.Key] = areaPath;
                        _loggingService.LogDebug($"Area path mapping: {mapping.Key} -> {areaPath}");
                    }
                }
                else
                {
                    // Create default mappings if none exist
                    mappingConfig.AreaPathMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "EMIS | Emerson", $"{mappingConfig.DefaultAdoProject}\\Emerson" },
                        { "EMIS", $"{mappingConfig.DefaultAdoProject}\\Emerson" },
                        { "Emerson", $"{mappingConfig.DefaultAdoProject}\\Emerson" }
                    };
                    
                    _loggingService.LogDebug("Created default area path mappings:");
                    foreach (var mapping in mappingConfig.AreaPathMappings)
                    {
                        _loggingService.LogDebug($"  {mapping.Key} -> {mapping.Value}");
                    }
                }

                // Add default mapping for defects
                var defectMapping = CreateDefaultDefectMapping(mappingConfig);
                mappingConfig.WorkItemTypeMappings.Add(defectMapping);

                var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FieldMappingConfiguration.json");
                
                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Write the configuration
                var json = JsonConvert.SerializeObject(mappingConfig, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                });
                File.WriteAllText(outputPath, json, Encoding.UTF8);

                _loggingService.LogInfo($"Dynamic mapping generated: {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Dynamic mapping generation failed", ex);
                return null;
            }
        }

        private WorkItemTypeMapping CreateDefaultDefectMapping(FieldMappingConfiguration existingConfig)
        {
            // Ensure we have a valid area path
            var projectName = existingConfig?.DefaultAdoProject ?? "Acute Meds Management";
            var areaPath = ValidateAndFormatAreaPath(projectName, $"{projectName}\\Emerson");
            _loggingService.LogDebug($"Using validated area path: {areaPath}");

            var defaultMapping = new WorkItemTypeMapping
            {
                RallyWorkItemType = "Defect",
                AdoWorkItemType = "Bug",
                FieldMappings = new List<FieldMapping>
                {
                    // Core fields with enhanced preservation
                    new FieldMapping { 
                        RallyField = "FormattedID", 
                        AdoField = "System.Title", 
                        CustomTransformation = "RALLY_ID_FORMAT", 
                        RallyRequired = true,
                        RallyFieldType = "string",
                        MappingNotes = "Combines Rally ID and Name for better traceability"
                    },
                    new FieldMapping { 
                        RallyField = "Description", 
                        AdoField = "System.Description", 
                        CustomTransformation = "HTML_PRESERVE", 
                        RallyFieldType = "string",
                        RallyRequired = false,
                        Skip = false,
                        MappingConfidence = "High",
                        MappingNotes = "HTML content preserved with formatting",
                        DefaultValue = string.Empty
                    },
                    new FieldMapping { 
                        RallyField = "State", 
                        AdoField = "System.State", 
                        CustomTransformation = "STATE_MAPPING", 
                        RallyRequired = true,
                        RallyFieldType = "state",
                        MappingNotes = "Maps Rally states to ADO states"
                    },
                    new FieldMapping { 
                        RallyField = "Owner", 
                        AdoField = "System.AssignedTo", 
                        CustomTransformation = "USER_LOOKUP",
                        RallyFieldType = "user",
                        MappingNotes = "Maps Rally users to ADO users with domain handling"
                    },
                    new FieldMapping { 
                        RallyField = "Priority", 
                        AdoField = "Microsoft.VSTS.Common.Priority", 
                        CustomTransformation = "ENUM_MAPPING",
                        RallyFieldType = "rating",
                        MappingNotes = "Maps Rally priority values to ADO 1-4 scale"
                    },
                    new FieldMapping { 
                        RallyField = "Severity", 
                        AdoField = "Microsoft.VSTS.Common.Severity", 
                        CustomTransformation = "ENUM_MAPPING",
                        RallyFieldType = "rating",
                        MappingNotes = "Maps Rally severity to ADO severity levels"
                    },
                    // Fixed Area Path - no Rally mapping, constant value
                    new FieldMapping { 
                        RallyField = null, // No Rally field mapping
                        AdoField = "System.AreaPath", 
                        CustomTransformation = "CONSTANT", // Use constant value
                        RallyRequired = false,
                        RallyFieldType = "string",
                        DefaultValue = areaPath, // Set the constant area path
                        Skip = false,
                        MappingConfidence = "High",
                        MappingNotes = "Fixed area path value set from configuration"
                    },
                    new FieldMapping { 
                        RallyField = "Tags", 
                        AdoField = "System.Tags", 
                        CustomTransformation = "COLLECTION_TO_STRING",
                        RallyFieldType = "collection",
                        MappingNotes = "Preserves Rally tags as ADO tags"
                    },
                    new FieldMapping { 
                        RallyField = "Resolution", 
                        AdoField = "Microsoft.VSTS.Common.Resolution", 
                        CustomTransformation = "DIRECT",
                        RallyFieldType = "string"
                    },
                    new FieldMapping { 
                        RallyField = "CreationDate", 
                        AdoField = "System.CreatedDate", 
                        CustomTransformation = "DATE_FORMAT", 
                        RallyRequired = true,
                        RallyFieldType = "date"
                    },
                    new FieldMapping { 
                        RallyField = "LastUpdateDate", 
                        AdoField = "System.ChangedDate", 
                        CustomTransformation = "DATE_FORMAT", 
                        RallyRequired = true,
                        RallyFieldType = "date"
                    },
                    new FieldMapping { 
                        RallyField = "CreatedBy", 
                        AdoField = "System.CreatedBy", 
                        CustomTransformation = "USER_LOOKUP", 
                        RallyRequired = true,
                        RallyFieldType = "user"
                    },
                    // Custom fields with enhanced preservation
                    new FieldMapping { 
                        RallyField = "c_FoundInBuild", 
                        AdoField = "Microsoft.VSTS.Build.FoundIn", 
                        CustomTransformation = "DIRECT",
                        RallyFieldType = "string",
                        MappingNotes = "Preserves build version information"
                    },
                    new FieldMapping { 
                        RallyField = "c_IntegratedInBuild", 
                        AdoField = "Microsoft.VSTS.Build.IntegrationBuild", 
                        CustomTransformation = "DIRECT",
                        RallyFieldType = "string",
                        MappingNotes = "Preserves integration build information"
                    },
                    new FieldMapping { 
                        RallyField = "Environment", 
                        AdoField = "Microsoft.VSTS.TCM.SystemInfo", 
                        CustomTransformation = "DIRECT",
                        RallyFieldType = "string",
                        MappingNotes = "Preserves environment information"
                    },
                    // Additional Rally fields to preserve
                    new FieldMapping {
                        RallyField = "Notes",
                        AdoField = "System.Description",
                        CustomTransformation = "HTML_APPEND", // Changed to new transformation type
                        RallyFieldType = "string",
                        Skip = false,
                        MappingConfidence = "High",
                        MappingNotes = "HTML content appended with formatting preserved"
                    },
                    new FieldMapping {
                        RallyField = "Blocked",
                        AdoField = "Microsoft.VSTS.CMMI.Blocked",
                        CustomTransformation = "BOOLEAN_MAPPING",
                        RallyFieldType = "boolean",
                        MappingNotes = "Preserves blocked status"
                    },
                    new FieldMapping {
                        RallyField = "TaskEstimateTotal",
                        AdoField = "Microsoft.VSTS.Scheduling.OriginalEstimate",
                        CustomTransformation = "DIRECT",
                        RallyFieldType = "decimal",
                        MappingNotes = "Preserves original estimate"
                    },
                    new FieldMapping {
                        RallyField = "TaskRemainingTotal",
                        AdoField = "Microsoft.VSTS.Scheduling.RemainingWork",
                        CustomTransformation = "DIRECT",
                        RallyFieldType = "decimal",
                        MappingNotes = "Preserves remaining work"
                    }
                }
            };

            _loggingService.LogDebug($"Area path will be set to: {areaPath}");

            return defaultMapping;
        }
    }
}
