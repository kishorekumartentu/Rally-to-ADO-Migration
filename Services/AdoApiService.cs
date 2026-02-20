using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public class AdoApiService : IDisposable
    {
        private readonly LoggingService _loggingService;
        private readonly string _organizationUrl;
        private readonly string _projectName;
        private readonly string _personalAccessToken;

        public AdoApiService(LoggingService loggingService, string organizationUrl, string projectName, string personalAccessToken)
        {
            _loggingService = loggingService;
            _organizationUrl = organizationUrl;
            _projectName = projectName;
            _personalAccessToken = personalAccessToken;
        }

        private HttpClient CreateHttpClient(string personalAccessToken = null)
        {
            var client = new HttpClient();
            var pat = personalAccessToken ?? _personalAccessToken;
            if (!string.IsNullOrEmpty(pat))
            {
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            }
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Rally-ADO-Migration-Tool/1.0");
            return client;
        }

        private string BuildApiUrl(string apiPath, string organizationUrl = null)
        {
            var baseUrl = (organizationUrl ?? _organizationUrl)?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = "https://dev.azure.com"; // fallback
            return $"{baseUrl}/{apiPath}";
        }

    /// <summary>
    /// Update Task state using intermediate transitions if direct transition is not allowed.
    /// ADO process templates often require: New -> Active -> Closed (cannot go New -> Closed directly).
    /// This method automatically detects and handles this workflow restriction.
    /// </summary>
    public async Task<bool> UpdateTaskStateWithTransitionsAsync(ConnectionSettings settings, int workItemId, string targetState, bool bypassRules = false)
    {
        try
        {
            if (string.IsNullOrEmpty(targetState))
            {
                _logging_service_safe().LogWarning($"UpdateTaskStateWithTransitionsAsync: targetState is null or empty");
                return false;
            }

            _logging_service_safe().LogInfo($"[STATE_TRANSITION] Updating Task {workItemId} to state '{targetState}'");

            // Get current state
            var currentWorkItem = await GetWorkItemByIdAsync(settings, workItemId);
            if (currentWorkItem == null)
            {
                _logging_service_safe().LogWarning($"Cannot get current work item {workItemId}");
                return false;
            }

            var currentState = currentWorkItem["fields"]?["System.State"]?.ToString();
            _logging_service_safe().LogInfo($"[STATE_TRANSITION] Current state: '{currentState}' -> Target state: '{targetState}'");

            // If already at target state, return success
            if (string.Equals(currentState, targetState, StringComparison.OrdinalIgnoreCase))
            {
                _logging_service_safe().LogInfo($"[STATE_TRANSITION] Already at target state '{targetState}'");
                return true;
            }

            // Define state transition paths for Tasks
            // Common ADO workflow: New -> Active -> Resolved -> Closed
            var stateTransitions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Closed"] = new List<string> { "Active", "Closed" },  // New -> Active -> Closed
                ["Resolved"] = new List<string> { "Active", "Resolved" },  // New -> Active -> Resolved
                ["Removed"] = new List<string> { "Removed" }  // Direct transition allowed
            };

            // For target states not requiring intermediate steps, try direct transition
            if (!stateTransitions.ContainsKey(targetState))
            {
                _logging_service_safe().LogInfo($"[STATE_TRANSITION] Attempting direct transition to '{targetState}'");
                var directUpdate = new Dictionary<string, object> { ["System.State"] = targetState };
                var directSuccess = await PatchWorkItemFieldsAsync(settings, workItemId, directUpdate, bypassRules);

                if (directSuccess)
                {
                    // Verify state was actually set
                    var verifyWorkItem = await GetWorkItemByIdAsync(settings, workItemId);
                    var actualState = verifyWorkItem?["fields"]?["System.State"]?.ToString();
                    
                    if (string.Equals(actualState, targetState, StringComparison.OrdinalIgnoreCase))
                    {
                        _logging_service_safe().LogInfo($"[STATE_TRANSITION] ? Direct transition successful: '{currentState}' -> '{actualState}'");
                        return true;
                    }
                    else
                    {
                        _logging_service_safe().LogWarning($"[STATE_TRANSITION] Direct transition appeared to succeed but state is '{actualState}', not '{targetState}'");
                        return false;
                    }
                }

                return directSuccess;
            }

            // Use intermediate state transitions
            var transitionPath = stateTransitions[targetState];
            _logging_service_safe().LogInfo($"[STATE_TRANSITION] Using intermediate transitions: {currentState} -> {string.Join(" -> ", transitionPath)}");

            string lastState = currentState;
            foreach (var intermediateState in transitionPath)
            {
                // Skip if already at this state
                if (string.Equals(lastState, intermediateState, StringComparison.OrdinalIgnoreCase))
                {
                    _logging_service_safe().LogDebug($"[STATE_TRANSITION] Already at '{intermediateState}', skipping");
                    continue;
                }

                _logging_service_safe().LogInfo($"[STATE_TRANSITION] Step: '{lastState}' -> '{intermediateState}'");

                // Only set System.State - System.Reason is read-only and automatically set by ADO
                var stateUpdate = new Dictionary<string, object> { ["System.State"] = intermediateState };

                var stepSuccess = await PatchWorkItemFieldsAsync(settings, workItemId, stateUpdate, bypassRules);

                if (!stepSuccess)
                {
                    _logging_service_safe().LogWarning($"[STATE_TRANSITION] Failed at step: '{lastState}' -> '{intermediateState}'");
                    return false;
                }

                // Verify state was actually set
                var verifyWorkItem = await GetWorkItemByIdAsync(settings, workItemId);
                var actualState = verifyWorkItem?["fields"]?["System.State"]?.ToString();

                if (!string.Equals(actualState, intermediateState, StringComparison.OrdinalIgnoreCase))
                {
                    _logging_service_safe().LogWarning($"[STATE_TRANSITION] State verification failed! Expected '{intermediateState}' but got '{actualState}'");
                    _logging_service_safe().LogWarning($"[STATE_TRANSITION] This may be due to ADO workflow rules or process template restrictions");
                    return false;
                }

                _logging_service_safe().LogInfo($"[STATE_TRANSITION] ? Step successful: '{lastState}' -> '{actualState}'");
                lastState = actualState;
            }

            // Final verification
            var finalWorkItem = await GetWorkItemByIdAsync(settings, workItemId);
            var finalState = finalWorkItem?["fields"]?["System.State"]?.ToString();

            if (string.Equals(finalState, targetState, StringComparison.OrdinalIgnoreCase))
            {
                _logging_service_safe().LogInfo($"[STATE_TRANSITION] ? All transitions successful! Final state: '{finalState}'");
                return true;
            }
            else
            {
                _logging_service_safe().LogWarning($"[STATE_TRANSITION] ? Final state is '{finalState}', expected '{targetState}'");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logging_service_safe().LogError($"UpdateTaskStateWithTransitionsAsync failed: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Patch fields on an existing ADO work item. Optionally bypass rules for historical preservation.
    /// Uses 'add' for most fields and 'replace' for Test Case Steps which must replace existing content.
    /// </summary>
    public async Task<bool> PatchWorkItemFieldsAsync(ConnectionSettings settings, int workItemId, Dictionary<string, object> fields, bool bypassRules = false)
    {
        try
        {
            if (fields == null || fields.Count == 0) return true; // nothing to patch

            var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
            var organization = settings?.AdoOrganization?.Trim();
            var project = settings?.AdoProject?.Trim() ?? _projectName;
            var pat = settings?.AdoApiKey ?? _personalAccessToken;

            string baseApiPath;
            if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                baseApiPath = $"{serverUrl}/{project}";
            else if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                baseApiPath = $"{serverUrl}/{organization}/{project}";
            else baseApiPath = $"{serverUrl}/{project}";

            var bypassParam = bypassRules ? "&bypassRules=true" : string.Empty;
            var requestUrl = $"{baseApiPath}/_apis/wit/workitems/{workItemId}?api-version=7.1{bypassParam}";

            var patchOps = new List<Dictionary<string, object>>();
            foreach (var kv in fields)
            {
                if (kv.Value == null) continue;
                
                // CRITICAL: Test Case Steps field must use 'replace' operation, not 'add'
                // ADO requires replace to update existing steps or add new ones
                var operation = kv.Key.Equals("Microsoft.VSTS.TCM.Steps", StringComparison.OrdinalIgnoreCase) 
                    ? "replace" 
                    : "add";
                
                patchOps.Add(new Dictionary<string, object>
                {
                    ["op"] = operation,
                    ["path"] = $"/fields/{kv.Key}",
                    ["value"] = kv.Value
                });
                
                if (operation == "replace")
                {
                    _logging_service_safe().LogDebug($"Using 'replace' operation for {kv.Key} field");
                }
            }

            if (patchOps.Count == 0) return true;

            using (var client = CreateHttpClient(pat))
            {
                var jsonSettings = new Newtonsoft.Json.JsonSerializerSettings { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.Default };
                var patchJson = Newtonsoft.Json.JsonConvert.SerializeObject(patchOps, jsonSettings);
                
                _logging_service_safe().LogInfo($"Patching work item {workItemId} with {patchOps.Count} operations (bypass={bypassRules})");
                _logging_service_safe().LogDebug($"PATCH JSON: {patchJson.Substring(0, Math.Min(500, patchJson.Length))}");
                
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl)
                {
                    Content = new StringContent(patchJson, Encoding.UTF8, "application/json-patch+json")
                };

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _logging_service_safe().LogInfo($"Patch succeeded for work item {workItemId}");
                    return true;
                }
                else
                {
                    _logging_service_safe().LogWarning($"Patch failed for {workItemId}: {response.StatusCode} - {content}");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logging_service_safe().LogError($"Exception in PatchWorkItemFieldsAsync: {ex.Message}", ex);
            return false;
        }
    }

        /// <summary>
        /// Compare desired vs existing ADO work item fields and patch differences.
        /// </summary>
        public async Task<(bool patched, List<string> patchedFields)> CompareAndPatchDifferencesAsync(ConnectionSettings settings, int workItemId, IDictionary<string, object> desired, bool bypassRules = false)
        {
            var patchedFields = new List<string>();
            try
            {
                var existing = await GetWorkItemByIdAsync(settings, workItemId);
                if (existing == null)
                {
                    _logging_service_safe().LogWarning($"Cannot diff: existing work item {workItemId} not retrievable");
                    return (false, patchedFields);
                }

                var existingFields = existing["fields"] as JObject ?? new JObject();
                var diff = new Dictionary<string, object>();
                foreach (var kv in desired)
                {
                    if (kv.Value == null) continue;
                    var currentValue = existingFields[kv.Key]?.ToString();
                    var newValueStr = kv.Value.ToString();
                    if (!string.Equals(currentValue, newValueStr, StringComparison.Ordinal))
                    {
                        diff[kv.Key] = kv.Value;
                    }
                }

                if (diff.Count == 0)
                {
                    _logging_service_safe().LogInfo($"No field differences detected for work item {workItemId}");
                    return (false, patchedFields);
                }

                var success = await PatchWorkItemFieldsAsync(settings, workItemId, diff, bypassRules);
                if (success) patchedFields.AddRange(diff.Keys);
                return (success, patchedFields);
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogWarning($"CompareAndPatchDifferencesAsync failed for {workItemId}: {ex.Message}");
                return (false, patchedFields);
            }
        }

        public async Task<List<AdoWorkItemType>> GetWorkItemTypesAsync(ConnectionSettings settings)
        {
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Bug", "Epic", "Feature", "Issue", "Iteration Goal", "Task", "Test Case", "Test Plan", "Test Suite", "User Story"
            };
            var types = new List<AdoWorkItemType>();
            var projectName = settings.AdoProject;
            
            // Build correct URL based on server URL format
            string url;
            if (settings.AdoServerUrl.Contains("dev.azure.com"))
            {
                // New format: https://dev.azure.com/{organization}/{project}/_apis/...
                url = $"{settings.AdoServerUrl}/{settings.AdoOrganization}/{Uri.EscapeDataString(projectName)}/_apis/wit/workitemtypes?api-version=7.1";
            }
            else
            {
                // Old format: https://{organization}.visualstudio.com/{project}/_apis/...
                url = $"{settings.AdoServerUrl}/{Uri.EscapeDataString(projectName)}/_apis/wit/workitemtypes?api-version=7.1";
            }
            
            using (var client = CreateHttpClient(settings.AdoApiKey))
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jObj = JObject.Parse(content);
                if (jObj["value"] == null)
                {
                    _logging_service_safe().LogError($"Unexpected response when retrieving work item types: {content}");
                    return types;
                }
                foreach (var type in jObj["value"])
                {
                    var typeName = type["name"]?.ToString();
                    if (!string.IsNullOrEmpty(typeName) && allowedTypes.Contains(typeName))
                    {
                        types.Add(new AdoWorkItemType
                        {
                            Name = typeName,
                            Description = type["description"]?.ToString()
                        });
                    }
                }
            }
            return types;
        }

        public async Task<List<AdoWorkItemType>> GetWorkItemTypesAsync()
        {
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Bug", "Epic", "Feature", "Issue", "Iteration Goal", "Task", "Test Case", "Test Plan", "Test Suite", "User Story"
            };
            var types = new List<AdoWorkItemType>();
            var url = BuildApiUrl($"{Uri.EscapeDataString(_projectName)}/_apis/wit/workitemtypes?api-version=7.1");
            using (var client = CreateHttpClient())
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jObj = JObject.Parse(content);
                if (jObj["value"] == null)
                {
                    _logging_service_safe().LogError($"Unexpected response when retrieving work item types: {content}");
                    return types;
                }
                foreach (var type in jObj["value"])
                {
                    var name = type["name"]?.ToString();
                    if (name == null) continue;
                    if (allowedTypes.Contains(name))
                    {
                        types.Add(new AdoWorkItemType
                        {
                            Name = name,
                            ReferenceName = type["referenceName"]?.ToString(),
                            Description = type["description"]?.ToString()
                        });
                    }
                }
            }
            return types;
        }

        public async Task<List<AdoField>> GetFieldsForWorkItemTypeAsync(string workItemType)
        {
            var fields = new List<AdoField>();
            var url = BuildApiUrl($"{Uri.EscapeDataString(_projectName)}/_apis/wit/workitemtypes/{Uri.EscapeDataString(workItemType)}/fields?api-version=7.1");
            using (var client = CreateHttpClient())
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jObj = JObject.Parse(content);
                if (jObj["value"] == null)
                {
                    _logging_service_safe().LogError($"Unexpected response when retrieving fields for {workItemType}: {content}");
                    return fields;
                }
                foreach (var field in jObj["value"]
)
                {
                    var name = field["name"]?.ToString();
                    var reference = field["referenceName"]?.ToString();
                    var type = field["type"]?.ToString();
                    var desc = field["description"]?.ToString();
                    var isReq = field["required"]?.ToObject<bool>() ?? false;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(reference))
                    {
                        _logging_service_safe().LogWarning($"Skipping field with missing data for work item type {workItemType}: {field}");
                        continue;
                    }
                    fields.Add(new AdoField
                    {
                        Name = name,
                        ReferenceName = reference,
                        Type = type,
                        Description = desc,
                        IsRequired = isReq
                    });
                }
            }
            return fields;
        }

        public async Task<List<AdoField>> GetGlobalFieldsAsync(ConnectionSettings settings)
        {
            var fields = new List<AdoField>();
            
            // Build correct URL based on server URL format
            string url;
            if (settings.AdoServerUrl.Contains("dev.azure.com"))
            {
                // New format: https://dev.azure.com/{organization}/_apis/...
                url = $"{settings.AdoServerUrl}/{settings.AdoOrganization}/_apis/wit/fields?api-version=7.1";
            }
            else
            {
                // Old format: https://{organization}.visualstudio.com/_apis/...
                url = $"{settings.AdoServerUrl}/_apis/wit/fields?api-version=7.1";
            }
            
            using (var client = CreateHttpClient(settings.AdoApiKey))
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jObj = JObject.Parse(content);
                if (jObj["value"] == null)
                {
                    _logging_service_safe().LogError($"Unexpected response when retrieving global fields: {content}");
                    return fields;
                }
                foreach (var field in jObj["value"]
)
                {
                    var name = field["name"]?.ToString();
                    var reference = field["referenceName"]?.ToString();
                    var type = field["type"]?.ToString();
                    var desc = field["description"]?.ToString();
                    var isReq = field["required"]?.ToObject<bool>() ?? false;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(reference))
                        continue;
                    fields.Add(new AdoField
                    {
                        Name = name,
                        ReferenceName = reference,
                        Type = type,
                        Description = desc,
                        IsRequired = isReq
                    });
                }
            }
            return fields;
        }

        public async Task<List<AdoField>> GetGlobalFieldsAsync()
        {
            var fields = new List<AdoField>();
            var url = BuildApiUrl($"_apis/wit/fields?api-version=7.1");
            using (var client = CreateHttpClient())
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var jObj = JObject.Parse(content);
                if (jObj["value"] == null)
                {
                    _logging_service_safe().LogError($"Unexpected response when retrieving global fields: {content}");
                    return fields;
                }
                foreach (var field in jObj["value"]
)
                {
                    var name = field["name"]?.ToString();
                    var reference = field["referenceName"]?.ToString();
                    var type = field["type"]?.ToString();
                    var desc = field["description"]?.ToString();
                    var isReq = field["required"]?.ToObject<bool>() ?? false;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(reference))
                        continue;
                    fields.Add(new AdoField
                    {
                        Name = name,
                        ReferenceName = reference,
                        Type = type,
                        Description = desc,
                        IsRequired = isReq
                    });
                }
            }
            return fields;
        }

        // Create work item using JSON Patch
        public async Task<AdoWorkItemResult> CreateWorkItemWithFallbackAsync(ConnectionSettings settings, object adoFieldsObj)
        {
            try
            {
                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    baseApiPath = $"{serverUrl}/{project}";
                }
                else
                {
                    if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        baseApiPath = $"{serverUrl}/{organization}/{project}";
                    else
                        baseApiPath = $"{serverUrl}/{project}";
                }

                Dictionary<string, object> adoFields = null;
                if (adoFieldsObj is Dictionary<string, object> dict)
                    adoFields = dict;
                else if (adoFieldsObj is JObject j)
                    adoFields = j.ToObject<Dictionary<string, object>>();
                else
                {
                    try { adoFields = JObject.FromObject(adoFieldsObj).ToObject<Dictionary<string, object>>(); }
                    catch { _logging_service_safe().LogError("Unsupported adoFields object type for CreateWorkItemWithFallbackAsync"); return null; }
                }

                string workItemType = null;
                if (adoFields != null && adoFields.ContainsKey("System.WorkItemType"))
                {
                    workItemType = adoFields["System.WorkItemType"]?.ToString();
                    adoFields.Remove("System.WorkItemType");
                }
                if (string.IsNullOrEmpty(workItemType)) workItemType = "Task";

                var patchOps = new List<Dictionary<string, object>>();
                string assignedToOriginalValue = null;
                int assignedOpIndex = -1;

                var keys = adoFields.Keys.ToList();
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    var value = adoFields[key];
                    if (value == null) continue;

                    if (key.Equals("System.Tags", StringComparison.OrdinalIgnoreCase) && value is IEnumerable<string> tags)
                        value = string.Join(";", tags);

                    var op = new Dictionary<string, object>
                    {
                        ["op"] = "add",
                        ["path"] = $"/fields/{key}",
                        ["value"] = value
                    };

                    if (key.Equals("System.AssignedTo", StringComparison.OrdinalIgnoreCase))
                    {
                        assignedToOriginalValue = value?.ToString();
                        assignedOpIndex = patchOps.Count;
                    }

                    patchOps.Add(op);
                }

                var requestUrl = $"{baseApiPath}/_apis/wit/workitems/${Uri.EscapeDataString(workItemType)}?api-version=7.1";

                using (var client = CreateHttpClient(pat))
                {
                    // Try candidate AssignedTo emails if needed
                    if (!string.IsNullOrEmpty(assignedToOriginalValue) && !assignedToOriginalValue.Contains("@"))
                    {
                        var preferOptum = settings?.PreferOptumFirst ?? true;
                        var candidates = GenerateAssignedToCandidates(assignedToOriginalValue, preferOptum);
                        var jsonSettings = new Newtonsoft.Json.JsonSerializerSettings { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.Default };
                        // Validate candidates against Graph to prioritize existing users
                        var validated = new List<string>();
                        var unvalidated = new List<string>();
                        foreach (var c in candidates)
                        {
                            try
                            {
                                var exists = await FindUserByEmailAsync(settings, c);
                                if (exists) validated.Add(c);
                                else unvalidated.Add(c);
                            }
                            catch
                            {
                                unvalidated.Add(c);
                            }
                        }

                        var orderedCandidates = validated.Concat(unvalidated).ToList();

                        foreach (var candidate in orderedCandidates)
                        {
                            try
                            {
                                if (assignedOpIndex >= 0 && assignedOpIndex < patchOps.Count)
                                    patchOps[assignedOpIndex]["value"] = candidate;

                                var attemptJson = Newtonsoft.Json.JsonConvert.SerializeObject(patchOps, jsonSettings);
                                var attemptReq = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl)
                                {
                                    Content = new StringContent(attemptJson, Encoding.UTF8, "application/json-patch+json")
                                };

                                _logging_service_safe().LogInfo($"Attempting create with AssignedTo: {candidate}");
                                var attemptResp = await client.SendAsync(attemptReq);
                                var attemptContent = await attemptResp.Content.ReadAsStringAsync();
                                if (attemptResp.IsSuccessStatusCode)
                                {
                                    var j = JObject.Parse(attemptContent);
                                    var id = j["id"]?.ToObject<int>() ?? 0;
                                    var webUrl = j["_links"]?["html"]?["href"]?.ToString();
                                    return new AdoWorkItemResult { Id = id, Url = webUrl };
                                }
                                else
                                {
                                    _logging_service_safe().LogWarning($"Attempt with AssignedTo={candidate} failed: {attemptResp.StatusCode} - {attemptContent}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logging_service_safe().LogWarning($"Exception attempting AssignedTo candidate {candidate}: {ex.Message}");
                            }
                        }
                        _logging_service_safe().LogWarning("All AssignedTo candidate attempts failed, attempting original payload");
                    }

                    var finalJsonSettings = new Newtonsoft.Json.JsonSerializerSettings { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.Default };
                    var patchJson = Newtonsoft.Json.JsonConvert.SerializeObject(patchOps, finalJsonSettings);
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl)
                    {
                        Content = new StringContent(patchJson, Encoding.UTF8, "application/json-patch+json")
                    };

                    var response = await client.SendAsync(request);
                    var content = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var j = JObject.Parse(content);
                        var id = j["id"]?.ToObject<int>() ?? 0;
                        var webUrl = j["_links"]?["html"]?["href"]?.ToString();
                        return new AdoWorkItemResult { Id = id, Url = webUrl };
                    }
                    else
                    {
                        // Final fallback: Remove AssignedTo field and try creating unassigned
                        if (!string.IsNullOrEmpty(assignedToOriginalValue))
                        {
                            _logging_service_safe().LogWarning($"Creation with AssignedTo failed ({response.StatusCode}), attempting final fallback: creating as unassigned");
                            
                            // Remove the AssignedTo operation from patch operations
                            if (assignedOpIndex >= 0 && assignedOpIndex < patchOps.Count)
                            {
                                patchOps.RemoveAt(assignedOpIndex);
                                _logging_service_safe().LogInfo("Removed System.AssignedTo field for final unassigned creation attempt");
                            }
                            
                            // Try creating without AssignedTo field
                            var unassignedJson = Newtonsoft.Json.JsonConvert.SerializeObject(patchOps, finalJsonSettings);
                            var unassignedRequest = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl)
                            {
                                Content = new StringContent(unassignedJson, Encoding.UTF8, "application/json-patch+json")
                            };
                            
                            var unassignedResponse = await client.SendAsync(unassignedRequest);
                            var unassignedContent = await unassignedResponse.Content.ReadAsStringAsync();
                            
                            if (unassignedResponse.IsSuccessStatusCode)
                            {
                                _logging_service_safe().LogInfo($"✅ Successfully created work item as unassigned after user validation failure");
                                var j = JObject.Parse(unassignedContent);
                                var id = j["id"]?.ToObject<int>() ?? 0;
                                var webUrl = j["_links"]?["html"]?["href"]?.ToString();
                                return new AdoWorkItemResult { Id = id, Url = webUrl };
                            }
                            else
                            {
                                _logging_service_safe().LogError($"Final unassigned fallback also failed. Status: {unassignedResponse.StatusCode}. Response: {unassignedContent}");
                            }
                        }
                        
                        _logging_service_safe().LogError($"Failed to create ADO work item. Status: {response.StatusCode}. Response: {content}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError("Exception creating ADO work item", ex);
                return null;
            }
        }

        public async Task<JObject> GetWorkItemByIdAsync(ConnectionSettings settings, int id)
        {
            try
            {
                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    baseApiPath = $"{serverUrl}/{project}";
                }
                else
                {
                    if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        baseApiPath = $"{serverUrl}/{organization}/{project}";
                    else
                        baseApiPath = $"{serverUrl}/{project}";
                }

                var requestUrl = $"{baseApiPath}/_apis/wit/workitems/{id}?api-version=7.1";
                using (var client = CreateHttpClient(pat))
                {
                    var resp = await client.GetAsync(requestUrl);
                    var content = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode)
                        return JObject.Parse(content);

                    // try fallback org-level
                    var fallbackUrls = new List<string>();
                    if (!string.IsNullOrEmpty(serverUrl) && serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        fallbackUrls.Add($"{serverUrl}/{organization}/_apis/wit/workitems/{id}?api-version=7.1");
                    if (!string.IsNullOrEmpty(serverUrl) && serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                        fallbackUrls.Add($"{serverUrl}/_apis/wit/workitems/{id}?api-version=7.1");

                    foreach (var urlTry in fallbackUrls)
                    {
                        try
                        {
                            var r2 = await client.GetAsync(urlTry);
                            var c2 = await r2.Content.ReadAsStringAsync();
                            if (r2.IsSuccessStatusCode) return JObject.Parse(c2);
                        }
                        catch { }
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError($"Exception in GetWorkItemByIdAsync: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Get work item by ID with relations expanded (for checking attachments/links)
        /// </summary>
        public async Task<JObject> GetWorkItemWithRelationsAsync(ConnectionSettings settings, int id)
        {
            try
            {
                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    baseApiPath = $"{serverUrl}/{project}";
                }
                else
                {
                    if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        baseApiPath = $"{serverUrl}/{organization}/{project}";
                    else
                        baseApiPath = $"{serverUrl}/{project}";
                }

                // CRITICAL: Add $expand=relations to get attachment data
                var requestUrl = $"{baseApiPath}/_apis/wit/workitems/{id}?$expand=relations&api-version=7.1";
                using (var client = CreateHttpClient(pat))
                {
                    var resp = await client.GetAsync(requestUrl);
                    var content = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode)
                        return JObject.Parse(content);

                    _logging_service_safe().LogWarning($"Failed to get work item {id} with relations. Status: {resp.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError($"Exception in GetWorkItemWithRelationsAsync: {ex.Message}", ex);
                return null;
            }
        }

        public async Task<bool> WorkItemExistsAsync(ConnectionSettings settings, string rallyObjectId)
        {
            try
            {
                var tag = $"RallyObjectID-{rallyObjectId}";
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;
                var requestUrl = BuildApiUrl($"{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version=7.1");

                var wiql = new { query = $"Select [System.Id] From WorkItems Where [System.TeamProject] = '{project}' AND [System.Tags] CONTAINS '{tag}'" };
                using (var client = CreateHttpClient(pat))
                {
                    var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(wiql), Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync(requestUrl, content);
                    var respContent = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) return false;
                    var j = JObject.Parse(respContent);
                    var workItems = j["workItems"] as JArray;
                    return workItems != null && workItems.Count > 0;
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogWarning($"WorkItemExistsAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<Dictionary<string, bool>> CheckWorkItemsExistBatchAsync(ConnectionSettings settings, List<string> rallyObjectIds, int maxConcurrency)
        {
            var results = new Dictionary<string, bool>();
            if (rallyObjectIds == null || rallyObjectIds.Count == 0) return results;

            var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency <= 0 ? 4 : maxConcurrency);
            var tasks = new List<Task>();
            foreach (var id in rallyObjectIds)
            {
                await semaphore.WaitAsync();
                var t = Task.Run(async () =>
                {
                    try
                    {
                        var exists = await WorkItemExistsAsync(settings, id);
                        lock (results) { results[id] = exists; }
                    }
                    catch (Exception ex)
                    {
                        _logging_service_safe().LogWarning($"CheckWorkItemsExistBatchAsync failed for {id}: {ex.Message}");
                        lock (results) { results[id] = false; }
                    }
                    finally { semaphore.Release(); }
                });
                tasks.Add(t);
            }
            await Task.WhenAll(tasks);
            return results;
        }

        public async Task<bool> LinkWorkItemsAsync(ConnectionSettings settings, int parentAdoId, int childAdoId, string linkType)
        {
            try
            {
                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseApiPath = $"{serverUrl}/{project}";
                else if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                    baseApiPath = $"{serverUrl}/{organization}/{project}";
                else baseApiPath = $"{serverUrl}/{project}";

                var requestUrl = $"{baseApiPath}/_apis/wit/workitems/{parentAdoId}?api-version=7.1";
                var relationUrl = $"{baseApiPath}/_apis/wit/workItems/{childAdoId}";
                var patch = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["op"] = "add",
                        ["path"] = "/relations/-",
                        ["value"] = new Dictionary<string, object>
                        {
                            ["rel"] = "System.LinkTypes.Hierarchy-Forward",
                            ["url"] = relationUrl,
                            ["attributes"] = new Dictionary<string, object> { ["comment"] = "Linked by Rally?ADO migration" }
                        }
                    }
                };

                var patchJson = Newtonsoft.Json.JsonConvert.SerializeObject(patch);
                using (var client = CreateHttpClient(pat))
                {
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl)
                    {
                        Content = new StringContent(patchJson, Encoding.UTF8, "application/json-patch+json")
                    };
                    var resp = await client.SendAsync(request);
                    if (resp.IsSuccessStatusCode) return true;
                    var content = await resp.Content.ReadAsStringAsync();
                    _logging_service_safe().LogWarning($"LinkWorkItemsAsync failed: {resp.StatusCode} - {content}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError($"Exception in LinkWorkItemsAsync: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> UploadAttachmentAsync(ConnectionSettings settings, int adoWorkItemId, object attachment)
        {
            var result = await UploadAttachmentWithUrlAsync(settings, adoWorkItemId, attachment);
            return !string.IsNullOrEmpty(result);
        }

        /// <summary>
        /// Upload attachment and return the ADO attachment URL for embedding in descriptions
        /// </summary>
        public async Task<string> UploadAttachmentWithUrlAsync(ConnectionSettings settings, int adoWorkItemId, object attachment)
        {
            try
            {
                var rallyAttachment = attachment as RallyAttachment;
                if (rallyAttachment == null) { _logging_service_safe().LogWarning("UploadAttachmentWithUrlAsync: attachment is not RallyAttachment"); return null; }

                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseApiPath = $"{serverUrl}/{project}";
                else if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                    baseApiPath = $"{serverUrl}/{organization}/{project}";
                else baseApiPath = $"{serverUrl}/{project}";

                var attachUrl = $"{baseApiPath}/_apis/wit/attachments?fileName={Uri.EscapeDataString(rallyAttachment.Name)}&api-version=7.1";
                using (var client = CreateHttpClient(pat))
                {
                    var content = new ByteArrayContent(rallyAttachment.Content ?? new byte[0]);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    var resp = await client.PostAsync(attachUrl, content);
                    var respContent = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) { _logging_service_safe().LogWarning($"UploadAttachmentWithUrlAsync upload failed: {resp.StatusCode} - {respContent}"); return null; }

                    var j = JObject.Parse(respContent);
                    var attachmentUrl = j["url"]?.ToString();
                    if (string.IsNullOrEmpty(attachmentUrl)) { _logging_service_safe().LogWarning($"UploadAttachmentWithUrlAsync: no attachment url returned: {respContent}"); return null; }

                    var requestUrl = $"{baseApiPath}/_apis/wit/workitems/{adoWorkItemId}?api-version=7.1";
                    var patch = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["op"] = "add",
                            ["path"] = "/relations/-",
                            ["value"] = new Dictionary<string, object>
                            {
                                ["rel"] = "AttachedFile",
                                ["url"] = attachmentUrl,
                                ["attributes"] = new Dictionary<string, object> { ["comment"] = "Migrated attachment from Rally" }
                            }
                        }
                    };

                    var patchJson = Newtonsoft.Json.JsonConvert.SerializeObject(patch);
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl)
                    {
                        Content = new StringContent(patchJson, Encoding.UTF8, "application/json-patch+json")
                    };

                    var resp2 = await client.SendAsync(request);
                    var resp2Content = await resp2.Content.ReadAsStringAsync();
                    if (!resp2.IsSuccessStatusCode) { _logging_service_safe().LogWarning($"UploadAttachmentWithUrlAsync attach to work item failed: {resp2.StatusCode} - {resp2Content}"); return null; }
                    
                    // Return the attachment URL for embedding in descriptions
                    return attachmentUrl;
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError($"UploadAttachmentWithUrlAsync exception: {ex.Message}", ex);
                return null;
            }
        }

        public async Task<bool> AddCommentAsync(ConnectionSettings settings, int adoWorkItemId, object comment)
        {
            try
            {
                var rallyComment = comment as RallyComment;
                if (rallyComment == null) return false;

                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseApiPath = $"{serverUrl}/{project}";
                else if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                    baseApiPath = $"{serverUrl}/{organization}/{project}";
                else baseApiPath = $"{serverUrl}/{project}";

                var requestUrl = $"{baseApiPath}/_apis/wit/workItems/{adoWorkItemId}/comments?api-version=7.1-preview.3";
                
                // Decode Unicode escape sequences in comment text (e.g., \u003C to <)
                var commentText = rallyComment.Text;
                if (!string.IsNullOrEmpty(commentText))
                {
                    try
                    {
                        commentText = System.Text.RegularExpressions.Regex.Unescape(commentText);
                    }
                    catch (Exception unescapeEx)
                    {
                        _logging_service_safe().LogWarning($"Failed to unescape comment text: {unescapeEx.Message}, using original text");
                    }
                }
                
                var payload = new { text = commentText };
                using (var client = CreateHttpClient(pat))
                {
                    var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync(requestUrl, content);
                    var respContent = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) { _logging_service_safe().LogWarning($"AddCommentAsync failed: {resp.StatusCode} - {respContent}"); return false; }
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError($"AddCommentAsync exception: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<string> DiagnoseConnectionAsync(ConnectionSettings settings)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== ADO DIAGNOSTICS ===");
                sb.AppendLine($"Organization: {settings.AdoOrganization ?? "NOT SET"}");
                sb.AppendLine($"Project: {settings.AdoProject ?? "NOT SET"}");
                sb.AppendLine($"Server URL: {settings.AdoServerUrl ?? "NOT SET"}");
                sb.AppendLine($"API Key: {(string.IsNullOrEmpty(settings.AdoApiKey) ? "NOT SET" : "SET")}");
                
                if (string.IsNullOrEmpty(settings.AdoOrganization) || string.IsNullOrEmpty(settings.AdoProject) || string.IsNullOrEmpty(settings.AdoApiKey))
                {
                    sb.AppendLine("? Missing required connection settings");
                    return sb.ToString();
                }
                
                try { var types = await GetWorkItemTypesAsync(settings); sb.AppendLine($"? Work item types returned: {types.Count}"); } catch (Exception ex) { sb.AppendLine($"? Work item types retrieval failed: {ex.Message}"); }
                try { var globals = await GetGlobalFieldsAsync(settings); sb.AppendLine($"? Global fields returned: {globals.Count}"); } catch (Exception ex) { sb.AppendLine($"? Global fields retrieval failed: {ex.Message}"); }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogError($"DiagnoseConnectionAsync failed: {ex.Message}", ex);
                return $"ERROR: {ex.Message}";
            }
        }

        public async Task<bool> TestConnectionAsync(ConnectionSettings settings)
        {
            try 
            { 
                if (string.IsNullOrEmpty(settings.AdoOrganization) || string.IsNullOrEmpty(settings.AdoProject) || string.IsNullOrEmpty(settings.AdoApiKey))
                {
                    _logging_service_safe().LogError("ADO connection settings are incomplete");
                    return false;
                }
                
                var types = await GetWorkItemTypesAsync(settings); 
                return types != null && types.Count > 0; 
            }
            catch (Exception ex) { _logging_service_safe().LogWarning($"TestConnectionAsync failed: {ex.Message}"); return false; }
        }

        public async Task<string> TestEndpointAsync(string url, string apiKey)
        {
            try
            {
                using (var client = CreateHttpClient(apiKey))
                {
                    var resp = await client.GetAsync(url);
                    var content = await resp.Content.ReadAsStringAsync();
                    return content ?? "";
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        private LoggingService _logging_service_safe() => _loggingService ?? new LoggingService();

        private List<string> GenerateAssignedToCandidates(string displayName, bool preferOptumFirst)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(displayName)) return candidates;
            var name = displayName.Trim().Replace("\"", "");
            string first = null, last = null;
            if (name.Contains(","))
            {
                var parts = name.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    last = parts[0].Trim();
                    first = parts[1].Trim().Split(' ')[0];
                }
            }
            else
            {
                var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) { first = parts[0]; last = parts[parts.Length - 1]; }
                else if (parts.Length == 1) { first = parts[0]; last = parts[0]; }
            }
            if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
            {
                first = first.ToLower();
                last = last.ToLower();
                var candidateBase1 = $"{first}.{last}".Replace(" ", "").Replace("..", ".");
                var candidateBase2 = $"{first[0]}.{last}".Replace(" ", "");
                var candidateBase3 = $"{first}{last}".Replace(" ", "");
                var candidateBase4 = $"{first}{last[0]}".Replace(" ", "");

                var bases = new List<string> { candidateBase1, candidateBase2, candidateBase3, candidateBase4 };
                // remove empties and duplicates
                bases = bases.Where(b => !string.IsNullOrEmpty(b)).Distinct().ToList();

                var domains = preferOptumFirst ? new[] { "@optum.com", "@emishealth.com" } : new[] { "@emishealth.com", "@optum.com" };

                foreach (var b in bases)
                {
                    foreach (var d in domains)
                    {
                        candidates.Add(b + d);
                    }
                }
            }
            return candidates.Distinct().ToList();
        }

        public void Dispose() { }

        /// <summary>
        /// Try to find a user in Azure DevOps by email/principal name using Graph API
        /// </summary>
        public async Task<bool> FindUserByEmailAsync(ConnectionSettings settings, string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email)) return false;
                var organization = settings?.AdoOrganization?.Trim();
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                // Use vssps.dev.azure.com endpoint for Graph users
                var baseUrl = $"https://vssps.dev.azure.com/{organization}";
                var requestUrl = $"{baseUrl}/_apis/graph/users?api-version=7.1-preview.1";

                using (var client = CreateHttpClient(pat))
                {
                    var resp = await client.GetAsync(requestUrl);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _logging_service_safe().LogWarning($"FindUserByEmailAsync: Graph users query failed: {resp.StatusCode}");
                        return false;
                    }

                    var content = await resp.Content.ReadAsStringAsync();
                    var j = JObject.Parse(content);
                    var users = j["value"] as JArray;
                    if (users == null || users.Count == 0) return false;

                    var lower = email.Trim().ToLowerInvariant();
                    foreach (var u in users)
                    {
                        var principal = u["principalName"]?.ToString()?.ToLowerInvariant();
                        var mail = u["mailAddress"]?.ToString()?.ToLowerInvariant();
                        var display = u["displayName"]?.ToString()?.ToLowerInvariant();
                        if (principal == lower || mail == lower || display == lower) return true;
                        // sometimes principal contains the email as part
                        if (!string.IsNullOrEmpty(principal) && principal.Contains(lower)) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogWarning($"FindUserByEmailAsync exception: {ex.Message}");
            }
            return false;
        }

        // Insert new method near other API helpers
        public async Task<int> FindExistingWorkItemIdByRallyTagsAsync(ConnectionSettings settings, string formattedId, string objectId)
        {
            try
            {
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;
                var tag1 = $"Rally-{formattedId}";
                var tag2 = $"RallyObjectID-{objectId}";
                var wiql = new { query = $"Select [System.Id] From WorkItems Where [System.TeamProject] = '{project}' AND ([System.Tags] CONTAINS '{tag1}' OR [System.Tags] CONTAINS '{tag2}')" };
                var requestUrl = BuildApiUrl($"{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version=7.1");
                using (var client = CreateHttpClient(pat))
                {
                    var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(wiql), Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync(requestUrl, content);
                    var respContent = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) return -1;
                    var j = JObject.Parse(respContent);
                    var workItems = j["workItems"] as JArray;
                    if (workItems != null && workItems.Count > 0)
                    {
                        var id = workItems[0]["id"]?.ToObject<int>() ?? -1;
                        return id;
                    }
                }
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogWarning($"FindExistingWorkItemIdByRallyTagsAsync error: {ex.Message}");
            }
            return -1;
        }

        public async Task<bool> AddTestCaseStepsAsync(ConnectionSettings settings, int adoTestCaseId, IEnumerable<RallyTestCaseStep> steps, bool bypassRules = false)
        {
            try
            {
                if (steps == null || !steps.Any()) return true;
                
                _logging_service_safe().LogInfo($"[STEPS_API] Building XML for {steps.Count()} steps");
                
                // Set the logger for TestStepsXmlBuilder
                TestStepsXmlBuilder.SetLogger(_logging_service_safe());
                
                // **CRITICAL FIX**: Build complete Steps XML once and patch as single operation
                // Previous code was creating multiple patch operations for the same field, causing duplicate update error
                var stepsXml = TestStepsXmlBuilder.BuildTestStepsXml(steps.ToList());
                
                if (string.IsNullOrEmpty(stepsXml))
                {
                    _logging_service_safe().LogWarning("Generated Steps XML is empty, skipping");
                    return false;
                }
                
                _logging_service_safe().LogInfo($"[STEPS_API] XML generated: {stepsXml.Length} characters");
                _logging_service_safe().LogInfo($"[STEPS_API] Sending to ADO work item {adoTestCaseId}");
                
                // Use the PatchWorkItemFieldsAsync method which handles the Steps field correctly
                var stepsField = new Dictionary<string, object>
                {
                    ["Microsoft.VSTS.TCM.Steps"] = stepsXml
                };
                
                return await PatchWorkItemFieldsAsync(settings, adoTestCaseId, stepsField, bypassRules);
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogWarning($"AddTestCaseStepsAsync failed: {ex.Message}");
                return false;
            }
        }

        // Ensure IterationPath exists (create missing iterations hierarchy)
        public async Task<bool> EnsureIterationPathExistsAsync(ConnectionSettings settings, string iterationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(iterationPath)) return true;
                var norm = iterationPath.Replace('/', '\\').Trim();
                var segments = norm.Split(new[] {'\\'}, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0) return true;

                var organization = settings?.AdoOrganization?.Trim();
                var project = settings?.AdoProject?.Trim() ?? _projectName;
                var pat = settings?.AdoApiKey ?? _personalAccessToken;
                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";

                string baseApiPath;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseApiPath = $"{serverUrl}/{project}";
                else if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                    baseApiPath = $"{serverUrl}/{organization}/{project}";
                else baseApiPath = $"{serverUrl}/{project}";

                var listUrl = $"{baseApiPath}/_apis/wit/classificationnodes/iterations?$depth=10&api-version=7.1";
                var existingPaths = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); // path->id

                using (var client = CreateHttpClient(pat))
                {
                    // Fetch existing iteration tree
                    try
                    {
                        var resp = await client.GetAsync(listUrl);
                        if (resp.IsSuccessStatusCode)
                        {
                            var json = await resp.Content.ReadAsStringAsync();
                            var root = JObject.Parse(json);
                            void Walk(JToken node, string currentPath)
                            {
                                if (node == null) return;
                                var name = node["name"]?.ToString();
                                var id = node["id"]?.ToString();
                                string path = currentPath;
                                if (!string.IsNullOrEmpty(name))
                                {
                                    path = string.IsNullOrEmpty(currentPath) ? name : currentPath + "\\" + name;
                                    if (!existingPaths.ContainsKey(path) && !string.IsNullOrEmpty(id)) existingPaths[path] = id;
                                }
                                var children = node["children"] as JArray;
                                if (children != null)
                                {
                                    foreach (var c in children) Walk(c, path);
                                }
                            }
                            Walk(root, "");
                        }
                        else
                        {
                            _logging_service_safe().LogWarning($"Failed to list iterations: {resp.StatusCode}");
                        }
                    }
                    catch (Exception exTree)
                    {
                        _logging_service_safe().LogWarning($"Iteration listing exception: {exTree.Message}");
                    }

                    // Build the full path step by step, create segments as needed
                    string accumulated = "";
                    string parentId = existingPaths.ContainsKey(segments[0]) ? existingPaths[segments[0]] : null;

                    for (int i = 0; i < segments.Length; i++)
                    {
                        accumulated = i == 0 ? segments[0] : accumulated + "\\" + segments[i];
                        if (existingPaths.ContainsKey(accumulated))
                        {
                            parentId = existingPaths[accumulated];
                            continue; // already exists
                        }

                        // Need to create this segment
                        string createUrl;
                        if (i == 0) // root level create
                            createUrl = $"{baseApiPath}/_apis/wit/classificationnodes/iterations?api-version=7.1";
                        else if (!string.IsNullOrEmpty(parentId))
                            createUrl = $"{baseApiPath}/_apis/wit/classificationnodes/iterations/{parentId}?api-version=7.1";
                        else
                        {
                            _logging_service_safe().LogWarning($"Cannot determine parent for iteration segment '{segments[i]}'");
                            return false;
                        }

                        var body = new JObject { ["name"] = segments[i] };
                        var req = new HttpRequestMessage(HttpMethod.Post, createUrl)
                        {
                            Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
                        };
                        var createResp = await client.SendAsync(req);
                        var createContent = await createResp.Content.ReadAsStringAsync();
                        if (!createResp.IsSuccessStatusCode)
                        {
                            _logging_service_safe().LogWarning($"Failed to create iteration '{accumulated}': {createResp.StatusCode} - {createContent}");
                            return false;
                        }
                        try
                        {
                            var createdNode = JObject.Parse(createContent);
                            parentId = createdNode["id"]?.ToString();
                            if (!string.IsNullOrEmpty(parentId)) existingPaths[accumulated] = parentId;
                            _logging_service_safe().LogInfo($"Created iteration: {accumulated}");
                        }
                        catch { }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logging_service_safe().LogWarning($"EnsureIterationPathExistsAsync exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current authenticated user's email from ADO API
        /// </summary>
        public async Task<string> GetCurrentUserAsync(ConnectionSettings settings)
        {
            try
            {
                var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? _organizationUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                var organization = settings?.AdoOrganization?.Trim();
                var pat = settings?.AdoApiKey ?? _personalAccessToken;

                string apiUrl;
                if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    apiUrl = $"{serverUrl}/_apis/profile/profiles/me?api-version=7.1";
                }
                else
                {
                    if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        apiUrl = $"{serverUrl}/{organization}/_apis/profile/profiles/me?api-version=7.1";
                    else
                        apiUrl = $"{serverUrl}/_apis/profile/profiles/me?api-version=7.1";
                }

                using (var client = CreateHttpClient(pat))
                {
                    var response = await client.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var json = JObject.Parse(content);
                        
                        // Try to get email address from profile
                        var emailAddress = json["emailAddress"]?.ToString();
                        if (!string.IsNullOrEmpty(emailAddress))
                        {
                            _loggingService.LogInfo($"Retrieved current user email: {emailAddress}");
                            return emailAddress;
                        }
                    }
                    else
                    {
                        _loggingService.LogWarning($"Failed to get current user profile. Status: {response.StatusCode}");
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error getting current user: {ex.Message}");
                return null;
            }
        }
    }

    public class AdoWorkItemResult { public int Id { get; set; } public string Url { get; set; } }
    public class AdoWorkItemType { public string Name { get; set; } public string ReferenceName { get; set; } public string Description { get; set; } }
    public class AdoField { public string Name { get; set; } public string ReferenceName { get; set; } public string Type { get; set; } public string Description { get; set; } public bool IsRequired { get; set; } }
}
