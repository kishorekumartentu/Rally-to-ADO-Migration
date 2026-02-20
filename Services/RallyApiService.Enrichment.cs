using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService
    {
        private async Task FetchAttachmentsAndCommentsAsync(List<RallyWorkItem> workItems, IProgress<string> progress = null)
        {
            int count = 0;
            foreach (var wi in workItems)
            {
                count++;
                if (count % 10 == 0) progress?.Report($"Attachment/comment/user pass {count}/{workItems.Count}");
                await FetchAttachmentsForWorkItemAsync(wi);
                await FetchCommentsForWorkItemAsync(wi);
                await EnrichOwnerEmailAsync(wi); // NEW: Fetch owner email if missing
            }
        }
        
        
        /// <summary>
        /// Enrich work item with Owner email from Rally User API if not already present
        /// PUBLIC: Can be called from TwoPhaseHierarchicalMigrationService for bulk enrichment
        /// </summary>
        public async Task EnrichOwnerEmailAsync(RallyWorkItem workItem)
        {
            try
            {
                // Check if Owner is an email already
                if (!string.IsNullOrEmpty(workItem.Owner) && workItem.Owner.Contains("@"))
                {
                    _loggingService.LogDebug($"Owner already has email for {workItem.FormattedID}: {workItem.Owner}");
                    return; // Already has email
                }
                
                // Check if we stored the _OwnerRef URL during parsing
                if (workItem.CustomFields.TryGetValue("_OwnerRef", out var ownerRefObj) && ownerRefObj != null)
                {
                    var ownerRefUrl = ownerRefObj.ToString();
                    _loggingService.LogInfo($"Enriching Owner email for {workItem.FormattedID} using ref: {ownerRefUrl}");
                    
                    var email = await FetchUserEmailByRefAsync(ownerRefUrl);
                    if (!string.IsNullOrEmpty(email))
                    {
                        workItem.Owner = email; // Replace display name with email
                        _loggingService.LogInfo($"? Enriched Owner email: {email} for {workItem.FormattedID}");
                    }
                    else
                    {
                        _loggingService.LogWarning($"Could not fetch email for Owner of {workItem.FormattedID}");
                    }
                    
                    // Remove the temporary _OwnerRef field
                    workItem.CustomFields.Remove("_OwnerRef");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"EnrichOwnerEmailAsync failed for {workItem.FormattedID}: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch attachments for a work item from Rally
        /// PUBLIC: Can be called from migration services to fetch attachments for items
        /// </summary>
        public async Task FetchAttachmentsForWorkItemAsync(RallyWorkItem workItem)
        {
            try
            {
                // Fetch attachment metadata including Content._ref
                var url = $"{_serverUrl}/slm/webservice/v2.0/attachment?workspace=/workspace/{_workspace}&query=(Artifact.ObjectID%20=%20{workItem.ObjectID})&fetch=ObjectID,Name,Description,ContentType,Size,Content,CreationDate,User";
                var req = new HttpRequestMessage(HttpMethod.Get, url); 
                AddAuthenticationHeader(req);
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return;
                
                var content = await resp.Content.ReadAsStringAsync();
                workItem.Attachments = await ParseAndDownloadAttachmentsAsync(content);
                
                _loggingService.LogDebug($"Fetched {workItem.Attachments.Count} attachments for {workItem.FormattedID}");
            }
            catch (Exception ex) 
            { 
                _loggingService.LogWarning($"Attachment fetch failed {workItem.FormattedID}: {ex.Message}"); 
            }
        }

        /// <summary>
        /// Parse attachments from Rally API response and download their content
        /// </summary>
        private async Task<List<RallyAttachment>> ParseAndDownloadAttachmentsAsync(string json)
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
                            var attachment = ParseSingleAttachment(o);
                            if (attachment != null)
                            {
                                // Extract Content._ref URL
                                var contentRefUrl = ExtractContentRefUrl(o);
                                if (!string.IsNullOrEmpty(contentRefUrl))
                                {
                                    // Download actual file content
                                    attachment.Content = await DownloadAttachmentContentAsync(contentRefUrl);
                                    
                                    if (attachment.Content == null || attachment.Content.Length == 0)
                                    {
                                        _loggingService.LogWarning($"Failed to download content for attachment: {attachment.Name}");
                                    }
                                    else
                                    {
                                        _loggingService.LogDebug($"Downloaded {attachment.Name} ({attachment.Content.Length} bytes)");
                                    }
                                }
                                
                                list.Add(attachment);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) 
            { 
                _loggingService.LogWarning($"ParseAndDownloadAttachments error: {ex.Message}"); 
            }
            return list;
        }

        /// <summary>
        /// Extract Content._ref URL from attachment JSON
        /// </summary>
        private string ExtractContentRefUrl(string json)
        {
            try
            {
                // Look for "Content":{"_ref":"https://..."}
                var contentMatch = Regex.Match(json, "\"Content\"\\s*:\\s*\\{[^}]*\"_ref\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (contentMatch.Success && contentMatch.Groups.Count > 1)
                {
                    return contentMatch.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"ExtractContentRefUrl error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Download attachment content from Rally Content API
        /// Rally returns JSON with Base64-encoded content in "Content" field
        /// </summary>
        private async Task<byte[]> DownloadAttachmentContentAsync(string contentRefUrl)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, contentRefUrl);
                AddAuthenticationHeader(req);
                
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    _loggingService.LogWarning($"Failed to download attachment content: {resp.StatusCode}");
                    return new byte[0];
                }
                
                // Rally returns JSON with Base64-encoded content
                var jsonResponse = await resp.Content.ReadAsStringAsync();
                
                // Extract the Base64 content from JSON response
                // Format: {"AttachmentContent": {..., "Content": "base64string..."}}
                var contentMatch = Regex.Match(jsonResponse, "\"Content\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (contentMatch.Success && contentMatch.Groups.Count > 1)
                {
                    var base64Content = contentMatch.Groups[1].Value;
                    
                    // Decode Base64 to bytes
                    try
                    {
                        return Convert.FromBase64String(base64Content);
                    }
                    catch (FormatException ex)
                    {
                        _loggingService.LogWarning($"Failed to decode Base64 content: {ex.Message}");
                        return new byte[0];
                    }
                }
                else
                {
                    _loggingService.LogWarning("Could not extract Content field from Rally AttachmentContent response");
                    return new byte[0];
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"DownloadAttachmentContent error: {ex.Message}");
                return new byte[0];
            }
        }

        /// <summary>
        /// Fetch comments for a work item from Rally  
        /// PUBLIC: Can be called from migration services to fetch comments for items
        /// </summary>
        public async Task FetchCommentsForWorkItemAsync(RallyWorkItem workItem)
        {
            try
            {
                var url = $"{_serverUrl}/slm/webservice/v2.0/conversationpost?workspace=/workspace/{_workspace}&query=(Artifact.ObjectID%20=%20{workItem.ObjectID})&fetch=Text,User,CreationDate";
                var req = new HttpRequestMessage(HttpMethod.Get, url); AddAuthenticationHeader(req);
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return;
                var content = await resp.Content.ReadAsStringAsync();
                workItem.Comments = ParseCommentsResponse(content);
            }
            catch (Exception ex) { _loggingService.LogWarning($"Comments fetch failed {workItem.FormattedID}: {ex.Message}"); }
        }
    }
}
