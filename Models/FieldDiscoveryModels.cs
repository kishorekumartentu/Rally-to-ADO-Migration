using System;
using System.Collections.Generic;

namespace Rally_to_ADO_Migration.Models
{
    /// <summary>
    /// Rally work item schema definition
    /// </summary>
    public class RallyWorkItemSchema
    {
        public string WorkItemType { get; set; }
        public string DisplayName { get; set; }
        public Dictionary<string, RallyFieldDefinition> Fields { get; set; }

        public RallyWorkItemSchema()
        {
            Fields = new Dictionary<string, RallyFieldDefinition>();
        }
    }

    // RallyFieldDefinition moved to DataModels.cs to avoid duplicate

    /// <summary>
    /// Azure DevOps work item schema definition
    /// </summary>
    public class AdoWorkItemSchema
    {
        public string WorkItemType { get; set; }
        public string DisplayName { get; set; }
        public Dictionary<string, AdoFieldDefinition> Fields { get; set; }

        public AdoWorkItemSchema()
        {
            Fields = new Dictionary<string, AdoFieldDefinition>();
        }
    }

    /// <summary>
    /// Azure DevOps field definition
    /// </summary>
    public class AdoFieldDefinition
    {
        public string ReferenceName { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Required { get; set; }
        public string Description { get; set; }
        public string[] AllowedValues { get; set; }
        public string DefaultValue { get; set; }
        public bool CanSortBy { get; set; }
        public bool IsQueryable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsPicklist { get; set; }
        public string Usage { get; set; } // WorkItem, WorkItemLink, Tree, etc.
    }

    /// <summary>
    /// Field mapping matrix containing all work item type mappings
    /// </summary>
    public class FieldMappingMatrix
    {
        public Dictionary<string, WorkItemFieldMappings> WorkItemMappings { get; set; }
        public DateTime GeneratedOn { get; set; }
        public string RallyVersion { get; set; }
        public string AdoVersion { get; set; }
        public List<string> UnmappedRallyFields { get; set; }
        public List<string> UnmappedAdoFields { get; set; }

        public FieldMappingMatrix()
        {
            WorkItemMappings = new Dictionary<string, WorkItemFieldMappings>();
            GeneratedOn = DateTime.Now;
            UnmappedRallyFields = new List<string>();
            UnmappedAdoFields = new List<string>();
        }
    }

    /// <summary>
    /// Field mappings for a specific work item type
    /// </summary>
    public class WorkItemFieldMappings
    {
        public string RallyWorkItemType { get; set; }
        public string AdoWorkItemType { get; set; }
        public List<FieldMappingDefinition> FieldMappings { get; set; }

        public WorkItemFieldMappings()
        {
            FieldMappings = new List<FieldMappingDefinition>();
        }
    }

    /// <summary>
    /// Individual field mapping definition
    /// </summary>
    public class FieldMappingDefinition
    {
        public string RallyField { get; set; }
        public string RallyFieldType { get; set; }
        public string AdoField { get; set; }
        public string AdoFieldType { get; set; }
        public string DataType { get; set; }
        public bool IsRequired { get; set; }
        public double ConfidenceScore { get; set; } // 0.0 to 1.0
        public string MappingReason { get; set; }
        public string ValueTransformation { get; set; } // Optional transformation logic
        public string Notes { get; set; }
        public bool RequiresCustomHandling { get; set; }
    }

    /// <summary>
    /// Field discovery result for analysis
    /// </summary>
    public class FieldDiscoveryResult
    {
        public Dictionary<string, RallyWorkItemSchema> RallySchemas { get; set; }
        public Dictionary<string, AdoWorkItemSchema> AdoSchemas { get; set; }
        public FieldMappingMatrix MappingMatrix { get; set; }
        public List<FieldMappingIssue> Issues { get; set; }
        public FieldDiscoveryStatistics Statistics { get; set; }

        public FieldDiscoveryResult()
        {
            RallySchemas = new Dictionary<string, RallyWorkItemSchema>();
            AdoSchemas = new Dictionary<string, AdoWorkItemSchema>();
            Issues = new List<FieldMappingIssue>();
        }
    }

    /// <summary>
    /// Issues found during field mapping discovery
    /// </summary>
    public class FieldMappingIssue
    {
        public string IssueType { get; set; } // "NoMatch", "TypeMismatch", "RequiredFieldMissing", etc.
        public string WorkItemType { get; set; }
        public string RallyField { get; set; }
        public string AdoField { get; set; }
        public string Description { get; set; }
        public string Recommendation { get; set; }
        public string Severity { get; set; } // "High", "Medium", "Low"
    }

    /// <summary>
    /// Statistics about field discovery and mapping
    /// </summary>
    public class FieldDiscoveryStatistics
    {
        public int TotalRallyFields { get; set; }
        public int TotalAdoFields { get; set; }
        public int MappedFields { get; set; }
        public int UnmappedRallyFields { get; set; }
        public int UnmappedAdoFields { get; set; }
        public int HighConfidenceMappings { get; set; }
        public int MediumConfidenceMappings { get; set; }
        public int LowConfidenceMappings { get; set; }
        public double OverallMappingScore { get; set; }
        public Dictionary<string, int> MappingsByWorkItemType { get; set; }

        public FieldDiscoveryStatistics()
        {
            MappingsByWorkItemType = new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// Configuration for field discovery process
    /// </summary>
    public class FieldDiscoveryConfiguration
    {
        public bool IncludeSystemFields { get; set; } = true;
        public bool IncludeCustomFields { get; set; } = true;
        public bool IncludeReadOnlyFields { get; set; } = false;
        public double MinimumConfidenceThreshold { get; set; } = 0.5;
        public string[] ExcludedFieldPatterns { get; set; }
        public string[] RequiredMappings { get; set; }
        public bool GenerateCode { get; set; } = true;
        public bool GenerateJson { get; set; } = true;
        public bool GenerateReport { get; set; } = true;

        public FieldDiscoveryConfiguration()
        {
            ExcludedFieldPatterns = new string[] { "Rev", "Changeset", "_" };
            RequiredMappings = new string[] { "Name", "Description", "State", "Owner" };
        }
    }
}