using System;
using System.Collections.Generic;

namespace Rally_to_ADO_Migration.Models
{
    public class FieldMappingConfiguration
    {
        public string Version { get; set; }
        public DateTime GeneratedDate { get; set; }
        public List<WorkItemTypeMapping> WorkItemTypeMappings { get; set; }
        public string DefaultAdoProject { get; set; }
        public Dictionary<string, string> AreaPathMappings { get; set; }
        public string Description { get; set; }
        public string MigrationUserEmail { get; set; } // Email to assign Rally users that don't exist in ADO

        public FieldMappingConfiguration()
        {
            Version = "1.0";
            GeneratedDate = DateTime.Now;
            WorkItemTypeMappings = new List<WorkItemTypeMapping>();
            AreaPathMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "EMIS | Emerson", "Acute Meds Management\\Emerson\\Rally Migration" },
                { "Emerson", "Acute Meds Management\\Emerson\\Rally Migration" }
            };
            DefaultAdoProject = "Acute Meds Management";
            Description = "Comprehensive Rally to ADO field mapping - Auto-generated with intelligent matching";
        }
    }

    public class WorkItemTypeMapping
    {
        public string RallyWorkItemType { get; set; }
        public string AdoWorkItemType { get; set; }
        public List<FieldMapping> FieldMappings { get; set; }

        public WorkItemTypeMapping()
        {
            FieldMappings = new List<FieldMapping>();
        }
    }
}