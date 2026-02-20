using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Rally field discovery service using Rally REST API v2.0
    /// Based on https://github.com/emisgroup/jira-rally-export
    /// Scoped to project for proper authorization
    /// </summary>
    public class RallyFieldDiscoveryService
    {
        private readonly HttpClient _httpClient;
        private readonly LoggingService _loggingService;
        private string _apiKey;
        private string _serverUrl;
        private string _workspace;
        private string _project;
        private string _workspaceRef;
        private string _projectRef;

        public RallyFieldDiscoveryService(LoggingService loggingService)
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            
            _httpClient = new HttpClient(handler);
            _loggingService = loggingService;
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Discover all Rally fields and write to JSON
        /// </summary>
        public async Task<RallyFieldDiscoveryResult> DiscoverAllRallyFieldsAsync(
            ConnectionSettings settings, 
            IProgress<string> progress = null, 
            CancellationToken cancellationToken = default)
        {
            var result = new RallyFieldDiscoveryResult();
            var startTime = DateTime.Now;
            
            try
            {
                ConfigureConnection(settings);
                
                progress?.Report("Starting Rally field discovery...");
                _loggingService.LogInfo("Starting Rally field discovery using REST API v2.0 (Project-scoped)");

                // Step 1: Resolve workspace and project references
                progress?.Report("Resolving workspace and project...");
                await ResolveWorkspaceAndProjectAsync(cancellationToken);
                
                if (string.IsNullOrEmpty(_workspaceRef))
                {
                    throw new Exception($"Failed to resolve workspace: {_workspace}");
                }

                // If project is specified, use project scope; otherwise use workspace scope
                var scopeRef = !string.IsNullOrEmpty(_projectRef) ? _projectRef : _workspaceRef;
                var scopeType = !string.IsNullOrEmpty(_projectRef) ? "project" : "workspace";
                
                progress?.Report($"? Using {scopeType} scope for queries");
                _loggingService.LogInfo($"Using {scopeType} scope: {scopeRef}");

                // Step 2: Get TypeDefinitions (work item types that are creatable)
                progress?.Report("Getting Rally TypeDefinitions...");
                var typeDefinitions = await GetTypeDefinitionsAsync(scopeRef, scopeType, cancellationToken);
                
                // Filter to only the 6 mapped types
                var mappedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Task",
                    "Defect", 
                    "HierarchicalRequirement",
                    "Hierarchical Requirement",  // Rally may use spaces
                    "PortfolioItem/Epic",
                    "Epic",  // Rally may use shortened name
                    "PortfolioItem/Feature",
                    "Feature",  // Rally may use shortened name
                    "TestCase",
                    "Test Case"  // Rally may use spaces
                };
                
                typeDefinitions = typeDefinitions
                    .Where(t => 
                        mappedTypes.Contains(t.TypePath) || 
                        mappedTypes.Contains(t.Name) ||
                        (t.TypePath != null && t.TypePath.IndexOf("Epic", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (t.TypePath != null && t.TypePath.IndexOf("Feature", StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();
                
                if (typeDefinitions == null || !typeDefinitions.Any())
                {
                    throw new Exception("No Rally TypeDefinitions found for mapped types (Task, Defect, HierarchicalRequirement, Epic, Feature, TestCase)");
                }

                progress?.Report($"? Found {typeDefinitions.Count} mapped TypeDefinitions");
                _loggingService.LogInfo($"Found {typeDefinitions.Count} Rally TypeDefinitions (filtered to mapped types only)");

                // Step 3: Get Attributes (fields) for each TypeDefinition
                progress?.Report("Getting Attributes for each TypeDefinition...");
                
                foreach (var typeDef in typeDefinitions)
                {
                    try
                    {
                        progress?.Report($"  Processing {typeDef.Name}...");
                        
                        var attributes = await GetAttributesForTypeAsync(typeDef.AttributesRef, cancellationToken);
                        
                        if (attributes != null && attributes.Any())
                        {
                            result.WorkItemTypeFields[typeDef.Name] = attributes;
                            progress?.Report($"  ? {typeDef.Name}: {attributes.Count} attributes");
                            _loggingService.LogInfo($"Retrieved {attributes.Count} attributes for {typeDef.Name}");
                        }
                        else
                        {
                            progress?.Report($"  ?? {typeDef.Name}: No attributes found");
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"  ? {typeDef.Name}: {ex.Message}");
                        _loggingService.LogWarning($"Failed to get attributes for {typeDef.Name}: {ex.Message}");
                    }
                    
                    // Small delay to avoid rate limiting
                    await Task.Delay(200, cancellationToken);
                }

                // Step 4: Generate JSON output file
                progress?.Report("Writing JSON output...");
                var fileName = await GenerateFieldMappingJsonAsync(result);

                var totalTime = DateTime.Now - startTime;
                progress?.Report($"? Discovery completed in {totalTime:mm\\:ss}! File: {fileName}");
                
                _loggingService.LogInfo($"? Rally field discovery completed successfully in {totalTime:mm\\:ss}!");
                _loggingService.LogInfo($"  - TypeDefinitions: {result.WorkItemTypeFields.Count}");
                _loggingService.LogInfo($"  - Total unique fields: {result.GetAllUniqueFields().Count}");
                _loggingService.LogInfo($"  - Output file: {fileName}");

                return result;
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Discovery cancelled by user");
                _loggingService.LogWarning("Rally field discovery was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                progress?.Report($"? Discovery failed: {ex.Message}");
                _loggingService.LogError("Rally field discovery failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Resolve workspace and project to their references
        /// Based on working RallyApiService implementation
        /// </summary>
        private async Task ResolveWorkspaceAndProjectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // IMPORTANT: The working RallyApiService uses workspace ID directly, not workspace name lookup
                // Format: workspace=/workspace/{workspace_id}
                // The _workspace field should contain the workspace ID/number, not the name
                
                _loggingService.LogInfo($"Using workspace: {_workspace}");
                
                // Build workspace reference in Rally API format
                // Based on RallyApiService which uses: workspace=/workspace/{_workspace}
                _workspaceRef = $"/workspace/{_workspace}";
                
                _loggingService.LogInfo($"? Using workspace reference: {_workspaceRef}");
                
                // If project is specified, build project reference
                if (!string.IsNullOrEmpty(_project))
                {
                    // Based on RallyApiService format: project=/project/{_project}
                    _projectRef = $"/project/{_project}";
                    _loggingService.LogInfo($"? Using project reference: {_projectRef}");
                }
                else
                {
                    _loggingService.LogInfo("No project specified, using workspace scope");
                }

                // Verify access by making a test query (like RallyApiService.TestConnectionAsync does)
                var testUrl = $"{_serverUrl}/slm/webservice/v2.0/hierarchicalrequirement?workspace={_workspaceRef}&pagesize=1&fetch=FormattedID,Name";
                
                _loggingService.LogDebug($"Testing workspace access: {testUrl}");
                
                var testRequest = new HttpRequestMessage(HttpMethod.Get, testUrl);
                AddAuthenticationHeader(testRequest);

                using (var response = await _httpClient.SendAsync(testRequest, cancellationToken))
                {
                    var content = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _loggingService.LogError($"Workspace access test failed: {response.StatusCode}");
                        _loggingService.LogError($"Response: {content}");
                        
                        // Provide helpful error messages
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            throw new Exception($"Unauthorized: Rally API Key is invalid or expired.\n\nPlease verify:\n1. Your API key is correct (copy from Rally Settings > API Keys)\n2. The API key hasn't expired\n3. You're using the full API key without spaces\n\nNote: Workspace should be the workspace ID (number), not the name.");
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            throw new Exception($"Forbidden: API Key does not have access to workspace '{_workspace}'.\n\nPlease ensure:\n1. The API key has 'Viewer' or higher role\n2. You have access to this workspace\n3. The workspace ID is correct (use workspace number, not name)");
                        }
                        else
                        {
                            throw new Exception($"Failed to access workspace: {response.StatusCode}\n\nResponse: {content}\n\nNote: Workspace field should contain the workspace ID (number), not the workspace name.");
                        }
                    }

                    _loggingService.LogDebug($"Workspace access test response: {content.Substring(0, Math.Min(200, content.Length))}...");
                    
                    // Verify Rally API response structure (from RallyApiService.TestConnectionAsync)
                    if (content.Contains("\"QueryResult\"") && content.Contains("\"Results\"") )
                    {
                        _loggingService.LogInfo("? Rally API response structure verified");
                        _loggingService.LogInfo("? Workspace access confirmed");
                    }
                    else
                    {
                        _loggingService.LogWarning("Rally API response format unexpected - continuing anyway");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to resolve workspace/project: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Alternative method to resolve workspace from subscription
        /// </summary>
        private async Task ResolveWorkspaceFromSubscriptionAsync(CancellationToken cancellationToken = default)
        {
            // This method is no longer needed with the new approach
            // Keeping it for backwards compatibility but it won't be called
        }

        /// <summary>
        /// Get all TypeDefinitions (work item types) for the workspace or project
        /// Based on jira-rally-export TypeDefinition query
        /// </summary>
        private async Task<List<RallyTypeDefinition>> GetTypeDefinitionsAsync(
            string scopeRef, 
            string scopeType, 
            CancellationToken cancellationToken = default)
        {
            var typeDefinitions = new List<RallyTypeDefinition>();
            
            try
            {
                // Query TypeDefinitions - get creatable types with their attributes reference
                // Use project scope if available for better authorization
                var url = $"{_serverUrl}/slm/webservice/v2.0/typedefinition?" +
                         $"{scopeType}={scopeRef}&" +
                         $"query=(Creatable = true)&" +
                         $"fetch=Name,TypePath,ElementName,Attributes,DisplayName&" +
                         $"pagesize=200";
                
                _loggingService.LogDebug($"Querying TypeDefinitions with {scopeType} scope: {url}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                using (var response = await _httpClient.SendAsync(request, cancellationToken))
                {
                    var content = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _loggingService.LogError($"TypeDefinition query failed: {response.StatusCode} - {content}");
                        
                        // If using project scope failed with 401/403, try workspace scope
                        if (scopeType == "project" && (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                                                       response.StatusCode == System.Net.HttpStatusCode.Forbidden))
                        {
                            _loggingService.LogWarning("Project scope unauthorized, retrying with workspace scope");
                            return await GetTypeDefinitionsAsync(_workspaceRef, "workspace", cancellationToken);
                        }
                        
                        return typeDefinitions;
                    }

                    _loggingService.LogDebug($"TypeDefinition response length: {content.Length} characters");

                    var json = JObject.Parse(content);
                    var results = json["QueryResult"]?["Results"] as JArray;
                    
                    if (results != null)
                    {
                        foreach (var item in results)
                        {
                            var typeDef = new RallyTypeDefinition
                            {
                                Name = item["Name"]?.ToString(),
                                TypePath = item["TypePath"]?.ToString(),
                                ElementName = item["ElementName"]?.ToString(),
                                DisplayName = item["DisplayName"]?.ToString(),
                                AttributesRef = item["Attributes"]?["_ref"]?.ToString()
                            };
                            
                            if (!string.IsNullOrEmpty(typeDef.Name) && !string.IsNullOrEmpty(typeDef.AttributesRef))
                            {
                                typeDefinitions.Add(typeDef);
                                _loggingService.LogDebug($"Found TypeDefinition: {typeDef.Name} ({typeDef.TypePath})");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get TypeDefinitions: {ex.Message}");
                throw;
            }
            
            return typeDefinitions;
        }

        /// <summary>
        /// Get Attributes (fields) for a specific TypeDefinition
        /// Based on jira-rally-export attribute fetching
        /// Robust parsing added to support different Rally JSON shapes and AllowedValues formats
        /// </summary>
        private async Task<List<RallyFieldInfo>> GetAttributesForTypeAsync(string attributesRef, CancellationToken cancellationToken = default)
        {
            var attributes = new List<RallyFieldInfo>();
            
            try
            {
                if (string.IsNullOrEmpty(attributesRef))
                    return attributes;

                // Normalize attributes URL: attributesRef may be relative or full URL
                string baseUrl = NormalizeAttributeRef(attributesRef);

                // Append fetch/page params safely
                var separator = baseUrl.Contains("?") ? "&" : "?";
                var url = $"{baseUrl}{separator}pagesize=200&fetch=ElementName,Name,AttributeType,Required,ReadOnly,Custom,Hidden,AllowedValues";

                _loggingService.LogDebug($"Fetching attributes: {url}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);

                using (var response = await _httpClient.SendAsync(request, cancellationToken))
                {
                    var content = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _loggingService.LogError($"Attributes query failed: {response.StatusCode} - {content}");
                        return attributes;
                    }

                    var json = JObject.Parse(content);

                    // Try common Rally shapes: QueryResult.Results, Results, or find array that contains ElementName
                    var results = json["QueryResult"]?["Results"] as JArray ?? json["Results"] as JArray;

                    if (results == null)
                    {
                        // Try to locate any JArray that looks like attribute definitions
                        var candidate = json.Descendants().OfType<JArray>()
                            .FirstOrDefault(a => a.Count > 0 && a.First.Type == JTokenType.Object && a.First["ElementName"] != null);

                        results = candidate;
                    }

                    if (results != null)
                    {
                        foreach (var item in results)
                        {
                            var attribute = new RallyFieldInfo
                            {
                                ElementName = item["ElementName"]?.ToString(),
                                Name = item["Name"]?.ToString(),
                                AttributeType = item["AttributeType"]?.ToString(),
                                Required = item["Required"]?.ToObject<bool>() ?? false,
                                ReadOnly = item["ReadOnly"]?.ToObject<bool>() ?? false,
                                Custom = item["Custom"]?.ToObject<bool>() ?? false,
                                Hidden = item["Hidden"]?.ToObject<bool>() ?? false
                            };
                            
                            // Parse AllowedValues which can be an array or a QueryResult wrapper
                            var allowedValuesToken = item["AllowedValues"];
                            if (allowedValuesToken != null)
                            {
                                JArray allowedArray = null;

                                if (allowedValuesToken.Type == JTokenType.Array)
                                {
                                    allowedArray = (JArray)allowedValuesToken;
                                }
                                else
                                {
                                    // Could be { QueryResult: { Results: [...] } } or { Results: [...] }
                                    allowedArray = allowedValuesToken["QueryResult"]?["Results"] as JArray ?? allowedValuesToken["Results"] as JArray;

                                    // If still null, try to find a nested array that contains StringValue or IntegerValue
                                    if (allowedArray == null)
                                    {
                                        allowedArray = FindFirstArray(allowedValuesToken, a => a.Count > 0 && (a.First["StringValue"] != null || a.First["IntegerValue"] != null || a.First.Type == JTokenType.String));
                                    }
                                }

                                if (allowedArray != null && allowedArray.Count > 0)
                                {
                                    attribute.AllowedValues = allowedArray
                                        .Select(v => v["StringValue"]?.ToString() ?? v["LocalizedStringValue"]?.ToString() ?? v["IntegerValue"]?.ToString() ?? v.ToString())
                                        .Where(v => !string.IsNullOrEmpty(v))
                                        .ToArray();
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(attribute.ElementName))
                            {
                                attributes.Add(attribute);
                            }
                        }
                        
                        _loggingService.LogDebug($"Parsed {attributes.Count} attributes");
                    }
                    else
                    {
                        _loggingService.LogWarning("No attribute results array found in Rally response");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get attributes: {ex.Message}");
                throw;
            }
            
            return attributes;
        }

        // Recursive helper to find first JArray matching a predicate within a token
        private JArray FindFirstArray(JToken token, Func<JArray, bool> predicate = null)
        {
            if (token == null) return null;

            if (token.Type == JTokenType.Array)
            {
                var arr = (JArray)token;
                if (predicate == null || predicate(arr))
                    return arr;
            }

            if (token.Type == JTokenType.Object)
            {
                foreach (var prop in ((JObject)token).Properties())
                {
                    var found = FindFirstArray(prop.Value, predicate);
                    if (found != null) return found;
                }
            }

            if (token.HasValues)
            {
                foreach (var child in token.Children())
                {
                    var found = FindFirstArray(child, predicate);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Normalize an attribute reference returned by Rally to a full URL we can call.
        /// Handles full URLs, root-relative refs and relative refs.
        /// </summary>
        private string NormalizeAttributeRef(string attributesRef)
        {
            if (string.IsNullOrEmpty(attributesRef))
                return attributesRef;

            // If it's already a full URL, return as-is
            if (attributesRef.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return attributesRef;

            var server = _serverUrl?.TrimEnd('/') ?? "https://rally1.rallydev.com";

            // If it's root-relative (starts with /), append server directly
            if (attributesRef.StartsWith("/"))
                return server + attributesRef;

            // Otherwise assume it's a path under slm/webservice/v2.0
            return server + "/slm/webservice/v2.0/" + attributesRef.TrimStart('/');
        }

        /// <summary>
        /// Generate Rally field mapping JSON file with proper structure
        /// Output filename: RallyFieldDiscovery.json (no timestamp)
        /// </summary>
        private async Task<string> GenerateFieldMappingJsonAsync(RallyFieldDiscoveryResult result)
        {
            try
            {
                var fileName = "RallyFieldDiscovery.json";
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                
                // Create structured JSON output
                var output = new
                {
                    GeneratedOn = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    RallyServerUrl = _serverUrl,
                    RallyWorkspace = _workspace,
                    RallyProject = _project ?? "N/A",
                    WorkspaceRef = _workspaceRef,
                    ProjectRef = _projectRef ?? "N/A",
                    Scope = !string.IsNullOrEmpty(_projectRef) ? "Project" : "Workspace",
                    TotalTypeDefinitions = result.WorkItemTypeFields.Count,
                    TotalUniqueFields = result.GetAllUniqueFields().Count,
                    TypeDefinitions = result.WorkItemTypeFields.Select(kvp => new
                    {
                        TypeName = kvp.Key,
                        AttributeCount = kvp.Value.Count,
                        Attributes = kvp.Value.Select(attr => new
                        {
                            attr.ElementName,
                            attr.Name,
                            attr.AttributeType,
                            attr.Required,
                            attr.ReadOnly,
                            attr.Custom,
                            attr.Hidden,
                            AllowedValues = attr.AllowedValues ?? new string[0]
                        }).OrderBy(a => a.ElementName).ToList()
                    }).OrderBy(t => t.TypeName).ToList()
                };
                
                // Write JSON file with formatting
                var json = JsonConvert.SerializeObject(output, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(filePath, json));
                
                _loggingService.LogInfo($"? Rally field discovery JSON saved to: {filePath}");
                _loggingService.LogInfo($"   File size: {new FileInfo(filePath).Length / 1024} KB");
                
                return fileName;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to generate Rally field mapping JSON: {ex.Message}");
                throw;
            }
        }

        private void ConfigureConnection(ConnectionSettings settings)
        {
            _apiKey = settings.RallyApiKey?.Trim();
            _serverUrl = settings.RallyServerUrl?.TrimEnd('/') ?? "https://rally1.rallydev.com";
            _workspace = settings.RallyWorkspace?.Trim();
            _project = settings.RallyProject?.Trim();
            
            if (string.IsNullOrEmpty(_apiKey))
                throw new ArgumentException("Rally API Key is required");
            if (string.IsNullOrEmpty(_workspace))
                throw new ArgumentException("Rally Workspace ID is required.\n\nNote: Use the workspace ID (number), not the workspace name.\nYou can find it in Rally's URL or Setup ? Workspaces.");
            
            _loggingService.LogDebug($"Rally connection configured:");
            _loggingService.LogDebug($"- Server URL: {_serverUrl}");
            _loggingService.LogDebug($"- Workspace ID: {_workspace}");
            _loggingService.LogDebug($"- Project ID: {_project ?? "Not specified"}");
            _loggingService.LogDebug($"- API Key: {(_apiKey?.Length > 0 ? $"***{_apiKey.Substring(Math.Max(0, _apiKey.Length - 4))}" : "Not provided")} ");
        }

        private void AddAuthenticationHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_apiKey))
            {
                // Rally REST API v2.0 uses Basic Authentication with API Key
                // IMPORTANT: Based on working RallyApiService, use format: "apikey:" not "_:apikey"
                // Reference: RallyApiService.cs AddAuthenticationHeader method
                
                var cleanApiKey = _apiKey.Trim();
                var authString = $"{cleanApiKey}:";  // API key as username, empty password
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
            
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("Rally-ADO-Migration-Tool/1.0");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Rally TypeDefinition information
    /// </summary>
    public class RallyTypeDefinition
    {
        public string Name { get; set; }
        public string TypePath { get; set; }
        public string ElementName { get; set; }
        public string DisplayName { get; set; }
        public string AttributesRef { get; set; }
    }

    /// <summary>
    /// Rally field discovery result
    /// </summary>
    public class RallyFieldDiscoveryResult
    {
        public Dictionary<string, List<RallyFieldInfo>> WorkItemTypeFields { get; set; }

        public RallyFieldDiscoveryResult()
        {
            WorkItemTypeFields = new Dictionary<string, List<RallyFieldInfo>>();
        }

        public List<RallyFieldInfo> GetAllUniqueFields()
        {
            var allFields = new List<RallyFieldInfo>();
            
            foreach (var fields in WorkItemTypeFields.Values)
            {
                allFields.AddRange(fields);
            }
            
            return allFields.GroupBy(f => f.ElementName).Select(g => g.First()).ToList();
        }
    }

    /// <summary>
    /// Rally field/attribute information
    /// </summary>
    public class RallyFieldInfo
    {
        public string ElementName { get; set; }
        public string Name { get; set; }
        public string AttributeType { get; set; }
        public bool Required { get; set; }
        public bool ReadOnly { get; set; }
        public bool Custom { get; set; }
        public bool Hidden { get; set; }
        public string[] AllowedValues { get; set; }
    }
}
