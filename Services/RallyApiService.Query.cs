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
        private string BuildSingleItemQueryUrl(string itemType, string rallyId, bool isFormattedId)
        {
            var baseUrl = $"{_serverUrl}/slm/webservice/v2.0/{itemType.ToLower()}";
            var workspaceParam = $"workspace=/workspace/{_workspace}";
            var projectParam = !string.IsNullOrEmpty(_project) ? $"&project=/project/{_project}" : "";
            
            // CRITICAL: Rally requires explicit fetch of nested Owner fields
            // Using both "Owner" and "Owner[...]" syntax to ensure compatibility
            // IMPORTANT: PreConditions field added for Test Cases
            // IMPORTANT: Estimate, ToDo, Actuals fields added for Task time tracking
            // FIX: Rally API bug - Only fetch ScheduleState (needed for User Stories), NOT TaskStatus
            // TaskStatus and other state fields trigger a Rally API bug where State returns stale values
            var fetch = "ObjectID,FormattedID,Name,Description,Notes,State,ScheduleState,Owner,Owner[EmailAddress,Email,Name,DisplayName,UserName],CreationDate,LastUpdateDate,PlanEstimate,Estimate,ToDo,Actuals,Priority,Severity,Blocked,Ready,Tags,Project,Iteration,Release,AcceptanceCriteria,Children,Tasks,Parent,PortfolioItem,WorkProduct,TestCases,Method,Type,PreConditions,CreatedBy,CreatedBy[EmailAddress,Email,Name]";
            
            _loggingService.LogInfo($"[RALLY_FETCH] Querying Rally for {rallyId} - Fetching State + ScheduleState only (Rally API bug workaround)");
            
            var query = isFormattedId ? $"?{workspaceParam}{projectParam}&query=(FormattedID%20=%20\"{rallyId}\")&fetch={fetch}" : $"?{workspaceParam}{projectParam}&query=(ObjectID%20=%20{rallyId})&fetch={fetch}";
            return baseUrl + query;
        }

        /// <summary>
        /// Build URL for direct Rally Read endpoint (bypasses query cache)
        /// https://rally1.rallydev.com/slm/webservice/v2.0/task/{ObjectID}
        /// </summary>
        private string BuildDirectReadUrl(string itemType, string objectId)
        {
            var baseUrl = $"{_serverUrl}/slm/webservice/v2.0/{itemType.ToLower()}/{objectId}";
            // FIX: Rally API bug - Only fetch ScheduleState (needed for User Stories), NOT TaskStatus
            // TaskStatus and other state fields trigger a Rally API bug where State returns stale values
            var fetch = "ObjectID,FormattedID,Name,Description,Notes,State,ScheduleState,Owner,Owner[EmailAddress,Email,Name,DisplayName,UserName],CreationDate,LastUpdateDate,PlanEstimate,Estimate,ToDo,Actuals,Priority,Severity,Blocked,Ready,Tags,Project,Iteration,Release,AcceptanceCriteria,Children,Tasks,Parent,PortfolioItem,WorkProduct,TestCases,Method,Type,PreConditions,CreatedBy,CreatedBy[EmailAddress,Email,Name]";
            
            _loggingService.LogInfo($"[RALLY_DIRECT_READ] Using direct Read endpoint for ObjectID {objectId} (bypasses query cache)");
            
            return $"{baseUrl}?fetch={fetch}";
        }

        /// <summary>
        /// Fetch work item using direct Read endpoint (may have fresher state data than Query endpoint)
        /// https://rally1.rallydev.com/slm/webservice/v2.0/task/{ObjectID}
        /// </summary>
        public async Task<RallyWorkItem> GetWorkItemByObjectIdDirectAsync(string itemType, string objectId)
        {
            try
            {
                var url = BuildDirectReadUrl(itemType, objectId);
                _loggingService.LogInfo($"[RALLY_DIRECT_READ] Fetching {itemType} {objectId} via direct Read API");
                _loggingService.LogDebug($"   Direct Read URL: {url}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);
                var resp = await _httpClient.SendAsync(request);
                
                if (!resp.IsSuccessStatusCode)
                {
                    _loggingService.LogWarning($"[RALLY_DIRECT_READ] Failed: {resp.StatusCode}");
                    return null;
                }
                
                var content = await resp.Content.ReadAsStringAsync();
                _loggingService.LogDebug($"[RALLY_DIRECT_READ] Response length: {content.Length} chars");
                
                // Direct Read endpoint returns single object, not QueryResult wrapper
                // Need to wrap it in QueryResult format for ParseRallyResponse
                var wrappedJson = $"{{\"QueryResult\": {{\"TotalResultCount\": 1, \"Results\": [{content}]}}}}";
                
                var items = ParseRallyResponse(wrappedJson, itemType);
                if (items.Count > 0)
                {
                    _loggingService.LogInfo($"[RALLY_DIRECT_READ] ? Successfully fetched {items[0].FormattedID} via direct Read");
                    return items[0];
                }
                else
                {
                    _loggingService.LogWarning($"[RALLY_DIRECT_READ] Failed to parse response");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[RALLY_DIRECT_READ] Error: {ex.Message}", ex);
                return null;
            }
        }

        private string BuildQueryUrl(string itemType, int startIndex, int pageSize)
        {
            var baseUrl = $"{_serverUrl}/slm/webservice/v2.0/{itemType.ToLower()}";
            var workspaceParam = $"workspace=/workspace/{_workspace}";
            var projectParam = !string.IsNullOrEmpty(_project) ? $"&project=/project/{_project}" : "";
            
            // CRITICAL: Rally requires explicit fetch of nested Owner fields
            // Using both "Owner" and "Owner[...]" syntax to ensure compatibility
            // IMPORTANT: PreConditions field added for Test Cases
            // IMPORTANT: Estimate, ToDo, Actuals fields added for Task time tracking
            // FIX: Rally API bug - Only fetch ScheduleState (needed for User Stories), NOT TaskStatus
            // TaskStatus and other state fields trigger a Rally API bug where State returns stale values
            var fetch = "ObjectID,FormattedID,Name,Description,Notes,State,ScheduleState,Owner,Owner[EmailAddress,Email,Name,DisplayName,UserName],CreationDate,LastUpdateDate,PlanEstimate,Estimate,ToDo,Actuals,Priority,Severity,Blocked,Ready,Tags,Project,Iteration,Release,AcceptanceCriteria,Children,Tasks,Parent,PortfolioItem,WorkProduct,TestCases,Method,Type,PreConditions,CreatedBy,CreatedBy[EmailAddress,Email,Name]";
            
            return $"{baseUrl}?{workspaceParam}{projectParam}&fetch={fetch}&start={startIndex}&pagesize={pageSize}&order=CreationDate%20desc";
        }

        public async Task<RallyWorkItem> GetWorkItemByIdAsync(ConnectionSettings settings, string rallyId)
        {
            ConfigureConnection(settings);
            var isFormattedId = !long.TryParse(rallyId, out _);
            var types = new[] { "HierarchicalRequirement", "Defect", "Task", "TestCase", "PortfolioItem/Feature", "PortfolioItem/Epic" };
            
            _loggingService.LogInfo($"Searching for Rally item: {rallyId}");
            _loggingService.LogInfo($"   ID Type: {(isFormattedId ? "FormattedID (e.g., US1234)" : "ObjectID (numeric)")}");
            _loggingService.LogInfo($"   Workspace: {_workspace}");
            _loggingService.LogInfo($"   Project: {_project}");
            
            foreach (var t in types)
            {
                var url = BuildSingleItemQueryUrl(t, rallyId, isFormattedId);
                _loggingService.LogDebug($"   Trying type: {t}");
                _loggingService.LogDebug($"   Query URL: {url}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url); AddAuthenticationHeader(request);
                var resp = await _httpClient.SendAsync(request);
                
                _loggingService.LogDebug($"   HTTP Status: {resp.StatusCode}");
                
                if (!resp.IsSuccessStatusCode)
                {
                    var errorContent = await resp.Content.ReadAsStringAsync();
                    _loggingService.LogDebug($"   Response (failed): {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                    continue;
                }
                
                var content = await resp.Content.ReadAsStringAsync();
                _loggingService.LogDebug($"   Response length: {content.Length} chars");
                
                // Log first 1000 chars of Rally response to see what we're getting
                _loggingService.LogInfo($"   ?? Rally API Response (first 1000 chars):");
                _loggingService.LogInfo($"   {content.Substring(0, Math.Min(1000, content.Length))}");
                
                var items = ParseRallyResponse(content, t);
                _loggingService.LogDebug($"   Parsed items count: {items.Count}");
                
                if (items.Count > 0)
                {
                    var wi = items[0];
                    _loggingService.LogInfo($"? Found: {wi.FormattedID} ({t}) - {wi.Name}");
                    
                    // FIX: Rally API Bug - For Tasks, fetch State using minimal query (PowerShell-style)
                    // The full query returns stale State values, but minimal query works correctly
                    if (string.Equals(t, "Task", StringComparison.OrdinalIgnoreCase))
                    {
                        _loggingService.LogInfo($"");
                        _loggingService.LogInfo($"??  [TASK_STATE_FIX] Detected Task - fetching State using minimal query...");
                        _loggingService.LogInfo($"   Current State from full query: '{wi.State}'");
                        
                        var freshState = await GetTaskStateMinimalAsync(wi.FormattedID);
                        if (!string.IsNullOrEmpty(freshState))
                        {
                            if (!string.Equals(wi.State, freshState, StringComparison.OrdinalIgnoreCase))
                            {
                                _loggingService.LogInfo($"");
                                _loggingService.LogInfo($"?? [STATE_CORRECTED] Minimal query returned different state!");
                                _loggingService.LogInfo($"   ? Full query had: '{wi.State}' (STALE)");
                                _loggingService.LogInfo($"   ? Minimal query has: '{freshState}' (FRESH)");
                                _loggingService.LogInfo($"   Using minimal query result (PowerShell-style)");
                                _loggingService.LogInfo($"");
                                
                                // Use the fresh state from minimal query
                                wi.State = freshState;
                            }
                            else
                            {
                                _loggingService.LogInfo($"   ? Both queries returned same state: '{wi.State}'");
                                _loggingService.LogInfo($"   State is consistent (not stale)");
                            }
                        }
                        else
                        {
                            _loggingService.LogWarning($"??  [TASK_STATE_FIX_FAILED] Could not fetch state via minimal query");
                            _loggingService.LogWarning($"   Continuing with full query result: '{wi.State}'");
                        }
                        _loggingService.LogInfo($"");
                    }
                    
                    // Fetch actual Children/Tasks if Rally returned references
                    await FetchChildrenAndTasksAsync(wi, content);
                    
                    // Fetch linked TestCases for User Stories and Defects
                    if (string.Equals(t, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(t, "Defect", StringComparison.OrdinalIgnoreCase))
                    {
                        wi.TestCases = await FetchLinkedTestCasesAsync(wi, content);
                    }
                    
                    
                    
                    await FetchAttachmentsForWorkItemAsync(wi);
                    await FetchCommentsForWorkItemAsync(wi);
                    await EnrichOwnerEmailAsync(wi); // NEW: Fetch owner email from Rally User API
                    
                    // VERIFICATION: Log final State value before returning (especially for Tasks)
                    if (string.Equals(t, "Task", StringComparison.OrdinalIgnoreCase))
                    {
                        _loggingService.LogInfo($"");
                        _loggingService.LogInfo($"? [FINAL_VERIFICATION] Returning Task {wi.FormattedID} with State='{wi.State}'");
                        _loggingService.LogInfo($"   This State value will be used for ADO migration");
                        _loggingService.LogInfo($"");
                    }
                    
                    return wi;
                }
                else
                {
                    // Log why no items were found
                    if (content.Contains("\"TotalResultCount\":0"))
                    {
                        _loggingService.LogDebug($"   Rally returned 0 results for type {t}");
                    }
                    else
                    {
                        _loggingService.LogDebug($"   Response JSON (first 500 chars): {content.Substring(0, Math.Min(500, content.Length))}");
                    }
                }
            }
            
            // Enhanced error message
            _loggingService.LogWarning($"? Item {rallyId} not found in Rally after checking all work item types");
            _loggingService.LogWarning($"   Possible reasons:");
            _loggingService.LogWarning($"   1. Work item doesn't exist in Rally");
            _loggingService.LogWarning($"   2. Work item is in a different Workspace ({_workspace}) or Project ({_project})");
            _loggingService.LogWarning($"   3. Incorrect FormattedID (typo or wrong format)");
            _loggingService.LogWarning($"   4. Permissions - your Rally API key may not have access to this item");
            _loggingService.LogWarning($"   5. Work item has been deleted or archived in Rally");
            
            return null;
        }

        /// <summary>
        /// Fetch actual Children and Tasks when Rally returns reference objects with Count > 0
        /// </summary>
        private async Task FetchChildrenAndTasksAsync(RallyWorkItem workItem, string originalJson)
        {
            try
            {
                _loggingService.LogInfo($"[CHILDREN_FETCH] Starting child/task fetch for {workItem.FormattedID}");
                
                // Extract Children reference
                var childrenRefMatch = System.Text.RegularExpressions.Regex.Match(originalJson, "\"Children\"\\s*:\\s*\\{[^}]*\"_ref\"\\s*:\\s*\"([^\"]+)\"[^}]*\"Count\"\\s*:\\s*(\\d+)");
                if (childrenRefMatch.Success)
                {
                    int expectedCount = int.Parse(childrenRefMatch.Groups[2].Value);
                    if (expectedCount > 0)
                    {
                        var childrenUrl = childrenRefMatch.Groups[1].Value + "?fetch=ObjectID,State,FormattedID";
                        _loggingService.LogInfo($"[CHILDREN_FETCH] Rally reports {expectedCount} children - fetching all pages...");
                        var childrenIds = await FetchObjectIDsFromUrl(childrenUrl);
                        workItem.Children.AddRange(childrenIds);
                        
                        if (childrenIds.Count != expectedCount)
                        {
                            _loggingService.LogWarning($"[CHILDREN_FETCH] Count mismatch! Expected {expectedCount} children, got {childrenIds.Count}");
                        }
                        else
                        {
                            _loggingService.LogInfo($"[CHILDREN_FETCH] Successfully fetched all {childrenIds.Count} children");
                        }
                    }
                }

                // Extract Tasks reference
                var tasksRefMatch = System.Text.RegularExpressions.Regex.Match(originalJson, "\"Tasks\"\\s*:\\s*\\{[^}]*\"_ref\"\\s*:\\s*\"([^\"]+)\"[^}]*\"Count\"\\s*:\\s*(\\d+)");
                if (tasksRefMatch.Success)
                {
                    int expectedCount = int.Parse(tasksRefMatch.Groups[2].Value);
                    if (expectedCount > 0)
                    {
                        var tasksUrl = tasksRefMatch.Groups[1].Value + "?fetch=ObjectID,State,FormattedID";
                        _loggingService.LogInfo($"[CHILDREN_FETCH] Rally reports {expectedCount} tasks - fetching all pages...");
                        var taskIds = await FetchObjectIDsFromUrl(tasksUrl);
                        workItem.Children.AddRange(taskIds);
                        
                        if (taskIds.Count != expectedCount)
                        {
                            _loggingService.LogWarning($"[CHILDREN_FETCH] Count mismatch! Expected {expectedCount} tasks, got {taskIds.Count}");
                        }
                        else
                        {
                            _loggingService.LogInfo($"[CHILDREN_FETCH] Successfully fetched all {taskIds.Count} tasks");
                        }
                    }
                }

                if (workItem.Children.Any())
                {
                    int beforeDistinct = workItem.Children.Count;
                    workItem.Children = workItem.Children.Distinct().ToList();
                    int afterDistinct = workItem.Children.Count;
                    
                    if (beforeDistinct != afterDistinct)
                    {
                        _loggingService.LogWarning($"[CHILDREN_FETCH] Removed {beforeDistinct - afterDistinct} duplicate ObjectIDs");
                    }
                    
                    _loggingService.LogInfo($"[CHILDREN_FETCH] FINAL: {workItem.FormattedID} has {workItem.Children.Count} unique child/task IDs");
                }
                else
                {
                    _loggingService.LogDebug($"[CHILDREN_FETCH] No children or tasks found for {workItem.FormattedID}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[CHILDREN_FETCH] Failed to fetch children/tasks for {workItem.FormattedID}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Fetch ObjectIDs from a Rally collection URL with pagination support
        /// </summary>
        private async Task<List<string>> FetchObjectIDsFromUrl(string url)
        {
            var allObjectIds = new List<string>();
            try
            {
                // Rally collections need pagination - default pagesize is only 20!
                // For User Stories with many tasks, we need to fetch all pages
                int pageSize = 200; // Max allowed by Rally
                int start = 1;
                bool hasMore = true;
                int totalFetched = 0;
                
                // Add fetch and pagination parameters if not already present
                var baseUrl = url;
                if (!url.Contains("pagesize="))
                {
                    var separator = url.Contains("?") ? "&" : "?";
                    baseUrl = $"{url}{separator}pagesize={pageSize}";
                }
                
                _loggingService.LogInfo($"[FETCH_CHILDREN] Starting paginated fetch from collection URL");
                _loggingService.LogDebug($"[FETCH_CHILDREN] Base URL: {baseUrl}");
                
                while (hasMore)
                {
                    // Add start parameter for pagination
                    var paginatedUrl = baseUrl.Contains("start=") 
                        ? System.Text.RegularExpressions.Regex.Replace(baseUrl, "start=\\d+", $"start={start}")
                        : $"{baseUrl}&start={start}";
                    
                    _loggingService.LogDebug($"[FETCH_CHILDREN] Fetching page: start={start}, pagesize={pageSize}");
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, paginatedUrl);
                    AddAuthenticationHeader(request);
                    var response = await _httpClient.SendAsync(request);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _loggingService.LogWarning($"[FETCH_CHILDREN] Failed to fetch page: {response.StatusCode}");
                        break;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    
                    // Extract ObjectIDs from the Results array
                    var objectIdMatches = System.Text.RegularExpressions.Regex.Matches(json, "\"ObjectID\"\\s*:\\s*\"?([0-9]+)\"?");
                    int pageCount = 0;
                    foreach (System.Text.RegularExpressions.Match match in objectIdMatches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            var objectId = match.Groups[1].Value;
                            if (!allObjectIds.Contains(objectId)) // Avoid duplicates
                            {
                                allObjectIds.Add(objectId);
                                pageCount++;
                            }
                        }
                    }

                    totalFetched += pageCount;
                    _loggingService.LogInfo($"[FETCH_CHILDREN] Page fetched: {pageCount} items (Total so far: {totalFetched})");
                    
                    // Check if there are more pages
                    // Rally returns fewer than pageSize when we've reached the end
                    hasMore = (pageCount == pageSize);
                    start += pageSize;
                    
                    // Safety check: prevent infinite loops
                    if (start > 10000)
                    {
                        _loggingService.LogWarning($"[FETCH_CHILDREN] Safety limit reached (10000 items), stopping pagination");
                        break;
                    }
                }

                _loggingService.LogInfo($"[FETCH_CHILDREN] Completed: Extracted {allObjectIds.Count} unique ObjectIDs from collection");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[FETCH_CHILDREN] Error fetching ObjectIDs from {url}: {ex.Message}", ex);
            }

            return allObjectIds;
        }

        private async Task<List<RallyWorkItem>> GetWorkItemsByTypeAsync(string itemType)
        {
            var list = new List<RallyWorkItem>();
            int pageSize = 200, start = 1; bool more = true;
            while (more)
            {
                var url = BuildQueryUrl(itemType, start, pageSize);
                var request = new HttpRequestMessage(HttpMethod.Get, url); AddAuthenticationHeader(request);
                try
                {
                    var resp = await _httpClient.SendAsync(request);
                    if (!resp.IsSuccessStatusCode) break;
                    var content = await resp.Content.ReadAsStringAsync();
                    var page = ParseRallyResponse(content, itemType);
                    list.AddRange(page);
                    more = page.Count == pageSize; start += pageSize;
                }
                catch (Exception ex) { _loggingService.LogWarning($"Error querying {itemType}: {ex.Message}"); break; }
            }
            return list;
        }

        /// <summary>
        /// Fetch all work items of a specific type from Rally
        /// Public wrapper for two-phase migration service
        /// </summary>
        public async Task<List<RallyWorkItem>> GetWorkItemsByTypeAsync(ConnectionSettings settings, string itemType)
        {
            ConfigureConnection(settings);
            return await GetWorkItemsByTypeAsync(itemType);
        }

        public async Task<List<RallyWorkItem>> GetAllWorkItemsAsync(ConnectionSettings settings, IProgress<string> progress = null)
        {
            var result = new List<RallyWorkItem>();
            ConfigureConnection(settings);
            var types = new[] { "HierarchicalRequirement", "Defect", "Task", "TestCase", "PortfolioItem/Feature", "PortfolioItem/Epic" };
            foreach (var t in types)
            {
                progress?.Report($"Fetching {t}...");
                var items = await GetWorkItemsByTypeAsync(t);
                result.AddRange(items);
            }
            await FetchAttachmentsAndCommentsAsync(result, progress);
            return result;
        }

        /// <summary>
        /// Fetch ONLY the State field for a Task using minimal fields (PowerShell-style query)
        /// Rally API has a bug where fetching many fields returns stale State values
        /// This method uses the EXACT same query as the PowerShell test script that works correctly
        /// </summary>
        public async Task<string> GetTaskStateMinimalAsync(string formattedId)
        {
            try
            {
                // Use EXACT same query as PowerShell script - minimal fields only
                var baseUrl = $"{_serverUrl}/slm/webservice/v2.0/task";
                var workspaceParam = $"workspace=/workspace/{_workspace}";
                var projectParam = !string.IsNullOrEmpty(_project) ? $"&project=/project/{_project}" : "";
                
                // MINIMAL FETCH - exactly like PowerShell script
                var fetch = "ObjectID,FormattedID,Name,State,Estimate,ToDo,Actuals";
                var url = $"{baseUrl}?{workspaceParam}{projectParam}&query=(FormattedID%20=%20\"{formattedId}\")&fetch={fetch}";
                
                _loggingService.LogInfo($"[TASK_STATE_MINIMAL] Fetching Task State using PowerShell-style minimal query");
                _loggingService.LogDebug($"   URL: {url}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthenticationHeader(request);
                var resp = await _httpClient.SendAsync(request);
                
                if (!resp.IsSuccessStatusCode)
                {
                    _loggingService.LogWarning($"[TASK_STATE_MINIMAL] Failed: {resp.StatusCode}");
                    return null;
                }
                
                var content = await resp.Content.ReadAsStringAsync();
                _loggingService.LogDebug($"[TASK_STATE_MINIMAL] Response: {content.Substring(0, Math.Min(500, content.Length))}");
                
                // Extract State field from JSON response
                var stateMatch = System.Text.RegularExpressions.Regex.Match(content, "\"State\"\\s*:\\s*\"([^\"]+)\"");
                if (stateMatch.Success)
                {
                    var state = stateMatch.Groups[1].Value;
                    _loggingService.LogInfo($"[TASK_STATE_MINIMAL] ? Fetched State='{state}' using minimal query (PowerShell-style)");
                    return state;
                }
                else
                {
                    _loggingService.LogWarning($"[TASK_STATE_MINIMAL] Could not extract State from response");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[TASK_STATE_MINIMAL] Error: {ex.Message}", ex);
                return null;
            }
        }
    }
}
