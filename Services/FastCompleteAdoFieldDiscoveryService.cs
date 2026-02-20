using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Fast and efficient ADO field discovery that processes ALL work item types quickly
    /// </summary>
    public class FastCompleteAdoFieldDiscoveryService
    {
        private readonly HttpClient _httpClient;
        private readonly LoggingService _loggingService;
        private string _apiKey;
        private string _organization;
        private string _project;
        private string _serverUrl;

        public FastCompleteAdoFieldDiscoveryService(LoggingService loggingService)
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            
            _httpClient = new HttpClient(handler);
            _loggingService = loggingService;
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Shorter timeout for speed
        }

        /// <summary>
        /// Fast discovery that gets ALL work item types and their fields efficiently
        /// </summary>
        public async Task<AdoFieldDiscoveryResult> DiscoverAllAdoFieldsFastAsync(ConnectionSettings settings)
        {
            var result = new AdoFieldDiscoveryResult();
            var startTime = DateTime.Now;
            
            try
            {
                ConfigureConnection(settings);
                
                _loggingService.LogInfo("? Starting FAST Complete ADO field discovery");

                // Step 1: Get all work item types quickly
                var allWorkItemTypes = await GetWorkItemTypesQuickAsync();
                if (allWorkItemTypes == null || !allWorkItemTypes.Any())
                {
                    _loggingService.LogError("? Failed to retrieve work item types");
                    return result;
                }

                // Filter to only the 6 mapped types
                var mappedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Task",
                    "Bug",
                    "User Story",
                    "Epic",
                    "Feature",
                    "Test Case"
                };
                
                var workItemTypes = allWorkItemTypes
                    .Where(wit => mappedTypes.Contains(wit.Name))
                    .ToList();
                
                if (!workItemTypes.Any())
                {
                    _loggingService.LogError("? None of the 6 mapped work item types found (Task, Bug, User Story, Epic, Feature, Test Case)");
                    return result;
                }

                _loggingService.LogInfo($"Found {workItemTypes.Count} mapped work item types to process (filtered from {allWorkItemTypes.Count} total)");

                // Step 2: Process all work item types concurrently for speed
                var tasks = new List<Task<KeyValuePair<string, List<AdoFieldInfo>>>>();
                
                foreach (var workItemType in workItemTypes)
                {
                    var task = ProcessWorkItemTypeAsync(workItemType);
                    tasks.Add(task);
                }

                _loggingService.LogInfo($"? Processing all {workItemTypes.Count} work item types concurrently...");
                
                // Wait for all tasks to complete
                var results = await Task.WhenAll(tasks);
                
                // Collect results
                foreach (var kvp in results.Where(r => r.Value != null && r.Value.Any()))
                {
                    result.WorkItemTypeFields[kvp.Key] = kvp.Value;
                    _loggingService.LogInfo($"  ? {kvp.Key}: {kvp.Value.Count} fields");
                }

                // Step 3: Generate output quickly
                await GenerateFastJsonAsync(result);

                var totalTime = DateTime.Now - startTime;
                _loggingService.LogInfo($"? FAST Complete discovery finished in {totalTime.TotalSeconds:F1} seconds!");
                _loggingService.LogInfo($"  ?? Successfully processed: {result.WorkItemTypeFields.Count} work item types");
                _loggingService.LogInfo($"  ?? Total unique fields: {result.GetAllUniqueFields().Count}");

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("FAST Complete ADO field discovery failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Process a single work item type quickly and return the result
        /// </summary>
        private async Task<KeyValuePair<string, List<AdoFieldInfo>>> ProcessWorkItemTypeAsync(AdoWorkItemTypeInfo workItemType)
        {
            try
            {
                var fields = await GetFieldsForWorkItemTypeFastAsync(workItemType.ReferenceName);
                return new KeyValuePair<string, List<AdoFieldInfo>>(workItemType.Name, fields);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Failed to get fields for {workItemType.Name}: {ex.Message}");
                return new KeyValuePair<string, List<AdoFieldInfo>>(workItemType.Name, new List<AdoFieldInfo>());
            }
        }

        /// <summary>
        /// Get work item types quickly with minimal parsing
        /// </summary>
        private async Task<List<AdoWorkItemTypeInfo>> GetWorkItemTypesQuickAsync()
        {
            try
            {
                var url = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes?api-version=6.0");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return ParseWorkItemTypesQuick(content);
                }
                else
                {
                    _loggingService.LogError($"Failed to get work item types: {response.StatusCode}");
                    return new List<AdoWorkItemTypeInfo>();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Exception getting work item types", ex);
                return new List<AdoWorkItemTypeInfo>();
            }
        }

        /// <summary>
        /// Get fields for a work item type with fast, simple parsing
        /// </summary>
        private async Task<List<AdoFieldInfo>> GetFieldsForWorkItemTypeFastAsync(string workItemTypeReferenceName)
        {
            try
            {
                var encodedReferenceName = Uri.EscapeDataString(workItemTypeReferenceName);
                var url = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes/{encodedReferenceName}/fields?api-version=6.0");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return ParseFieldDefinitionsFast(content);
                }
                else
                {
                    // Try with simple name as fallback
                    var simpleName = workItemTypeReferenceName.Split('.').LastOrDefault();
                    if (!string.IsNullOrEmpty(simpleName) && simpleName != workItemTypeReferenceName)
                    {
                        var fallbackUrl = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes/{Uri.EscapeDataString(simpleName)}/fields?api-version=6.0");
                        var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
                        AddAuthenticationHeader(fallbackRequest);
                        
                        var fallbackResponse = await _httpClient.SendAsync(fallbackRequest);
                        if (fallbackResponse.IsSuccessStatusCode)
                        {
                            var fallbackContent = await fallbackResponse.Content.ReadAsStringAsync();
                            return ParseFieldDefinitionsFast(fallbackContent);
                        }
                    }
                    
                    return new List<AdoFieldInfo>();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogDebug($"Exception getting fields for {workItemTypeReferenceName}: {ex.Message}");
                return new List<AdoFieldInfo>();
            }
        }

        /// <summary>
        /// Quick parsing of work item types - optimized for speed
        /// </summary>
        private List<AdoWorkItemTypeInfo> ParseWorkItemTypesQuick(string json)
        {
            var workItemTypes = new List<AdoWorkItemTypeInfo>();
            
            try
            {
                // Fast regex-based parsing
                var pattern = @"\{[^}]*""name""\s*:\s*""([^""]+)""[^}]*""referenceName""\s*:\s*""([^""]+)""[^}]*\}";
                var matches = Regex.Matches(json, pattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    workItemTypes.Add(new AdoWorkItemTypeInfo
                    {
                        Name = match.Groups[1].Value,
                        ReferenceName = match.Groups[2].Value
                    });
                }
                
                // Alternative pattern if the first didn't work
                if (workItemTypes.Count == 0)
                {
                    var altPattern = @"""name""\s*:\s*""([^""]+)"".*?""referenceName""\s*:\s*""([^""]+)""";
                    var altMatches = Regex.Matches(json, altPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    
                    foreach (Match match in altMatches)
                    {
                        workItemTypes.Add(new AdoWorkItemTypeInfo
                        {
                            Name = match.Groups[1].Value,
                            ReferenceName = match.Groups[2].Value
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error parsing work item types quickly", ex);
            }
            
            return workItemTypes;
        }

        /// <summary>
        /// Fast parsing of field definitions - optimized for speed
        /// </summary>
        private List<AdoFieldInfo> ParseFieldDefinitionsFast(string json)
        {
            var fields = new List<AdoFieldInfo>();
            
            try
            {
                // Fast regex pattern to extract all field info at once
                var pattern = @"""referenceName""\s*:\s*""([^""]+)""[^}]*""name""\s*:\s*""([^""]+)""[^}]*""type""\s*:\s*""([^""]+)""";
                var matches = Regex.Matches(json, pattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    var referenceName = match.Groups[1].Value;
                    if (IsValidFieldReferenceName(referenceName))
                    {
                        fields.Add(new AdoFieldInfo
                        {
                            ReferenceName = referenceName,
                            Name = match.Groups[2].Value,
                            Type = match.Groups[3].Value
                        });
                    }
                }
                
                // Alternative pattern if first didn't work well
                if (fields.Count < 10) // If we got very few fields, try alternative
                {
                    var altPattern = @"""referenceName""\s*:\s*""([^""]+)""";
                    var refMatches = Regex.Matches(json, altPattern);
                    
                    foreach (Match refMatch in refMatches)
                    {
                        var referenceName = refMatch.Groups[1].Value;
                        if (IsValidFieldReferenceName(referenceName))
                        {
                            var field = new AdoFieldInfo
                            {
                                ReferenceName = referenceName,
                                Name = GetDisplayNameFromReferenceName(referenceName),
                                Type = "string"
                            };
                            
                            // Try to find actual name and type nearby
                            var context = json.Substring(Math.Max(0, refMatch.Index - 200), 
                                                        Math.Min(400, json.Length - Math.Max(0, refMatch.Index - 200)));
                            
                            var nameMatch = Regex.Match(context, @"""name""\s*:\s*""([^""]+)""");
                            if (nameMatch.Success) field.Name = nameMatch.Groups[1].Value;
                            
                            var typeMatch = Regex.Match(context, @"""type""\s*:\s*""([^""]+)""");
                            if (typeMatch.Success) field.Type = typeMatch.Groups[1].Value;
                            
                            fields.Add(field);
                        }
                    }
                }
                
                return fields.GroupBy(f => f.ReferenceName).Select(g => g.First()).ToList();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error parsing field definitions quickly", ex);
                return new List<AdoFieldInfo>();
            }
        }

        private bool IsValidFieldReferenceName(string referenceName)
        {
            return !string.IsNullOrEmpty(referenceName) && 
                   (referenceName.StartsWith("System.") || 
                    referenceName.StartsWith("Microsoft.VSTS.") || 
                    referenceName.StartsWith("Custom."));
        }

        private string GetDisplayNameFromReferenceName(string referenceName)
        {
            if (string.IsNullOrEmpty(referenceName)) return "";
            var lastPart = referenceName.Substring(referenceName.LastIndexOf('.') + 1);
            return Regex.Replace(lastPart, "([a-z])([A-Z])", "$1 $2");
        }

        /// <summary>
        /// Generate JSON output quickly
        /// </summary>
        private async Task GenerateFastJsonAsync(AdoFieldDiscoveryResult result)
        {
            try
            {
                var jsonBuilder = new StringBuilder();
                jsonBuilder.AppendLine("{");
                jsonBuilder.AppendLine($"  \"GeneratedOn\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",");
                jsonBuilder.AppendLine($"  \"Organization\": \"{_organization}\",");
                jsonBuilder.AppendLine($"  \"Project\": \"{_project}\",");
                jsonBuilder.AppendLine($"  \"Description\": \"FAST Complete ADO field discovery - ALL work item types processed quickly\",");
                jsonBuilder.AppendLine("  \"WorkItemTypes\": {");
                
                var witEntries = result.WorkItemTypeFields.ToList();
                for (int i = 0; i < witEntries.Count; i++)
                {
                    var wit = witEntries[i];
                    jsonBuilder.AppendLine($"    \"{EscapeJsonString(wit.Key)}\": [");
                    
                    for (int j = 0; j < wit.Value.Count; j++)
                    {
                        var field = wit.Value[j];
                        jsonBuilder.AppendLine("      {");
                        jsonBuilder.AppendLine($"        \"ReferenceName\": \"{EscapeJsonString(field.ReferenceName)}\",");
                        jsonBuilder.AppendLine($"        \"Name\": \"{EscapeJsonString(field.Name)}\",");
                        jsonBuilder.AppendLine($"        \"Type\": \"{EscapeJsonString(field.Type)}\"");
                        jsonBuilder.AppendLine(j < wit.Value.Count - 1 ? "      }," : "      }");
                    }
                    
                    jsonBuilder.AppendLine(i < witEntries.Count - 1 ? "    ]," : "    ]");
                }
                
                jsonBuilder.AppendLine("  },");
                jsonBuilder.AppendLine("  \"Statistics\": {");
                jsonBuilder.AppendLine($"    \"TotalWorkItemTypes\": {result.WorkItemTypeFields.Count},");
                jsonBuilder.AppendLine($"    \"TotalUniqueFields\": {result.GetAllUniqueFields().Count}");
                jsonBuilder.AppendLine("  }");
                jsonBuilder.AppendLine("}");
                
                var json = jsonBuilder.ToString();
                var fileName = "ADOFieldDiscovery.json";
                
                await Task.Run(() => File.WriteAllText(fileName, json));

                _loggingService.LogInfo($"? Fast complete discovery saved to: {fileName}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to generate fast JSON", ex);
            }
        }

        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private void ConfigureConnection(ConnectionSettings settings)
        {
            _apiKey = settings.AdoApiKey?.Trim();
            _organization = settings.AdoOrganization?.Trim();
            _project = settings.AdoProject?.Trim();
            _serverUrl = settings.AdoServerUrl?.TrimEnd('/') ?? "https://dev.azure.com";
        }

        private string BuildApiUrl(string apiPath)
        {
            if (_serverUrl.Contains(".visualstudio.com"))
            {
                return $"{_serverUrl}/{apiPath}";
            }
            else
            {
                return $"{_serverUrl}/{_organization}/{apiPath}";
            }
        }

        private void AddAuthenticationHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_apiKey))
            {
                var authString = $":{_apiKey}";
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
            
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}