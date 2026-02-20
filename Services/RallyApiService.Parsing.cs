using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService
    {
        private List<RallyWorkItem> ParseRallyResponse(string json, string itemType)
        {
            var items = new List<RallyWorkItem>();
            try
            {
                var resultsStart = json.IndexOf("\"Results\":");
                if (resultsStart > -1)
                {
                    var arrayStart = json.IndexOf('[', resultsStart);
                    var arrayEnd = FindMatchingBracket(json, arrayStart);
                    if (arrayStart > -1 && arrayEnd > -1)
                    {
                        var array = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                        var objects = SplitJsonObjects(array);
                        foreach (var obj in objects)
                        {
                            var wi = ParseSingleWorkItem(obj, itemType);
                            if (wi != null) items.Add(wi);
                        }
                    }
                }
            }
            catch (Exception ex) { _loggingService.LogWarning($"ParseRallyResponse error: {ex.Message}"); }
            return items;
        }

        private RallyWorkItem ParseSingleWorkItem(string json, string itemType)
        {
            try
            {
                var item = new RallyWorkItem();
                var rawNames = new List<string>();
                var nameMatches = Regex.Matches(json, "\\\"([A-Za-z0-9_]+)\\\"\\s*:");
                foreach (Match m in nameMatches)
                {
                    if (m.Groups.Count > 1)
                    {
                        var key = m.Groups[1].Value;
                        if (!rawNames.Contains(key)) rawNames.Add(key);
                    }
                }
                item.RawFieldNames = rawNames;
                var standard = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ObjectID","FormattedID","Name","Description","Notes","State","Owner","Project","Priority","Severity","Children","Tasks","CreationDate","LastUpdateDate","PlanEstimate","TaskEstimateTotal","TaskRemainingTotal","Parent","Blocked","Ready","AcceptanceCriteria","c_AcceptanceCriteria","c_TestNotes","Iteration","Release","Tags","Feature","PortfolioItem"
                };
                item.ObjectID = ExtractJsonValue(json, "ObjectID");
                item.FormattedID = ExtractJsonValue(json, "FormattedID");
                item.Name = ExtractJsonValue(json, "Name");
                item.Description = ExtractJsonValue(json, "Description");
                item.Notes = ExtractJsonValue(json, "Notes");
                var rawStateField = ExtractJsonValue(json, "State");
                item.State = rawStateField;
                
                // FIXED: Rally API Bug Workaround
                // Rally has a bug where fetching ScheduleState/TaskStatus together with State field
                // causes Rally to return STALE State field values for Tasks
                // 
                // SOLUTION: 
                // - For User Stories: Use ScheduleState (their primary workflow field)
                // - For Tasks: ONLY use State field, IGNORE ScheduleState/TaskStatus completely
                // - For Defects: Use State field
                var scheduleState = ExtractJsonValue(json, "ScheduleState");
                
                // Log state fields for debugging
                _loggingService.LogDebug($"[STATE_FIELDS] {item.FormattedID} (Type:{itemType}): State='{rawStateField}', ScheduleState='{scheduleState}'");
                
                // Use ScheduleState for User Stories and Defects (primary workflow field)
                if ((itemType == "HierarchicalRequirement" || itemType == "Defect") && !string.IsNullOrEmpty(scheduleState))
                {
                    item.State = scheduleState;  // Override State with ScheduleState for User Stories and Defects
                    var workItemTypeName = itemType == "HierarchicalRequirement" ? "User Story" : "Defect";
                    _loggingService.LogInfo($"[STATE] {workItemTypeName} {item.FormattedID}: Using ScheduleState='{scheduleState}' (State field='{rawStateField}' ignored)");
                }
                // For Tasks and other types: Use State field ONLY
                else
                {
                    item.State = rawStateField;
                    if (itemType == "Task")
                    {
                        _loggingService.LogInfo($"[STATE] Task {item.FormattedID}: Using State='{rawStateField}' (primary field, ScheduleState ignored)");
                    }
                    else
                    {
                        _loggingService.LogInfo($"[STATE] {itemType} {item.FormattedID}: Using State='{item.State}'");
                    }
                }
                
                // Debug: Log final State value for Tasks and Defects
                if (itemType == "Task" || itemType == "Defect" || item.FormattedID?.StartsWith("TA") == true || item.FormattedID?.StartsWith("DE") == true)
                {
                    _loggingService.LogInfo($"[FINAL_STATE] {item.FormattedID} (Type:{itemType}) => Will use State='{item.State}' for migration");
                }
                
                item.Type = itemType;
                
                // Extract Owner with nested fields (EmailAddress, Email, DisplayName)
                var ownerJson = ExtractJsonValue(json, "Owner");
                _loggingService.LogInfo($"[OWNER] OWNER EXTRACTION DEBUG for {item.FormattedID}:");
                _loggingService.LogInfo($"   Raw Owner JSON: {(ownerJson ?? "NULL").Substring(0, Math.Min(200, (ownerJson ?? "NULL").Length))}");
                
                if (!string.IsNullOrEmpty(ownerJson) && ownerJson != "null")
                {
                    // Try to extract EmailAddress field first
                    var emailAddress = ExtractJsonValue(ownerJson, "EmailAddress");
                    _loggingService.LogInfo($"   EmailAddress field: '{emailAddress ?? "NULL"}'");
                    
                    if (string.IsNullOrEmpty(emailAddress))
                    {
                        // Fallback to Email field
                        emailAddress = ExtractJsonValue(ownerJson, "Email");
                        _loggingService.LogInfo($"   Email field: '{emailAddress ?? "NULL"}'");
                    }
                    
                    if (!string.IsNullOrEmpty(emailAddress))
                    {
                        item.Owner = emailAddress;
                        _loggingService.LogInfo($"   [SUCCESS] Extracted Owner email '{emailAddress}' for {item.FormattedID}");
                    }
                    else
                    {
                        // No email in work item response - try to fetch from Rally User API using _ref
                        var displayName = ExtractJsonValue(ownerJson, "DisplayName");
                        if (string.IsNullOrEmpty(displayName))
                        {
                            displayName = ExtractJsonValue(ownerJson, "_refObjectName");
                        }
                        
                        _loggingService.LogInfo($"   [WARNING] No email in Owner object, DisplayName: '{displayName}'");
                        
                        // Try cache first (fast)
                        var cachedEmail = GetCachedEmailByDisplayName(displayName);
                        if (!string.IsNullOrEmpty(cachedEmail))
                        {
                            item.Owner = cachedEmail;
                            _loggingService.LogInfo($"   [CACHE] Using cached email: '{cachedEmail}'");
                        }
                        else
                        {
                            // Extract _ref URL and fetch user email from Rally User API
                            var userRefUrl = ExtractJsonValue(ownerJson, "_ref");
                            if (!string.IsNullOrEmpty(userRefUrl))
                            {
                                _loggingService.LogInfo($"   [API] Fetching email from Rally User API: {userRefUrl}");
                                // Note: This is synchronous parsing, but we need async call
                                // Store the ref URL in Owner for now, will be enriched later
                                item.Owner = displayName; // Use display name as fallback
                                item.CustomFields["_OwnerRef"] = userRefUrl; // Store ref for async enrichment
                            }
                            else
                            {
                                item.Owner = displayName;
                                _loggingService.LogWarning($"   [FALLBACK] No _ref URL found for Owner, using DisplayName only");
                            }
                        }
                    }
                }
                else
                {
                    _loggingService.LogInfo($"   [INFO] Owner field is NULL or empty for {item.FormattedID}");
                }
                
                item.Project = ExtractJsonValue(json, "Project");
                item.Priority = ExtractJsonValue(json, "Priority");
                item.Severity = ExtractJsonValue(json, "Severity");
                
                // Extract Parent ObjectID from the Parent reference object
                // For User Stories (HierarchicalRequirement), the parent Feature is in "PortfolioItem" field
                // For Tasks, Rally has BOTH "Parent" (WorkProduct) and "PortfolioItem" fields
                // Tasks should link to their direct parent (usually a User Story via WorkProduct field)
                var parentJson = ExtractJsonValue(json, "Parent");
                var workProductJson = ExtractJsonValue(json, "WorkProduct");
                var portfolioItemJson = ExtractJsonValue(json, "PortfolioItem");
                
                _loggingService.LogDebug($"Parent extraction for {item.FormattedID} (Type:{itemType})");
                
                // Try WorkProduct first (for Tasks - this links to User Story)
                string parentObjectId = null;
                if (!string.IsNullOrEmpty(workProductJson) && workProductJson != "null")
                {
                    parentObjectId = ExtractJsonValue(workProductJson, "ObjectID");
                    if (!string.IsNullOrEmpty(parentObjectId))
                    {
                        item.Parent = parentObjectId;
                _loggingService.LogInfo($"[SUCCESS] Extracted Parent ObjectID '{parentObjectId}' from WorkProduct for {item.FormattedID}");
                    }
                }
                
                // Try Parent field if WorkProduct was empty (for Defects)
                if (string.IsNullOrEmpty(parentObjectId) && !string.IsNullOrEmpty(parentJson) && parentJson != "null")
                {
                    parentObjectId = ExtractJsonValue(parentJson, "ObjectID");
                    if (!string.IsNullOrEmpty(parentObjectId))
                    {
                        item.Parent = parentObjectId;
                        _loggingService.LogInfo($"[SUCCESS] Extracted Parent ObjectID '{parentObjectId}' from Parent field for {item.FormattedID}");
                    }
                }
                
                // Try PortfolioItem if still empty (for User Stories)
                if (string.IsNullOrEmpty(parentObjectId) && !string.IsNullOrEmpty(portfolioItemJson) && portfolioItemJson != "null")
                {
                    parentObjectId = ExtractJsonValue(portfolioItemJson, "ObjectID");
                    if (!string.IsNullOrEmpty(parentObjectId))
                    {
                        item.Parent = parentObjectId;
                        _loggingService.LogInfo($"[SUCCESS] Extracted Parent ObjectID '{parentObjectId}' from PortfolioItem for {item.FormattedID}");
                    }
                }
                
                if (string.IsNullOrEmpty(parentObjectId))
                {
                    _loggingService.LogDebug($"[INFO] No parent found for {item.FormattedID}");
                }
                
                var blockedStr = ExtractJsonValue(json, "Blocked");
                if (bool.TryParse(blockedStr, out var blocked)) item.Blocked = blocked;
                var readyStr = ExtractJsonValue(json, "Ready");
                if (bool.TryParse(readyStr, out var ready)) item.Ready = ready;
                var childrenJson = ExtractJsonValue(json, "Children");
                var tasksJson = ExtractJsonValue(json, "Tasks");
                
                // Debug logging to see what Rally returns
                if (!string.IsNullOrEmpty(childrenJson))
                    _loggingService.LogDebug($"Children JSON for {item.FormattedID}: {childrenJson.Substring(0, Math.Min(200, childrenJson.Length))}");
                if (!string.IsNullOrEmpty(tasksJson))
                    _loggingService.LogDebug($"Tasks JSON for {item.FormattedID}: {tasksJson.Substring(0, Math.Min(200, tasksJson.Length))}");
                
                item.Children = ParseChildrenReferences(childrenJson, tasksJson);
                
                if (item.Children.Any())
                    _loggingService.LogDebug($"Parsed {item.Children.Count} children for {item.FormattedID}: {string.Join(", ", item.Children)}");
                var creationDateStr = ExtractJsonValue(json, "CreationDate");
                if (!string.IsNullOrEmpty(creationDateStr) && DateTime.TryParse(creationDateStr, out DateTime creationDate)) item.CreationDate = creationDate;
                var updateDateStr = ExtractJsonValue(json, "LastUpdateDate");
                if (!string.IsNullOrEmpty(updateDateStr) && DateTime.TryParse(updateDateStr, out DateTime updateDate)) item.LastUpdateDate = updateDate;
                
                // DEBUG: Extract and log ALL estimate-related fields
                var planEstimateStr = ExtractJsonValue(json, "PlanEstimate");
                var taskEstStr = ExtractJsonValue(json, "TaskEstimateTotal");
                var taskRemStr = ExtractJsonValue(json, "TaskRemainingTotal");
                
                // Task-specific time tracking fields (in hours)
                var estimateStr = ExtractJsonValue(json, "Estimate");
                var toDoStr = ExtractJsonValue(json, "ToDo");
                var actualsStr = ExtractJsonValue(json, "Actuals");
                
                _loggingService.LogInfo($"[ESTIMATE] ESTIMATE FIELDS DEBUG for {item.FormattedID}:");
                _loggingService.LogInfo($"   [FIELD] PlanEstimate: '{planEstimateStr}'");
                _loggingService.LogInfo($"   [FIELD] TaskEstimateTotal: '{taskEstStr}'");
                _loggingService.LogInfo($"   [FIELD] TaskRemainingTotal: '{taskRemStr}'");
                
                // Log task-specific time tracking fields
                if (itemType == "Task" || item.FormattedID?.StartsWith("TA") == true)
                {
                    _loggingService.LogInfo($"   [TASK_TIME] Estimate (hours): '{estimateStr}'");
                    _loggingService.LogInfo($"   [TASK_TIME] ToDo (hours): '{toDoStr}'");
                    _loggingService.LogInfo($"   [TASK_TIME] Actuals (hours): '{actualsStr}'");
                }
                
                // Parse PlanEstimate
                if (!string.IsNullOrEmpty(planEstimateStr) && double.TryParse(planEstimateStr, out double planEstimate))
                {
                    item.PlanEstimate = planEstimate;
                    _loggingService.LogInfo($"[SET] Set PlanEstimate = {planEstimate}");
                }
                else if (!string.IsNullOrEmpty(planEstimateStr))
                {
                    _loggingService.LogWarning($"[PARSE_ERROR] Failed to parse PlanEstimate: '{planEstimateStr}'");
                }
                
                // Parse TaskEstimateTotal
                if (!string.IsNullOrEmpty(taskEstStr) && double.TryParse(taskEstStr, out double taskEst))
                {
                    item.TaskEstimateTotal = taskEst;
                    _loggingService.LogDebug($"Set TaskEstimateTotal = {taskEst}");
                }
                
                // Parse TaskRemainingTotal
                if (!string.IsNullOrEmpty(taskRemStr) && double.TryParse(taskRemStr, out double taskRem))
                {
                    item.TaskRemainingTotal = taskRem;
                    _loggingService.LogDebug($"Set TaskRemainingTotal = {taskRem}");
                }
                
                // Parse Task-specific time tracking fields (in hours)
                if (!string.IsNullOrEmpty(estimateStr) && double.TryParse(estimateStr, out double estimate))
                {
                    item.Estimate = estimate;
                    _loggingService.LogInfo($"[SET] Set Estimate (hours) = {estimate}");
                }
                
                if (!string.IsNullOrEmpty(toDoStr) && double.TryParse(toDoStr, out double toDo))
                {
                    item.ToDo = toDo;
                    _loggingService.LogInfo($"[SET] Set ToDo (hours) = {toDo}");
                }
                
                if (!string.IsNullOrEmpty(actualsStr) && double.TryParse(actualsStr, out double actuals))
                {
                    item.Actuals = actuals;
                    _loggingService.LogInfo($"[SET] Set Actuals (hours) = {actuals}");
                }
                
                var ac1 = ExtractJsonValue(json, "AcceptanceCriteria");
                var ac2 = ExtractJsonValue(json, "c_AcceptanceCriteria");
                item.AcceptanceCriteria = !string.IsNullOrEmpty(ac1) ? ac1 : (!string.IsNullOrEmpty(ac2) ? ac2 : null);
                
                // Extract Release field (name from reference object)
                var releaseJson = ExtractJsonValue(json, "Release");
                if (!string.IsNullOrEmpty(releaseJson) && releaseJson != "null")
                {
                    var releaseName = ExtractJsonValue(releaseJson, "_refObjectName");
                    if (!string.IsNullOrEmpty(releaseName))
                    {
                        item.Release = releaseName;
                        _loggingService.LogInfo($"[SUCCESS] Extracted Release '{releaseName}' for {item.FormattedID}");
                    }
                    else
                    {
                        _loggingService.LogDebug($"[WARNING] Release field exists but _refObjectName is empty for {item.FormattedID}");
                    }
                }
                else
                {
                    _loggingService.LogDebug($"[INFO] No Release field found for {item.FormattedID} (will show as 'Unscheduled')");
                }
                
                // Extract PreConditions field (Test Case specific field)
                if (string.Equals(itemType, "TestCase", StringComparison.OrdinalIgnoreCase))
                {
                    var preConditions = ExtractJsonValue(json, "PreConditions");
                    if (!string.IsNullOrEmpty(preConditions) && preConditions != "null")
                    {
                        item.PreConditions = preConditions;
                        _loggingService.LogInfo($"[PRECONDITIONS] Extracted for Test Case {item.FormattedID}: {preConditions.Substring(0, Math.Min(50, preConditions.Length))}...");
                    }
                    else
                    {
                        _loggingService.LogDebug($"No PreConditions found for Test Case {item.FormattedID}");
                    }
                }
                
                // Parse TestCase-specific fields if this is a TestCase
                if (itemType == "TestCase")
                {
                    ParseTestCaseFields(item, json);
                }
                
                foreach (var fieldName in rawNames)
                {
                    if (standard.Contains(fieldName)) continue;
                    if (item.CustomFields.ContainsKey(fieldName)) continue;
                    var rawVal = ExtractJsonValue(json, fieldName);
                    if (string.IsNullOrEmpty(rawVal)) continue;
                    if ((rawVal.StartsWith("{") && rawVal.Length > 4000) || (rawVal.StartsWith("[") && rawVal.Length > 4000)) continue;
                    item.CustomFields[fieldName] = rawVal;
                }
                return item;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Failed to parse work item: {ex.Message}");
                return null;
            }
        }

        private List<RallyAttachment> ParseAttachmentsResponse(string json)
        {
            var list = new List<RallyAttachment>();
            try
            {
                var resultsStart = json.IndexOf("\"Results\":");
                if (resultsStart > -1)
                {
                    var arrayStart = json.IndexOf('[', resultsStart);
                    var arrayEnd = FindMatchingBracket(json, arrayStart);
                    if (arrayStart > -1 && arrayEnd > -1)
                    {
                        var array = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                        var objs = SplitJsonObjects(array);
                        foreach (var o in objs)
                        {
                            var a = ParseSingleAttachment(o);
                            if (a != null) list.Add(a);
                        }
                    }
                }
            }
            catch (Exception ex) { _loggingService.LogWarning($"ParseAttachments error: {ex.Message}"); }
            return list;
        }

        private RallyAttachment ParseSingleAttachment(string json)
        {
            try
            {
                var a = new RallyAttachment();
                a.ObjectID = ExtractJsonValue(json, "ObjectID");
                a.Name = ExtractJsonValue(json, "Name");
                a.Description = ExtractJsonValue(json, "Description");
                a.ContentType = ExtractJsonValue(json, "ContentType");
                a.User = ExtractJsonValue(json, "User");
                var sizeStr = ExtractJsonValue(json, "Size");
                if (long.TryParse(sizeStr, out long size)) a.Size = size;
                var creationDateStr = ExtractJsonValue(json, "CreationDate");
                if (DateTime.TryParse(creationDateStr, out DateTime cd)) a.CreationDate = cd;
                a.Content = new byte[0];
                return a;
            }
            catch (Exception ex) { _loggingService.LogWarning($"ParseSingleAttachment error: {ex.Message}"); return null; }
        }

        private List<RallyComment> ParseCommentsResponse(string json)
        {
            var list = new List<RallyComment>();
            try
            {
                var resultsStart = json.IndexOf("\"Results\":");
                if (resultsStart > -1)
                {
                    var arrayStart = json.IndexOf('[', resultsStart);
                    var arrayEnd = FindMatchingBracket(json, arrayStart);
                    if (arrayStart > -1 && arrayEnd > -1)
                    {
                        var array = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                        var objs = SplitJsonObjects(array);
                        foreach (var o in objs)
                        {
                            var c = ParseSingleComment(o);
                            if (c != null) list.Add(c);
                        }
                    }
                }
            }
            catch (Exception ex) { _loggingService.LogWarning($"ParseComments error: {ex.Message}"); }
            return list;
        }

        private RallyComment ParseSingleComment(string json)
        {
            try
            {
                var c = new RallyComment();
                c.ObjectID = ExtractJsonValue(json, "ObjectID");
                c.Text = ExtractJsonValue(json, "Text");
                c.User = ExtractJsonValue(json, "User");
                var creationDateStr = ExtractJsonValue(json, "CreationDate");
                if (DateTime.TryParse(creationDateStr, out DateTime cd)) c.CreationDate = cd;
                return c;
            }
            catch (Exception ex) { _loggingService.LogWarning($"ParseSingleComment error: {ex.Message}"); return null; }
        }

        // Helpers
        private int FindMatchingBracket(string json, int startIndex)
        {
            int bracketCount = 0; bool inString = false;
            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
                if (!inString)
                {
                    if (c == '[') bracketCount++; else if (c == ']') bracketCount--;
                    if (bracketCount == 0) return i;
                }
            }
            return -1;
        }

        private List<string> SplitJsonObjects(string json)
        {
            var objs = new List<string>();
            int braceCount = 0; int startIndex = -1; bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
                if (!inString)
                {
                    if (c == '{') { if (braceCount == 0) startIndex = i; braceCount++; }
                    else if (c == '}') { braceCount--; if (braceCount == 0 && startIndex >= 0) { objs.Add(json.Substring(startIndex, i - startIndex + 1)); startIndex = -1; } }
                }
            }
            return objs;
        }

        private string ExtractJsonValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return string.Empty;
            try
            {
                var searchPattern = $"\"{key}\"\\s*:\\s*";
                var match = Regex.Match(json, searchPattern);
                if (!match.Success) return string.Empty;
                var startIndex = match.Index + match.Length;
                if (startIndex >= json.Length) return string.Empty;
                var valueChar = json[startIndex];
                if (valueChar == '"')
                {
                    startIndex++; var endIndex = startIndex;
                    while (endIndex < json.Length)
                    {
                        if (json[endIndex] == '"' && (endIndex == startIndex || json[endIndex - 1] != '\\')) break;
                        endIndex++;
                    }
                    if (endIndex < json.Length)
                    {
                        var value = json.Substring(startIndex, endIndex - startIndex);
                        return UnescapeJsonString(value);
                    }
                }
                else if (valueChar == '{' || valueChar == '[')
                {
                    char endChar = valueChar == '{' ? '}' : ']'; int depth = 1; int endIndex = startIndex + 1; bool inStr = false;
                    while (endIndex < json.Length && depth > 0)
                    {
                        var c = json[endIndex];
                        if (c == '"' && (endIndex == 0 || json[endIndex - 1] != '\\')) inStr = !inStr;
                        if (!inStr)
                        {
                            if (c == valueChar) depth++; else if (c == endChar) depth--;
                        }
                        endIndex++;
                    }
                    if (depth == 0) return json.Substring(startIndex, endIndex - startIndex);
                }
                else
                {
                    int endIndex = startIndex;
                    while (endIndex < json.Length && json[endIndex] != ',' && json[endIndex] != '}' && json[endIndex] != ']' && json[endIndex] != '\n' && json[endIndex] != '\r') endIndex++;
                    if (endIndex > startIndex)
                    {
                        var value = json.Substring(startIndex, endIndex - startIndex).Trim();
                        return value.Replace("null", "");
                    }
                }
            }
            catch (Exception ex) { _loggingService.LogWarning($"ExtractJsonValue error '{key}': {ex.Message}"); }
            return string.Empty;
        }

        private string UnescapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\b", "\b").Replace("\\f", "\f");
        }

        /// <summary>
        /// Normalizes TaskStatus values from Rally API format to standard format
        /// Examples: IN_PROGRESS -> In-Progress, COMPLETED -> Completed, DEFINED -> Defined
        /// </summary>
        private string NormalizeTaskStatus(string taskStatus)
        {
            if (string.IsNullOrEmpty(taskStatus))
                return taskStatus;

            // Replace underscores with hyphens: IN_PROGRESS -> IN-PROGRESS
            var normalized = taskStatus.Replace("_", "-");
            
            // Capitalize first letter of each word (split by hyphen)
            var words = normalized.Split('-');
            var capitalizedWords = new List<string>();
            
            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(word))
                    continue;
                    
                var lower = word.ToLower();
                var capitalized = char.ToUpper(lower[0]) + (lower.Length > 1 ? lower.Substring(1) : "");
                capitalizedWords.Add(capitalized);
            }
            
            return string.Join("-", capitalizedWords);
        }
    }
}
