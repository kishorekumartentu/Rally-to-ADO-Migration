using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Enhanced ADO field discovery using Work Item Tracking REST API
    /// Gets comprehensive field definitions for all work item types
    /// </summary>
    public class AdoFieldDiscoveryService
    {
        private readonly HttpClient _httpClient;
        private readonly LoggingService _loggingService;
        private string _apiKey;
        private string _organization;
        private string _project;
        private string _serverUrl;

        public AdoFieldDiscoveryService(LoggingService loggingService)
        {
            // Create HttpClientHandler to handle compression automatically
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            
            _httpClient = new HttpClient(handler);
            _loggingService = loggingService;
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Get comprehensive field definitions for all work item types using Work Item Tracking REST API
        /// </summary>
        public async Task<AdoFieldDiscoveryResult> DiscoverAllAdoFieldsAsync(ConnectionSettings settings)
        {
            var result = new AdoFieldDiscoveryResult();
            
            try
            {
                ConfigureConnection(settings);
                
                _loggingService.LogInfo("Starting comprehensive ADO field discovery using Work Item Tracking REST API");

                // Step 1: Get all work item types in the project
                var workItemTypes = await GetWorkItemTypesAsync();
                if (workItemTypes == null || !workItemTypes.Any())
                {
                    _loggingService.LogError("Failed to retrieve work item types");
                    return result;
                }

                _loggingService.LogInfo($"Found {workItemTypes.Count} work item types:");
                foreach (var wit in workItemTypes)
                {
                    _loggingService.LogInfo($"  - {wit.Name} ({wit.ReferenceName})");
                }

                // Step 2: Get field definitions for each work item type
                foreach (var workItemType in workItemTypes)
                {
                    _loggingService.LogInfo($"Getting field definitions for: {workItemType.Name}");
                    
                    var fields = await GetFieldsForWorkItemTypeAsync(workItemType.ReferenceName);
                    if (fields != null && fields.Any())
                    {
                        result.WorkItemTypeFields[workItemType.Name] = fields;
                        _loggingService.LogInfo($"  ? Found {fields.Count} fields for {workItemType.Name}");
                    }
                    else
                    {
                        _loggingService.LogWarning($"  ? No fields found for {workItemType.Name}");
                    }
                }

                // Step 3: Get global field definitions (all fields available in the organization)
                _loggingService.LogInfo("Getting global field definitions...");
                var globalFields = await GetAllFieldsAsync();
                if (globalFields != null && globalFields.Any())
                {
                    result.GlobalFields = globalFields;
                    _loggingService.LogInfo($"  ? Found {globalFields.Count} global fields");
                }

                // Step 4: Generate comprehensive field mapping JSON
                await GenerateFieldMappingJsonAsync(result);

                _loggingService.LogInfo($"ADO field discovery completed successfully!");
                _loggingService.LogInfo($"  - Work item types: {result.WorkItemTypeFields.Count}");
                _loggingService.LogInfo($"  - Total unique fields: {result.GetAllUniqueFields().Count}");

                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("ADO field discovery failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Get all work item types in the project
        /// </summary>
        private async Task<List<AdoWorkItemTypeInfo>> GetWorkItemTypesAsync()
        {
            try
            {
                // Require project for project-scoped endpoints
                if (string.IsNullOrEmpty(_project))
                {
                    _loggingService.LogError("ADO Project is not set - cannot request work item types");
                    return null;
                }

                // Use api-version 7.1 to match JS usage for work item GETs
                var projectEscaped = Uri.EscapeDataString(_project);
                var url = BuildApiUrl($"{projectEscaped}/_apis/wit/workitemtypes?api-version=7.1");
                _loggingService.LogInfo($"Requesting work item types from: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                // Log request and send
                _loggingService.LogInfo($"ADO Request: {request.Method} {request.RequestUri}");
                var response = await _httpClient.SendAsync(request);

                var content = await response.Content.ReadAsStringAsync();
                _loggingService.LogInfo($"ADO Response: {response.StatusCode} - preview: { (string.IsNullOrEmpty(content) ? "(empty)" : content.Substring(0, Math.Min(500, content.Length))) }");

                if (response.IsSuccessStatusCode)
                {
                    _loggingService.LogInfo($"ADO API Response length: {content.Length} characters");
                    _loggingService.LogInfo($"ADO API Response preview: { (content.Length==0?"(empty)": content.Substring(0, Math.Min(500, content.Length))) }...");

                    if (content.Length > 500)
                    {
                        _loggingService.LogDebug($"Full ADO API Response: {content}");
                    }

                    var parsedTypes = ParseWorkItemTypes(content);
                    _loggingService.LogInfo($"Parsed {parsedTypes?.Count ?? 0} work item types from response");

                    if (parsedTypes == null || parsedTypes.Count == 0)
                    {
                        _loggingService.LogWarning("Primary parsing failed, trying alternative parsing methods...");
                        parsedTypes = ParseWorkItemTypesAlternative(content);
                        _loggingService.LogInfo($"Alternative parsing found {parsedTypes?.Count ?? 0} work item types");
                    }

                    return parsedTypes;
                }
                else
                {
                    _loggingService.LogError($"Failed to get work item types: {response.StatusCode} - {content}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Exception getting work item types", ex);
                return null;
            }
        }

        /// <summary>
        /// Get field definitions for a specific work item type
        /// </summary>
        private async Task<List<AdoFieldInfo>> GetFieldsForWorkItemTypeAsync(string workItemTypeReferenceName)
        {
            try
            {
                // Build project segment safely (support org-level calls when project is missing)
                var projectSegment = string.IsNullOrEmpty(_project) ? string.Empty : Uri.EscapeDataString(_project) + "/";

                // Use api-version=7.1 to match JS work item GETs
                var url = BuildApiUrl($"{projectSegment}_apis/wit/workitemtypes/{Uri.EscapeDataString(workItemTypeReferenceName)}/fields?api-version=7.1");
                _loggingService.LogInfo($"Requesting fields for work item type from: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                _loggingService.LogInfo($"ADO Request: {request.Method} {request.RequestUri}");
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                _loggingService.LogInfo($"ADO Response: {response.StatusCode} - preview: { (string.IsNullOrEmpty(content) ? "(empty)" : content.Substring(0, Math.Min(500, content.Length))) }");

                if (response.IsSuccessStatusCode)
                {
                    return ParseFieldDefinitions(content);
                }
                else
                {
                    _loggingService.LogError($"Failed to get fields for work item type {workItemTypeReferenceName}: {response.StatusCode} - {content}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Exception getting fields for work item type {workItemTypeReferenceName}", ex);
                return null;
            }
        }

        /// <summary>
        /// Get all field definitions in the organization (global fields)
        /// </summary>
        private async Task<List<AdoFieldInfo>> GetAllFieldsAsync()
        {
            try
            {
                // Use api-version 7.1 for global fields to match work item endpoints
                var url = BuildApiUrl("_apis/wit/fields?api-version=7.1");
                _loggingService.LogInfo($"Requesting global fields from: {url}");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                _loggingService.LogInfo($"ADO Request: {request.Method} {request.RequestUri}");
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                _loggingService.LogInfo($"ADO Response: {response.StatusCode} - preview: { (string.IsNullOrEmpty(content) ? "(empty)" : content.Substring(0, Math.Min(500, content.Length))) }");

                if (response.IsSuccessStatusCode)
                {
                    return ParseFieldDefinitions(content);
                }
                else
                {
                    _loggingService.LogError($"Failed to get global fields: {response.StatusCode} - {content}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Exception getting global fields", ex);
                return null;
            }
        }

        private string BuildApiUrl(string apiPath)
        {
            if (_serverUrl.Contains(".visualstudio.com"))
            {
                // Legacy VSTS format - serverUrl already contains org or org/project if user provided
                return $"{_serverUrl}/{apiPath}";
            }
            else
            {
                // Modern Azure DevOps format (dev.azure.com)
                // If organization is missing, allow apiPath to include organization/project segments
                if (string.IsNullOrEmpty(_organization))
                    return $"{_serverUrl}/{apiPath}";

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
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("Rally-ADO-Migration-Tool/1.0");
        }

        /// <summary>
        /// Simple diagnostic method to test ADO connection and log raw response
        /// </summary>
        public async Task<string> DiagnoseAdoConnectionAsync(ConnectionSettings settings)
        {
            try
            {
                ConfigureConnection(settings);
                
                var diagnostics = new StringBuilder();
                diagnostics.AppendLine("=== ADO CONNECTION DIAGNOSTICS ===");
                diagnostics.AppendLine($"Organization: {_organization}");
                diagnostics.AppendLine($"Project: {_project}");
                diagnostics.AppendLine($"Server URL: {_serverUrl}");
                diagnostics.AppendLine();
                
                // Test 1: Try to get work item types
                if (string.IsNullOrEmpty(_project))
                {
                    diagnostics.AppendLine("ERROR: ADO Project is not set - cannot build work item types URL");
                }
                else
                {
                    var url = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes?api-version=7.1");
                    diagnostics.AppendLine($"Testing URL: {url}");
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    AddAuthenticationHeader(request);
                    
                    diagnostics.AppendLine($"ADO Request: {request.Method} {request.RequestUri}");
                    var response = await _httpClient.SendAsync(request);
                    diagnostics.AppendLine($"Response Status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        diagnostics.AppendLine($"Response Length: {content.Length} characters");
                        diagnostics.AppendLine();
                        diagnostics.AppendLine("RAW RESPONSE (First 2000 characters):");
                        diagnostics.AppendLine(content.Substring(0, Math.Min(2000, content.Length)));
                        diagnostics.AppendLine();
                        
                        if (content.Length > 2000)
                        {
                            diagnostics.AppendLine($"... (truncated, total length: {content.Length})");
                            diagnostics.AppendLine();
                            diagnostics.AppendLine("RAW RESPONSE (Last 1000 characters):");
                            diagnostics.AppendLine(content.Substring(Math.Max(0, content.Length - 1000)));
                            diagnostics.AppendLine();
                        }
                        
                        // Try to parse and see what we get
                        var parsedTypes = ParseWorkItemTypes(content);
                        diagnostics.AppendLine($"PRIMARY PARSING: {parsedTypes?.Count ?? 0} work item types");
                        if (parsedTypes != null && parsedTypes.Any())
                        {
                            foreach (var wit in parsedTypes)
                            {
                                diagnostics.AppendLine($"  - {wit.Name} ({wit.ReferenceName})");
                            }
                        }
                        else
                        {
                            diagnostics.AppendLine("PRIMARY PARSING FAILED - Trying alternative method...");
                            var altParsedTypes = ParseWorkItemTypesAlternative(content);
                            diagnostics.AppendLine($"ALTERNATIVE PARSING: {altParsedTypes?.Count ?? 0} work item types");
                            if (altParsedTypes != null && altParsedTypes.Any())
                            {
                                foreach (var wit in altParsedTypes)
                                {
                                    diagnostics.AppendLine($"  - {wit.Name} ({wit.ReferenceName})");
                                }
                            }
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        diagnostics.AppendLine($"ERROR RESPONSE: {errorContent}");
                    }
                }
                
                return diagnostics.ToString();
            }
            catch (Exception ex)
            {
                return $"DIAGNOSTIC ERROR: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private void ConfigureConnection(ConnectionSettings settings)
        {
            if (settings == null) return;
            _apiKey = settings.AdoApiKey?.Trim();
            _organization = settings.AdoOrganization?.Trim();
            _project = settings.AdoProject?.Trim();
            _serverUrl = settings.AdoServerUrl?.TrimEnd('/') ?? "https://dev.azure.com";
        }

        private List<AdoWorkItemTypeInfo> ParseWorkItemTypes(string json)
        {
            var result = new List<AdoWorkItemTypeInfo>();
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return result;
                var j = JObject.Parse(json);
                var arr = j["value"] as JArray ?? (j["workItemTypes"] as JArray);
                if (arr != null)
                {
                    foreach (var item in arr)
                    {
                        var name = item["name"]?.ToString();
                        var refName = item["referenceName"]?.ToString() ?? item["referenceName"]?.ToString();
                        if (string.IsNullOrEmpty(name) && item["referenceName"] != null)
                            name = item["referenceName"]?.ToString();
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(refName)) continue;
                        result.Add(new AdoWorkItemTypeInfo
                        {
                            Name = name,
                            ReferenceName = refName,
                            Description = item["description"]?.ToString()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogDebug($"ParseWorkItemTypes fallback to alternative due to: {ex.Message}");
                return ParseWorkItemTypesAlternative(json);
            }
            return result;
        }

        private List<AdoWorkItemTypeInfo> ParseWorkItemTypesAlternative(string json)
        {
            var result = new List<AdoWorkItemTypeInfo>();
            try
            {
                if (string.IsNullOrEmpty(json)) return result;
                var nameMatches = Regex.Matches(json, "\"name\"\\s*:\\s*\"([^\\\"]+)\"");
                var refMatches = Regex.Matches(json, "\"referenceName\"\\s*:\\s*\"([^\\\"]+)\"");
                var count = Math.Min(nameMatches.Count, refMatches.Count);
                for (int i = 0; i < count; i++)
                {
                    var name = nameMatches[i].Groups[1].Value;
                    var r = refMatches[i].Groups[1].Value;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(r))
                        result.Add(new AdoWorkItemTypeInfo { Name = name, ReferenceName = r });
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("ParseWorkItemTypesAlternative failed", ex);
            }
            return result;
        }

        private List<AdoFieldInfo> ParseFieldDefinitions(string json)
        {
            var fields = new List<AdoFieldInfo>();
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return fields;
                var j = JObject.Parse(json);
                var arr = j["value"] as JArray ?? j["fields"] as JArray;
                if (arr != null)
                {
                    foreach (var f in arr)
                    {
                        var refName = f["referenceName"]?.ToString();
                        var name = f["name"]?.ToString();
                        var type = f["type"]?.ToString() ?? f["fieldType"]?.ToString();
                        var desc = f["description"]?.ToString() ?? f["helpText"]?.ToString();
                        var readOnly = f["readOnly"]?.ToObject<bool?>() ?? false;
                        string[] allowed = null;
                        try
                        {
                            var av = f["allowedValues"] as JArray;
                            if (av != null) allowed = av.Select(x => x.ToString()).ToArray();
                        }
                        catch { }
                        if (string.IsNullOrEmpty(refName)) continue;
                        fields.Add(new AdoFieldInfo
                        {
                            ReferenceName = refName,
                            Name = name ?? GetDisplayNameFromReferenceName(refName),
                            Type = type ?? GuessFieldType(refName),
                            Description = desc,
                            IsReadOnly = readOnly,
                            IsIdentity = refName.ToLower().Contains("assignedto"),
                            AllowedValues = allowed
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("ParseFieldDefinitions failed", ex);
            }
            return fields.GroupBy(f => f.ReferenceName).Select(g => g.First()).ToList();
        }

        private async Task GenerateFieldMappingJsonAsync(AdoFieldDiscoveryResult result)
        {
            try
            {
                // Merge global metadata into per-work-item fields
                var globalDict = (result.GlobalFields ?? new List<AdoFieldInfo>())
                    .Where(f => !string.IsNullOrEmpty(f.ReferenceName))
                    .ToDictionary(f => f.ReferenceName, StringComparer.OrdinalIgnoreCase);

                var workItemTypesOutput = new Dictionary<string, List<object>>();

                foreach (var kv in result.WorkItemTypeFields)
                {
                    var witName = kv.Key;
                    var fields = kv.Value ?? new List<AdoFieldInfo>();
                    var merged = new List<object>();

                    foreach (var f in fields)
                    {
                        AdoFieldInfo gf = null;
                        globalDict.TryGetValue(f.ReferenceName, out gf);

                        var type = gf?.Type ?? f.Type ?? GuessFieldType(f.ReferenceName);
                        var name = f.Name ?? gf?.Name ?? GetDisplayNameFromReferenceName(f.ReferenceName);
                        var desc = gf?.Description ?? f.Description;
                        var isReadOnly = gf?.IsReadOnly ?? f.IsReadOnly;
                        var isIdentity = gf?.IsIdentity ?? f.IsIdentity || (f.ReferenceName?.ToLowerInvariant().Contains("assignedto") ?? false);
                        var canSortBy = gf?.CanSortBy ?? f.CanSortBy;
                        var isQueryable = gf?.IsQueryable ?? f.IsQueryable;
                        var usage = gf?.Usage ?? f.Usage;
                        var allowedValues = gf?.AllowedValues ?? f.AllowedValues;

                        merged.Add(new
                        {
                            ReferenceName = f.ReferenceName,
                            Name = name,
                            Type = type,
                            Description = desc,
                            IsReadOnly = isReadOnly,
                            IsIdentity = isIdentity,
                            CanSortBy = canSortBy,
                            IsQueryable = isQueryable,
                            Usage = usage,
                            AllowedValues = allowedValues
                        });
                    }

                    workItemTypesOutput[witName] = merged;
                }

                // Build full output object
                var output = new
                {
                    GeneratedOn = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Organization = _organization,
                    Project = _project,
                    WorkItemTypes = workItemTypesOutput,
                    GlobalFields = (result.GlobalFields ?? new List<AdoFieldInfo>()).Select(gf => new
                    {
                        ReferenceName = gf.ReferenceName,
                        Name = gf.Name,
                        Type = gf.Type,
                        Description = gf.Description,
                        IsReadOnly = gf.IsReadOnly,
                        IsIdentity = gf.IsIdentity,
                        CanSortBy = gf.CanSortBy,
                        IsQueryable = gf.IsQueryable,
                        Usage = gf.Usage,
                        AllowedValues = gf.AllowedValues
                    }).ToList(),
                    Statistics = new { WorkItemTypes = result.WorkItemTypeFields.Count, GlobalFields = (result.GlobalFields?.Count ?? 0) }
                };

                var json = JsonConvert.SerializeObject(output, Formatting.Indented);
                var fileName = $"ADO_Field_Discovery_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                File.WriteAllText(fileName, json);
                _loggingService.LogInfo($"Enhanced ADO discovery saved to: {fileName}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("GenerateFieldMappingJsonAsync failed", ex);
            }
        }

        private string GetDisplayNameFromReferenceName(string referenceName)
        {
            if (string.IsNullOrEmpty(referenceName)) return string.Empty;
            var lastPart = referenceName.Contains(".") ? referenceName.Substring(referenceName.LastIndexOf('.') + 1) : referenceName;
            var result = Regex.Replace(lastPart, "([a-z])([A-Z])", "$1 $2");
            return result;
        }

        private string GuessFieldType(string referenceName)
        {
            if (string.IsNullOrEmpty(referenceName)) return "string";
            var lowerRef = referenceName.ToLowerInvariant();
            if (lowerRef.Contains("date") || lowerRef.Contains("time")) return "dateTime";
            if (lowerRef.Contains("count") || lowerRef.Contains("id") || lowerRef.Contains("level")) return "integer";
            if (lowerRef.Contains("assignedto") || lowerRef.Contains("createdby") || lowerRef.Contains("changedby")) return "identity";
            if (lowerRef.Contains("description") || lowerRef.Contains("steps") || lowerRef.Contains("notes")) return "html";
            if (lowerRef.Contains("estimate") || lowerRef.Contains("hours") || lowerRef.Contains("points")) return "double";
            if (lowerRef.Contains("blocked") || lowerRef.Contains("done")) return "boolean";
            return "string";
        }

    }

    /// <summary>
    /// Result of ADO field discovery
    /// </summary>
    public class AdoFieldDiscoveryResult
    {
        public Dictionary<string, List<AdoFieldInfo>> WorkItemTypeFields { get; set; }
        public List<AdoFieldInfo> GlobalFields { get; set; }

        public AdoFieldDiscoveryResult()
        {
            WorkItemTypeFields = new Dictionary<string, List<AdoFieldInfo>>();
            GlobalFields = new List<AdoFieldInfo>();
        }

        public List<AdoFieldInfo> GetAllUniqueFields()
        {
            var allFields = new List<AdoFieldInfo>();
            
            foreach (var fields in WorkItemTypeFields.Values)
            {
                allFields.AddRange(fields);
            }
            
            return allFields.GroupBy(f => f.ReferenceName).Select(g => g.First()).ToList();
        }
    }

    /// <summary>
    /// ADO work item type information
    /// </summary>
    public class AdoWorkItemTypeInfo
    {
        public string Name { get; set; }
        public string ReferenceName { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Icon { get; set; }
    }

    /// <summary>
    /// ADO field information
    /// </summary>
    public class AdoFieldInfo
    {
        public string ReferenceName { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public bool IsReadOnly { get; set; }
        public bool CanSortBy { get; set; }
        public bool IsQueryable { get; set; }
        public bool IsIdentity { get; set; }
        public string Usage { get; set; }
        public string[] AllowedValues { get; set; }
    }
}