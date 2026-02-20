using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Service to run comprehensive Rally field discovery
    /// </summary>
    public static class RallyFieldDiscoveryRunner
    {
        /// <summary>
        /// Run comprehensive Rally field discovery and generate mapping files
        /// </summary>
        public static async Task RunComprehensiveRallyFieldDiscoveryAsync(ConnectionSettings settings, LoggingService loggingService)
        {
            try
            {
                loggingService.LogInfo("Starting Rally Field Discovery");
                
                // Use the Rally discovery service
                var discoveryService = new RallyFieldDiscoveryService(loggingService);
                
                // Run Rally field discovery
                var result = await discoveryService.DiscoverAllRallyFieldsAsync(settings);
                
                // Generate summary report
                var summary = GenerateDiscoverySummary(result);
                loggingService.LogInfo(summary);

                // Show completion message
                var message = $@"
?? Rally Field Discovery Completed Successfully!

?? RESULTS:
• Work Item Types: {result.WorkItemTypeFields.Count}
• Total Unique Fields: {result.GetAllUniqueFields().Count}

?? FILES GENERATED:
• Rally_Field_Discovery_[timestamp].json - Complete field definitions

? PERFORMANCE:
? Processes ALL work item types concurrently for maximum speed
? Optimized parsing algorithms

?? NEXT STEPS:
1. Review the JSON file to verify field discovery
2. Compare with ADO_Field_Discovery JSON to plan mappings
3. Update FieldMappingConfiguration.json with Rally ? ADO mappings

Would you like to open the directory containing the generated files?";

                var dialogResult = MessageBox.Show(message, "Rally Field Discovery Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                
                if (dialogResult == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", System.IO.Directory.GetCurrentDirectory());
                    }
                    catch (Exception ex)
                    {
                        loggingService.LogWarning($"Could not open directory: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                loggingService.LogError("Rally field discovery failed", ex);
                MessageBox.Show($"Rally Field Discovery failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Generate a summary of the discovery results
        /// </summary>
        private static string GenerateDiscoverySummary(RallyFieldDiscoveryResult result)
        {
            var summary = new System.Text.StringBuilder();
            
            summary.AppendLine("=== RALLY FIELD DISCOVERY SUMMARY ===");
            summary.AppendLine();
            
            summary.AppendLine($"STATISTICS:");
            summary.AppendLine($"  Work Item Types: {result.WorkItemTypeFields.Count}");
            summary.AppendLine($"  Total Unique Fields: {result.GetAllUniqueFields().Count}");
            summary.AppendLine();
            
            summary.AppendLine($"WORK ITEM TYPES:");
            foreach (var typeMapping in result.WorkItemTypeFields.OrderBy(kvp => kvp.Key))
            {
                summary.AppendLine($"  • {typeMapping.Key}: {typeMapping.Value.Count} fields");
            }
            summary.AppendLine();
            
            summary.AppendLine($"FIELD BREAKDOWN:");
            var allFields = result.GetAllUniqueFields();
            var requiredFields = allFields.Count(f => f.Required);
            var customFields = allFields.Count(f => f.Custom);
            var readOnlyFields = allFields.Count(f => f.ReadOnly);
            
            summary.AppendLine($"  Required Fields: {requiredFields}");
            summary.AppendLine($"  Custom Fields: {customFields}");
            summary.AppendLine($"  Read-Only Fields: {readOnlyFields}");
            summary.AppendLine($"  Standard Fields: {allFields.Count - customFields}");
            summary.AppendLine();
            
            summary.AppendLine($"COMMON RALLY FIELDS FOUND:");
            var commonFields = new[] { "Name", "Description", "FormattedID", "ObjectID", "State", "Owner", "CreationDate", "LastUpdateDate" };
            foreach (var commonField in commonFields)
            {
                var found = allFields.Any(f => f.ElementName.Equals(commonField, StringComparison.OrdinalIgnoreCase));
                summary.AppendLine($"  {(found ? "?" : "?")} {commonField}");
            }
            
            return summary.ToString();
        }
    }
}
