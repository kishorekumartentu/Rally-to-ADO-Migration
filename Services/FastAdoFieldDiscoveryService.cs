using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// High-performance ADO field discovery using Work Item Tracking REST API
    /// Optimized for speed with concurrent requests, caching, and reduced timeouts
    /// </summary>
    public class FastAdoFieldDiscoveryService
    {
        private readonly HttpClient _httpClient;
        private readonly LoggingService _loggingService;
        private string _apiKey;
        private string _organization;
        private string _project;
        private string _serverUrl;

        // Performance optimization: Cache work item types and global fields
        private List<AdoWorkItemTypeInfo> _cachedWorkItemTypes;
        private List<AdoFieldInfo> _cachedGlobalFields;

        public FastAdoFieldDiscoveryService(LoggingService loggingService)
        {
            // Create HttpClientHandler to handle compression automatically
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            
            _httpClient = new HttpClient(handler);
            _loggingService = loggingService;
            
            // Performance optimization: Reduce timeout from 5 minutes to 30 seconds
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Fast field discovery with concurrent requests and progress reporting
        /// </summary>
        public async Task<AdoFieldDiscoveryResult> DiscoverAllAdoFieldsFastAsync(ConnectionSettings settings, 
            IProgress<string> progress = null, CancellationToken cancellationToken = default)
        {
            var result = new AdoFieldDiscoveryResult();
            var startTime = DateTime.Now;
            
            try
            {
                ConfigureConnection(settings);
                
                progress?.Report("Starting fast ADO field discovery...");
                _loggingService.LogInfo("Starting fast ADO field discovery using optimized REST API calls");

                // Step 1: Get work item types (fast)
                progress?.Report("Getting work item types...");
                var workItemTypes = await GetWorkItemTypesFastAsync(cancellationToken);
                if (workItemTypes == null || !workItemTypes.Any())
                {
                    progress?.Report("? Failed to retrieve work item types");
                    _loggingService.LogError("Failed to retrieve work item types");
                    return result;
                }

                progress?.Report($"? Found {workItemTypes.Count} work item types");
                _loggingService.LogInfo($"Found {workItemTypes.Count} work item types in {DateTime.Now - startTime:mm\\:ss}");

                // Step 2: Get field definitions for all work item types concurrently
                progress?.Report("Getting field definitions for all work item types...");
                var fieldsStartTime = DateTime.Now;
                
                var fieldTasks = workItemTypes.Select(async wit =>
                {
                    try
                    {
                        var fields = await GetFieldsForWorkItemTypeFastAsync(wit.ReferenceName, cancellationToken);
                        if (fields != null && fields.Any())
                        {
                            progress?.Report($"? {wit.Name}: {fields.Count} fields");
                            return new { WorkItemType = wit.Name, Fields = fields };
                        }
                        else
                        {
                            progress?.Report($"{wit.Name}: No fields found");
                            return new { WorkItemType = wit.Name, Fields = new List<AdoFieldInfo>() };
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"? {wit.Name}: Error - {ex.Message}");
                        _loggingService.LogWarning($"Failed to get fields for {wit.Name}: {ex.Message}");
                        return new { WorkItemType = wit.Name, Fields = new List<AdoFieldInfo>() };
                    }
                }).ToArray();

                var fieldResults = await Task.WhenAll(fieldTasks);
                
                // Populate results
                foreach (var fieldResult in fieldResults)
                {
                    if (fieldResult.Fields.Any())
                    {
                        result.WorkItemTypeFields[fieldResult.WorkItemType] = fieldResult.Fields;
                    }
                }

                _loggingService.LogInfo($"Retrieved fields for {result.WorkItemTypeFields.Count} work item types in {DateTime.Now - fieldsStartTime:mm\\:ss}");

                // Step 3: Skip global fields if we have enough data (performance optimization)
                var totalFieldsFound = result.WorkItemTypeFields.Sum(kvp => kvp.Value.Count);
                if (totalFieldsFound > 50)
                {
                    progress?.Report($"? Skipping global fields - found {totalFieldsFound} fields already");
                    _loggingService.LogInfo($"Skipping global fields query - already found {totalFieldsFound} fields");
                }
                else
                {
                    progress?.Report("Getting global field definitions...");
                    try
                    {
                        var globalFields = await GetAllFieldsFastAsync(cancellationToken);
                        if (globalFields != null && globalFields.Any())
                        {
                            result.GlobalFields = globalFields;
                            progress?.Report($"? Found {globalFields.Count} global fields");
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"Global fields query failed: {ex.Message}");
                        _loggingService.LogWarning($"Global fields query failed but continuing: {ex.Message}");
                    }
                }

                // Step 4: Generate files asynchronously (non-blocking)
                progress?.Report("Generating mapping files...");
                _ = Task.Run(() => GenerateFieldMappingJsonAsync(result), cancellationToken);

                var totalTime = DateTime.Now - startTime;
                progress?.Report($"Discovery completed in {totalTime:mm\\:ss}!");
                
                _loggingService.LogInfo($"Fast ADO field discovery completed successfully in {totalTime:mm\\:ss}!");
                _loggingService.LogInfo($"  - Work item types: {result.WorkItemTypeFields.Count}");
                _loggingService.LogInfo($"  - Total unique fields: {result.GetAllUniqueFields().Count}");

                return result;
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Discovery cancelled by user");
                _loggingService.LogWarning("ADO field discovery was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                progress?.Report($"? Discovery failed: {ex.Message}");
                _loggingService.LogError("Fast ADO field discovery failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Fast work item types retrieval with caching
        /// </summary>
        private async Task<List<AdoWorkItemTypeInfo>> GetWorkItemTypesFastAsync(CancellationToken cancellationToken = default)
        {
            // Return cached result if available
            if (_cachedWorkItemTypes != null)
            {
                _loggingService.LogDebug("Using cached work item types");
                return _cachedWorkItemTypes;
            }

            try
            {
                var url = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes?api-version=6.0");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                using (var response = await _httpClient.SendAsync(request, cancellationToken))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _loggingService.LogDebug($"Work item types API response: {content.Length} characters");
                        
                        var parsedTypes = ParseWorkItemTypesFast(content);
                        
                        // Cache the results
                        _cachedWorkItemTypes = parsedTypes;
                        
                        return parsedTypes;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _loggingService.LogError($"Failed to get work item types: {response.StatusCode} - {errorContent}");
                        return new List<AdoWorkItemTypeInfo>();
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Exception getting work item types: {ex.Message}");
                return new List<AdoWorkItemTypeInfo>();
            }
        }

        /// <summary>
        /// Fast field retrieval for a specific work item type with timeout
        /// </summary>
        private async Task<List<AdoFieldInfo>> GetFieldsForWorkItemTypeFastAsync(string workItemTypeReferenceName, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes/{Uri.EscapeDataString(workItemTypeReferenceName)}/fields?api-version=6.0");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                // Use shorter timeout for individual requests
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10-second timeout per work item type

                    using (var response = await _httpClient.SendAsync(request, cts.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            return ParseFieldDefinitionsFast(content);
                        }
                        else
                        {
                            _loggingService.LogWarning($"Failed to get fields for {workItemTypeReferenceName}: {response.StatusCode}");
                            return new List<AdoFieldInfo>();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Re-throw if it's the main cancellation
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogWarning($"Timeout getting fields for {workItemTypeReferenceName}");
                return new List<AdoFieldInfo>();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Exception getting fields for {workItemTypeReferenceName}: {ex.Message}");
                return new List<AdoFieldInfo>();
            }
        }

        /// <summary>
        /// Fast global fields retrieval with caching
        /// </summary>
        private async Task<List<AdoFieldInfo>> GetAllFieldsFastAsync(CancellationToken cancellationToken = default)
        {
            // Return cached result if available
            if (_cachedGlobalFields != null)
            {
                _loggingService.LogDebug("Using cached global fields");
                return _cachedGlobalFields;
            }

            try
            {
                var url = BuildApiUrl($"_apis/wit/fields?api-version=6.0");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(15)); // 15-second timeout for global fields

                    using (var response = await _httpClient.SendAsync(request, cts.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var fields = ParseFieldDefinitionsFast(content);
                            
                            // Cache the results
                            _cachedGlobalFields = fields;
                            
                            return fields;
                        }
                        else
                        {
                            _loggingService.LogWarning($"Failed to get global fields: {response.StatusCode}");
                            return new List<AdoFieldInfo>();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogWarning("Timeout getting global fields");
                return new List<AdoFieldInfo>();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Exception getting global fields: {ex.Message}");
                return new List<AdoFieldInfo>();
            }
        }

        /// <summary>
        /// Optimized JSON parsing for work item types
        /// </summary>
        private List<AdoWorkItemTypeInfo> ParseWorkItemTypesFast(string json)
        {
            var workItemTypes = new List<AdoWorkItemTypeInfo>();
            
            try
            {
                // Fast regex-based parsing
                var pattern = @"""name""\s*:\s*""([^""]+)""\s*[^}]*""referenceName""\s*:\s*""([^""]+)""";
                var matches = Regex.Matches(json, pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        workItemTypes.Add(new AdoWorkItemTypeInfo
                        {
                            Name = match.Groups[1].Value,
                            ReferenceName = match.Groups[2].Value
                        });
                    }
                }

                // Fallback method if primary parsing fails
                if (workItemTypes.Count == 0)
                {
                    var alternatePattern = @"""referenceName""\s*:\s*""([^""]+)""\s*[^}]*""name""\s*:\s*""([^""]+)""";
                    var alternateMatches = Regex.Matches(json, alternatePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    
                    foreach (Match match in alternateMatches)
                    {
                        if (match.Groups.Count >= 3)
                        {
                            workItemTypes.Add(new AdoWorkItemTypeInfo
                            {
                                Name = match.Groups[2].Value,
                                ReferenceName = match.Groups[1].Value
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error parsing work item types: {ex.Message}");
            }
            
            return workItemTypes.GroupBy(w => w.ReferenceName).Select(g => g.First()).ToList();
        }

        /// <summary>
        /// Optimized JSON parsing for field definitions
        /// </summary>
        private List<AdoFieldInfo> ParseFieldDefinitionsFast(string json)
        {
            var fields = new List<AdoFieldInfo>();
            
            try
            {
                // Fast regex pattern for field extraction
                var pattern = @"""referenceName""\s*:\s*""([^""]+)""\s*[^}]*""name""\s*:\s*""([^""]+)""\s*[^}]*""type""\s*:\s*""([^""]+)""";
                var matches = Regex.Matches(json, pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 4)
                    {
                        var field = new AdoFieldInfo
                        {
                            ReferenceName = match.Groups[1].Value,
                            Name = match.Groups[2].Value,
                            Type = match.Groups[3].Value,
                            IsIdentity = match.Groups[3].Value.ToLower() == "identity"
                        };
                        
                        fields.Add(field);
                    }
                }

                // Quick fallback if no fields found
                if (fields.Count == 0)
                {
                    var simplePattern = @"""referenceName""\s*:\s*""(System\.[^""]+|Microsoft\.[^""]+)""";
                    var simpleMatches = Regex.Matches(json, simplePattern, RegexOptions.Compiled);
                    
                    foreach (Match match in simpleMatches)
                    {
                        fields.Add(new AdoFieldInfo
                        {
                            ReferenceName = match.Groups[1].Value,
                            Name = match.Groups[1].Value.Split('.').Last(),
                            Type = "string"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error parsing field definitions: {ex.Message}");
            }
            
            return fields.GroupBy(f => f.ReferenceName).Select(g => g.First()).ToList();
        }

        /// <summary>
        /// Lightweight file generation (async, non-blocking)
        /// </summary>
        private async Task GenerateFieldMappingJsonAsync(AdoFieldDiscoveryResult result)
        {
            try
            {
                await Task.Delay(100); // Small delay to not block main thread
                
                var jsonBuilder = new StringBuilder();
                jsonBuilder.AppendLine("{");
                jsonBuilder.AppendLine($"  \"GeneratedOn\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",");
                jsonBuilder.AppendLine($"  \"Organization\": \"{_organization}\",");
                jsonBuilder.AppendLine($"  \"Project\": \"{_project}\",");
                jsonBuilder.AppendLine("  \"WorkItemTypes\": {");
                
                var workItemTypeEntries = result.WorkItemTypeFields.ToList();
                for (int i = 0; i < workItemTypeEntries.Count; i++)
                {
                    var kvp = workItemTypeEntries[i];
                    jsonBuilder.AppendLine($"    \"{kvp.Key}\": [");
                    
                    for (int j = 0; j < kvp.Value.Count; j++)
                    {
                        var field = kvp.Value[j];
                        jsonBuilder.AppendLine("      {");
                        jsonBuilder.AppendLine($"        \"ReferenceName\": \"{field.ReferenceName}\",");
                        jsonBuilder.AppendLine($"        \"Name\": \"{field.Name}\",");
                        jsonBuilder.AppendLine($"        \"Type\": \"{field.Type}\"");
                        jsonBuilder.AppendLine(j < kvp.Value.Count - 1 ? "      }," : "      }");
                    }
                    
                    jsonBuilder.AppendLine(i < workItemTypeEntries.Count - 1 ? "    ]," : "    ]");
                }
                
                jsonBuilder.AppendLine("  }");
                jsonBuilder.AppendLine("}");

                var fileName = $"ADO_Field_Discovery_Fast_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                await Task.Run(() => File.WriteAllText(fileName, jsonBuilder.ToString()));

                _loggingService.LogInfo($"Fast ADO field discovery saved to: {fileName}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to generate field mapping JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// Quick diagnostic method with timeout
        /// </summary>
        public async Task<string> QuickDiagnoseAdoConnectionAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            try
            {
                ConfigureConnection(settings);
                
                var diagnostics = new StringBuilder();
                diagnostics.AppendLine("=== QUICK ADO CONNECTION DIAGNOSTICS ===");
                diagnostics.AppendLine($"Organization: {_organization}");
                diagnostics.AppendLine($"Project: {_project}");
                diagnostics.AppendLine();
                
                var url = BuildApiUrl($"{Uri.EscapeDataString(_project)}/_apis/wit/workitemtypes?api-version=6.0");
                diagnostics.AppendLine($"Testing URL: {url}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);
                
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(10)); // Quick 10-second timeout
                    
                    using (var response = await _httpClient.SendAsync(request, cts.Token))
                    {
                        diagnostics.AppendLine($"Response Status: {response.StatusCode}");
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            diagnostics.AppendLine($"Response Length: {content.Length} characters");
                            
                            var parsedTypes = ParseWorkItemTypesFast(content);
                            diagnostics.AppendLine($"Parsed Work Item Types: {parsedTypes?.Count ?? 0}");
                            
                            if (parsedTypes != null && parsedTypes.Any())
                            {
                                diagnostics.AppendLine("Work Item Types Found:");
                                foreach (var wit in parsedTypes.Take(5))
                                {
                                    diagnostics.AppendLine($"  ? {wit.Name} ({wit.ReferenceName})");
                                }
                                if (parsedTypes.Count > 5)
                                {
                                    diagnostics.AppendLine($"  ... and {parsedTypes.Count - 5} more");
                                }
                            }
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            diagnostics.AppendLine($"ERROR: {errorContent}");
                        }
                    }
                }
                
                return diagnostics.ToString();
            }
            catch (OperationCanceledException)
            {
                return "? TIMEOUT: ADO connection took too long (>10 seconds)";
            }
            catch (Exception ex)
            {
                return $"? ERROR: {ex.Message}";
            }
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
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("Rally-ADO-Migration-Tool-Fast/1.0");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}