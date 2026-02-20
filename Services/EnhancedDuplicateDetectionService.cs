using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Enhanced duplicate detection and update service for ADO work items.
    /// Detects existing work items by Rally metadata and updates them instead of creating duplicates.
    /// </summary>
    public class EnhancedDuplicateDetectionService
    {
        private readonly LoggingService _loggingService;
        private readonly AdoApiService _adoService;

        public EnhancedDuplicateDetectionService(LoggingService loggingService, AdoApiService adoService)
        {
            _loggingService = loggingService;
            _adoService = adoService;
        }

        /// <summary>
        /// Find existing ADO work item for a Rally work item using multiple strategies
        /// Returns: (exists, adoWorkItemId, workItemData)
        /// </summary>
        public async Task<(bool exists, int adoWorkItemId, JObject workItemData)> FindExistingWorkItemAsync(
            ConnectionSettings settings,
            RallyWorkItem rallyItem)
        {
            try
            {
                // Strategy 1: Search by Rally ObjectID tag (most reliable)
                var adoIdByObjectId = await FindByRallyObjectIdAsync(settings, rallyItem.ObjectID);
                if (adoIdByObjectId > 0)
                {
                    _loggingService.LogDebug($"Found existing work item {adoIdByObjectId} by Rally ObjectID tag");
                    var workItemData = await _adoService.GetWorkItemByIdAsync(settings, adoIdByObjectId);
                    return (true, adoIdByObjectId, workItemData);
                }

                // Strategy 2: Search by Rally FormattedID tag
                var adoIdByFormattedId = await FindByRallyFormattedIdAsync(settings, rallyItem.FormattedID);
                if (adoIdByFormattedId > 0)
                {
                    _loggingService.LogDebug($"Found existing work item {adoIdByFormattedId} by Rally FormattedID tag");
                    var workItemData = await _adoService.GetWorkItemByIdAsync(settings, adoIdByFormattedId);
                    return (true, adoIdByFormattedId, workItemData);
                }

                // Strategy 3: Search by Title pattern [FormattedID]
                var adoIdByTitle = await FindByTitlePatternAsync(settings, rallyItem.FormattedID, rallyItem.Type);
                if (adoIdByTitle > 0)
                {
                    _loggingService.LogDebug($"Found existing work item {adoIdByTitle} by title pattern");
                    var workItemData = await _adoService.GetWorkItemByIdAsync(settings, adoIdByTitle);
                    return (true, adoIdByTitle, workItemData);
                }

                // Not found
                return (false, -1, null);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error finding existing work item for {rallyItem.FormattedID}: {ex.Message}");
                return (false, -1, null);
            }
        }

        /// <summary>
        /// Find ADO work item by Rally ObjectID tag
        /// </summary>
        private async Task<int> FindByRallyObjectIdAsync(ConnectionSettings settings, string rallyObjectId)
        {
            return await _adoService.FindExistingWorkItemIdByRallyTagsAsync(settings, null, rallyObjectId);
        }

        /// <summary>
        /// Find ADO work item by Rally FormattedID tag
        /// </summary>
        private async Task<int> FindByRallyFormattedIdAsync(ConnectionSettings settings, string formattedId)
        {
            return await _adoService.FindExistingWorkItemIdByRallyTagsAsync(settings, formattedId, null);
        }

        /// <summary>
        /// Find ADO work item by title pattern [FormattedID]
        /// </summary>
        private async Task<int> FindByTitlePatternAsync(ConnectionSettings settings, string formattedId, string rallyType)
        {
            try
            {
                var project = settings?.AdoProject?.Trim();
                if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(formattedId))
                    return -1;

                // Map Rally type to ADO work item type for more precise search
                var adoType = MapRallyTypeToAdoType(rallyType);
                var typeFilter = !string.IsNullOrEmpty(adoType) ? $" AND [System.WorkItemType] = '{adoType}'" : "";

                var titlePattern = $"[{formattedId}]";
                var wiql = new
                {
                    query = $"Select [System.Id] From WorkItems Where [System.TeamProject] = '{project}' AND [System.Title] CONTAINS '{titlePattern}'{typeFilter}"
                };

                using (var client = new System.Net.Http.HttpClient())
                {
                    var pat = settings?.AdoApiKey;
                    if (!string.IsNullOrEmpty(pat))
                    {
                        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                    }

                    var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                    var organization = settings?.AdoOrganization?.Trim();
                    
                    string baseApiPath;
                    if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        baseApiPath = $"{serverUrl}/{organization}/{project}";
                    else
                        baseApiPath = $"{serverUrl}/{project}";

                    var requestUrl = $"{baseApiPath}/_apis/wit/wiql?api-version=7.1";
                    var content = new System.Net.Http.StringContent(
                        Newtonsoft.Json.JsonConvert.SerializeObject(wiql),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    var resp = await client.PostAsync(requestUrl, content);
                    if (!resp.IsSuccessStatusCode)
                        return -1;

                    var respContent = await resp.Content.ReadAsStringAsync();
                    var j = JObject.Parse(respContent);
                    var workItems = j["workItems"] as JArray;

                    if (workItems != null && workItems.Count > 0)
                    {
                        return workItems[0]["id"]?.ToObject<int>() ?? -1;
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error searching by title pattern: {ex.Message}");
            }

            return -1;
        }

        /// <summary>
        /// Compare Rally work item fields with existing ADO work item and return differences
        /// </summary>
        public Dictionary<string, object> CompareAndGetDifferences(
            RallyWorkItem rallyItem,
            JObject existingAdoWorkItem,
            Dictionary<string, object> newAdoFields)
        {
            var differences = new Dictionary<string, object>();

            try
            {
                if (existingAdoWorkItem == null || newAdoFields == null)
                    return differences;

                var existingFields = existingAdoWorkItem["fields"] as JObject;
                if (existingFields == null)
                    return newAdoFields; // No existing fields, update all

                // Compare each field from Rally mapping
                foreach (var kvp in newAdoFields)
                {
                    var fieldName = kvp.Key;
                    var newValue = kvp.Value;

                    // Skip system fields that shouldn't be updated
                    if (ShouldSkipField(fieldName))
                        continue;

                    // Get existing value
                    var existingValue = existingFields[fieldName];

                    // Compare values intelligently
                    if (IsDifferent(existingValue, newValue, fieldName))
                    {
                        differences[fieldName] = newValue;
                        _loggingService.LogDebug($"Field '{fieldName}' differs: '{existingValue}' -> '{newValue}'");
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error comparing work item fields: {ex.Message}");
                return newAdoFields; // On error, update all fields
            }

            return differences;
        }

        /// <summary>
        /// Determines if a field should be skipped during updates
        /// </summary>
        private bool ShouldSkipField(string fieldName)
        {
            // System fields that shouldn't be updated
            var skipFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Id",
                "System.Rev",
                "System.CreatedBy",
                "System.CreatedDate",
                "System.ChangedBy",
                "System.ChangedDate",
                "System.AuthorizedDate",
                "System.RevisedDate",
                "System.WorkItemType", // Can't change work item type
                "System.TeamProject",
                "System.AreaId",
                "System.NodeName",
                "System.AreaLevel1",
                "System.AreaLevel2",
                "System.AreaLevel3",
                "System.AreaLevel4"
            };

            return skipFields.Contains(fieldName);
        }

        /// <summary>
        /// Intelligent comparison of field values
        /// </summary>
        private bool IsDifferent(JToken existingValue, object newValue, string fieldName)
        {
            try
            {
                // Handle null values
                if (existingValue == null || existingValue.Type == JTokenType.Null)
                    return newValue != null;

                if (newValue == null)
                    return existingValue != null && existingValue.Type != JTokenType.Null;

                // For HTML fields (Description, etc.), always update - comparison is too complex
                if (IsHtmlField(fieldName))
                    return true;

                // For tags, normalize and compare
                if (fieldName.Equals("System.Tags", StringComparison.OrdinalIgnoreCase))
                {
                    var existingTags = NormalizeTags(existingValue.ToString());
                    var newTags = NormalizeTags(newValue.ToString());
                    return existingTags != newTags;
                }

                // For dates, compare as dates
                if (IsDateField(fieldName))
                {
                    if (DateTime.TryParse(existingValue.ToString(), out var existingDate) &&
                        DateTime.TryParse(newValue.ToString(), out var newDate))
                    {
                        return existingDate.Date != newDate.Date; // Compare dates only, ignore time
                    }
                }

                // For numbers, compare as numbers
                if (IsNumberField(fieldName))
                {
                    if (double.TryParse(existingValue.ToString(), out var existingNum) &&
                        double.TryParse(newValue.ToString(), out var newNum))
                    {
                        return Math.Abs(existingNum - newNum) > 0.001; // Tolerance for floating point
                    }
                }

                // Default: string comparison (trim and case-sensitive)
                var existingStr = existingValue.ToString()?.Trim() ?? "";
                var newStr = newValue.ToString()?.Trim() ?? "";

                return !string.Equals(existingStr, newStr, StringComparison.Ordinal);
            }
            catch
            {
                // On error, assume different to trigger update
                return true;
            }
        }

        private bool IsHtmlField(string fieldName)
        {
            var htmlFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Description",
                "Microsoft.VSTS.TCM.ReproSteps",
                "Microsoft.VSTS.TCM.SystemInfo",
                "Microsoft.VSTS.TCM.Steps"
            };

            return htmlFields.Contains(fieldName);
        }

        private bool IsDateField(string fieldName)
        {
            return fieldName.IndexOf("Date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fieldName.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsNumberField(string fieldName)
        {
            var numberFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.VSTS.Scheduling.StoryPoints",
                "Microsoft.VSTS.Scheduling.OriginalEstimate",
                "Microsoft.VSTS.Scheduling.RemainingWork",
                "Microsoft.VSTS.Scheduling.CompletedWork",
                "Microsoft.VSTS.Common.Priority",
                "Microsoft.VSTS.Common.StackRank"
            };

            return numberFields.Contains(fieldName) ||
                   fieldName.IndexOf("Estimate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fieldName.IndexOf("Points", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NormalizeTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return "";

            // Split, trim, sort, and rejoin
            var tagList = tags.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(t => t.Trim())
                              .Where(t => !string.IsNullOrEmpty(t))
                              .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                              .ToList();

            return string.Join(";", tagList);
        }

        private string MapRallyTypeToAdoType(string rallyType)
        {
            if (string.IsNullOrEmpty(rallyType))
                return null;

            var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "HierarchicalRequirement", "User Story" },
                { "Defect", "Bug" },
                { "Task", "Task" },
                { "TestCase", "Test Case" },
                { "PortfolioItem/Feature", "Feature" },
                { "PortfolioItem/Epic", "Epic" },
                { "Feature", "Feature" },
                { "Epic", "Epic" }
            };

            return typeMap.TryGetValue(rallyType, out var adoType) ? adoType : null;
        }
    }
}
