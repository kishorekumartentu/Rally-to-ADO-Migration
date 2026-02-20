using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Service to run comprehensive ADO field discovery
    /// </summary>
    public static class AdoFieldDiscoveryRunner
    {
        /// <summary>
        /// Run comprehensive ADO field discovery and generate mapping files
        /// </summary>
        public static async Task RunComprehensiveAdoFieldDiscoveryAsync(ConnectionSettings settings, LoggingService loggingService)
        {
            try
            {
                loggingService.LogInfo("Starting FAST Complete ADO Field Discovery");
                
                // Use the FAST Complete discovery service for speed
                var discoveryService = new FastCompleteAdoFieldDiscoveryService(loggingService);
                
                // Run fast complete field discovery
                var result = await discoveryService.DiscoverAllAdoFieldsFastAsync(settings);
                
                // Generate summary report
                var summary = GenerateDiscoverySummary(result);
                loggingService.LogInfo(summary);

                // Show completion message
                var message = $@"
? FAST Complete ADO Field Discovery Completed Successfully!

?? RESULTS:
• Work Item Types: {result.WorkItemTypeFields.Count}
• Total Unique Fields: {result.GetAllUniqueFields().Count}

?? FILES GENERATED:
• ADO_Fast_Complete_Discovery_[timestamp].json - Complete field definitions

? PERFORMANCE:
? Processes ALL work item types concurrently for maximum speed
? Optimized parsing algorithms

?? NEXT STEPS:
1. Review the JSON file to verify field discovery
2. Create FieldMappingConfiguration.json from template
3. Customize Rally ? ADO field mappings as needed

Would you like to open the directory containing the generated files?";

                var dialogResult = MessageBox.Show(message, "ADO Field Discovery Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                
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
                loggingService.LogError("FAST Complete ADO field discovery failed", ex);
                MessageBox.Show($"FAST Complete ADO Field Discovery failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Generate a summary of the discovery results
        /// </summary>
        private static string GenerateDiscoverySummary(AdoFieldDiscoveryResult result)
        {
            var summary = new System.Text.StringBuilder();
            
            summary.AppendLine("=== ADO FIELD DISCOVERY SUMMARY ===");
            summary.AppendLine();
            
            summary.AppendLine($"STATISTICS:");
            summary.AppendLine($"  Work Item Types: {result.WorkItemTypeFields.Count}");
            summary.AppendLine($"  Total Unique Fields: {result.GetAllUniqueFields().Count}");
            summary.AppendLine($"  Global Fields: {result.GlobalFields?.Count ?? 0}");
            summary.AppendLine();
            
            summary.AppendLine($"WORK ITEM TYPES DISCOVERED:");
            foreach (var wit in result.WorkItemTypeFields)
            {
                summary.AppendLine($"  • {wit.Key}: {wit.Value.Count} fields");
            }
            summary.AppendLine();
            
            summary.AppendLine($"COMMON FIELDS FOUND:");
            var allFields = result.GetAllUniqueFields();
            var commonFields = new[] { "System.Title", "System.Description", "System.State", "System.AssignedTo", "Microsoft.VSTS.Common.Priority" };
            
            foreach (var commonField in commonFields)
            {
                var found = allFields.Exists(f => f.ReferenceName == commonField);
                summary.AppendLine($"  {(found ? "?" : "?")} {commonField}");
            }
            
            return summary.ToString();
        }

        /// <summary>
        /// Quick test to verify ADO REST API access
        /// </summary>
        public static async Task<string> TestAdoRestApiAccessAsync(ConnectionSettings settings, LoggingService loggingService)
        {
            try
            {
                loggingService.LogInfo("Testing ADO REST API Access...");
                
                // Use the fast discovery service for testing
                var discoveryService = new FastCompleteAdoFieldDiscoveryService(loggingService);
                
                // Just try to get work item types to test API access
                var result = await discoveryService.DiscoverAllAdoFieldsFastAsync(settings);
                
                var testResult = new System.Text.StringBuilder();
                testResult.AppendLine("=== ADO REST API ACCESS TEST ===");
                testResult.AppendLine();
                
                if (result.WorkItemTypeFields.Any())
                {
                    testResult.AppendLine("? SUCCESS: ADO REST API access is working!");
                    testResult.AppendLine();
                    testResult.AppendLine($"Found {result.WorkItemTypeFields.Count} work item types:");
                    
                    foreach (var wit in result.WorkItemTypeFields)
                    {
                        testResult.AppendLine($"  ?? {wit.Key}: {wit.Value.Count} fields");
                        
                        // Show sample fields
                        var sampleFields = wit.Value.Take(5).Select(f => f.ReferenceName);
                        testResult.AppendLine($"    Sample fields: {string.Join(", ", sampleFields)}");
                    }
                    
                    testResult.AppendLine();
                    testResult.AppendLine("? This confirms that:");
                    testResult.AppendLine("  ? Your ADO connection settings are correct");
                    testResult.AppendLine("  ? Personal Access Token has proper permissions");  
                    testResult.AppendLine("  ? Work Item Tracking REST API is accessible");
                }
                else
                {
                    testResult.AppendLine("? FAILED: No work item types or fields found");
                    testResult.AppendLine();
                    testResult.AppendLine("This indicates:");
                    testResult.AppendLine("  ?? Connection settings may be incorrect");
                    testResult.AppendLine("  ?? Personal Access Token may lack permissions");
                    testResult.AppendLine("  ?? Project name may be incorrect");
                }
                
                return testResult.ToString();
            }
            catch (Exception ex)
            {
                loggingService.LogError("ADO REST API access test failed", ex);
                return $"? ADO REST API ACCESS TEST FAILED: {ex.Message}";
            }
        }
    }
}