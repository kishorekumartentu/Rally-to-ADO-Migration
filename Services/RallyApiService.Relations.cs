using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService
    {
        private List<string> ParseChildrenReferences(string childrenJson, string tasksJson)
        {
            var children = new List<string>();
            try
            {
                int childrenCount = 0;
                int tasksCount = 0;
                
                if (!string.IsNullOrEmpty(childrenJson))
                {
                    var childrenIds = ExtractObjectIDsFromReferenceArray(childrenJson);
                    childrenCount = childrenIds.Count;
                    children.AddRange(childrenIds);
                    _loggingService.LogInfo($"[CHILDREN] Extracted {childrenCount} children ObjectIDs from Children field");
                }
                
                if (!string.IsNullOrEmpty(tasksJson))
                {
                    var tasksIds = ExtractObjectIDsFromReferenceArray(tasksJson);
                    tasksCount = tasksIds.Count;
                    children.AddRange(tasksIds);
                    _loggingService.LogInfo($"[CHILDREN] Extracted {tasksCount} task ObjectIDs from Tasks field");
                }
                
                var distinctChildren = children.Distinct().ToList();
                var duplicates = children.Count - distinctChildren.Count;
                
                if (duplicates > 0)
                {
                    _loggingService.LogWarning($"[CHILDREN] Found {duplicates} duplicate ObjectIDs between Children and Tasks (removed duplicates)");
                }
                
                _loggingService.LogInfo($"[CHILDREN] Total unique children: {distinctChildren.Count} (Children: {childrenCount}, Tasks: {tasksCount}, Duplicates removed: {duplicates})");
                
                return distinctChildren;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"ParseChildrenReferences error: {ex.Message}", ex);
                return children.Distinct().ToList();
            }
        }

        private List<string> ExtractObjectIDsFromReferenceArray(string jsonArray)
        {
            var objectIds = new List<string>();
            if (string.IsNullOrWhiteSpace(jsonArray) || jsonArray.Trim() == "[]")
            {
                _loggingService.LogDebug($"[EXTRACT] JSON array is empty or null");
                return objectIds;
            }
            
            try
            {
                _loggingService.LogDebug($"[EXTRACT] Parsing JSON array of length: {jsonArray.Length} chars");
                _loggingService.LogDebug($"[EXTRACT] JSON preview: {jsonArray.Substring(0, Math.Min(500, jsonArray.Length))}");
                
                // Method 1: Extract ObjectID field
                var objectIdPattern = "\"ObjectID\"\\s*:\\s*\"([0-9]+)\"";
                var objectIdMatches = Regex.Matches(jsonArray, objectIdPattern);
                foreach (Match m in objectIdMatches)
                {
                    if (m.Groups.Count > 1)
                    {
                        var id = m.Groups[1].Value;
                        objectIds.Add(id);
                        _loggingService.LogDebug($"[EXTRACT] Found ObjectID: {id}");
                    }
                }
                
                // Method 2: Extract from _ref URL as fallback
                var refPattern = "\"_ref\"\\s*:\\s*\"[^\"]*/([0-9]+)\"";
                var refMatches = Regex.Matches(jsonArray, refPattern);
                int refFoundCount = 0;
                foreach (Match m in refMatches)
                {
                    if (m.Groups.Count > 1)
                    {
                        var id = m.Groups[1].Value;
                        if (!objectIds.Contains(id))
                        {
                            objectIds.Add(id);
                            refFoundCount++;
                            _loggingService.LogDebug($"[EXTRACT] Found ObjectID from _ref: {id}");
                        }
                    }
                }
                
                _loggingService.LogInfo($"[EXTRACT] Extracted {objectIds.Count} ObjectIDs total (ObjectID field: {objectIdMatches.Count}, _ref: {refFoundCount})");
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"ExtractObjectIDsFromReferenceArray error: {ex.Message}", ex);
            }
            
            return objectIds;
        }
    }
}
