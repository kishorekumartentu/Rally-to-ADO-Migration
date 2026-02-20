using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService
    {
        /// <summary>
        /// Fetch Test Cases linked to a Rally work item (User Story, Defect, etc.)
        /// </summary>
        public async Task<List<string>> FetchLinkedTestCasesAsync(RallyWorkItem workItem, string originalJson)
        {
            var testCaseIds = new List<string>();
            
            try
            {
                // Extract TestCases reference from the original JSON
                var testCasesRefMatch = System.Text.RegularExpressions.Regex.Match(
                    originalJson, 
                    "\"TestCases\"\\s*:\\s*\\{[^}]*\"_ref\"\\s*:\\s*\"([^\"]+)\"[^}]*\"Count\"\\s*:\\s*(\\d+)"
                );
                
                if (testCasesRefMatch.Success && int.Parse(testCasesRefMatch.Groups[2].Value) > 0)
                {
                    var testCasesUrl = testCasesRefMatch.Groups[1].Value + "?fetch=ObjectID,FormattedID";
                    var count = testCasesRefMatch.Groups[2].Value;
                    
                    _loggingService.LogInfo($"Found {count} Test Cases linked to {workItem.FormattedID}");
                    _loggingService.LogDebug($"Fetching Test Cases from: {testCasesUrl}");
                    
                    testCaseIds = await FetchObjectIDsFromUrl(testCasesUrl);
                    
                    if (testCaseIds.Any())
                    {
                        _loggingService.LogInfo($"? Fetched {testCaseIds.Count} Test Case IDs for {workItem.FormattedID}");
                    }
                }
                else
                {
                    _loggingService.LogDebug($"No Test Cases linked to {workItem.FormattedID}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Failed to fetch Test Cases for {workItem.FormattedID}: {ex.Message}");
            }
            
            return testCaseIds;
        }
        
        /// <summary>
        /// Parse TestCase-specific fields during work item parsing
        /// </summary>
        private void ParseTestCaseFields(RallyWorkItem item, string json)
        {
            try
            {
                // Extract Method (Manual/Automated)
                var method = ExtractJsonValue(json, "Method");
                if (!string.IsNullOrEmpty(method))
                {
                    item.CustomFields["Method"] = method;
                    _loggingService.LogDebug($"TestCase Method: {method}");
                }
                
                // Extract Type (Functional, Integration, etc.)
                var type = ExtractJsonValue(json, "Type");
                if (!string.IsNullOrEmpty(type))
                {
                    item.CustomFields["TestCaseType"] = type;
                    _loggingService.LogDebug($"TestCase Type: {type}");
                }
                
                // Extract WorkProduct (linked User Story/Defect)
                var workProductJson = ExtractJsonValue(json, "WorkProduct");
                if (!string.IsNullOrEmpty(workProductJson) && workProductJson != "null")
                {
                    var workProductId = ExtractJsonValue(workProductJson, "ObjectID");
                    if (!string.IsNullOrEmpty(workProductId))
                    {
                        item.CustomFields["WorkProduct"] = workProductId;
                        _loggingService.LogDebug($"TestCase WorkProduct: {workProductId}");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error parsing TestCase fields: {ex.Message}");
            }
        }
    }
}
