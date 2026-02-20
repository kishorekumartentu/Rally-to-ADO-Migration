using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Helper service to handle replacement of attachment placeholders with actual ADO URLs
    /// </summary>
    public class AttachmentPlaceholderService
    {
        private readonly AdoApiService _adoService;
        private readonly LoggingService _loggingService;

        public AttachmentPlaceholderService(AdoApiService adoService, LoggingService loggingService)
        {
            _adoService = adoService;
            _loggingService = loggingService;
        }

        /// <summary>
        /// Upload attachments and replace placeholders in description with actual ADO attachment URLs
        /// </summary>
        public async Task<bool> UploadAttachmentsAndReplacePlaceholdersAsync(
            ConnectionSettings settings,
            int adoWorkItemId,
            RallyWorkItem rallyItem,
            List<RallyAttachmentReference> attachmentReferences,
            string descriptionWithPlaceholders = null)
        {
            try
            {
                if (rallyItem?.Attachments == null || !rallyItem.Attachments.Any())
                {
                    _loggingService.LogDebug($"No attachments to upload for {rallyItem?.FormattedID}");
                    return true;
                }

                if (attachmentReferences == null || !attachmentReferences.Any())
                {
                    _loggingService.LogDebug($"No attachment references (placeholders) found for {rallyItem?.FormattedID}");
                    // Still upload attachments even if no placeholders
                    return await UploadAttachmentsWithoutPlaceholdersAsync(settings, adoWorkItemId, rallyItem.Attachments);
                }

                _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] Processing {rallyItem.Attachments.Count} attachments with {attachmentReferences.Count} placeholders for {rallyItem.FormattedID}");

                // CRITICAL FIX: Build mapping using OriginalPath (unique) instead of FileName (can have duplicates like "image.png")
                // The OriginalPath contains the Rally attachment URL which includes the unique attachment ObjectID
                var originalPathToReference = new Dictionary<string, RallyAttachmentReference>(StringComparer.OrdinalIgnoreCase);
                foreach (var attachRef in attachmentReferences)
                {
                    if (!string.IsNullOrEmpty(attachRef.OriginalPath))
                    {
                        originalPathToReference[attachRef.OriginalPath] = attachRef;
                        _loggingService.LogDebug($"[MAPPING] Placeholder {attachRef.PlaceholderToken} for path: {attachRef.OriginalPath}");
                    }
                }
                
                _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] Built mapping for {originalPathToReference.Count} unique attachment paths");

                // CRITICAL FIX: Get existing attachment URLs from ADO first
                // If attachments already exist, we need their URLs to replace placeholders
                var existingAttachmentUrls = await GetExistingAttachmentUrlsAsync(settings, adoWorkItemId);
                _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] Found {existingAttachmentUrls.Count} existing attachments in ADO");

                // Upload attachments and build placeholder -> URL mapping
                var placeholderToUrl = new Dictionary<string, string>();
                
                foreach (var attachment in rallyItem.Attachments)
                {
                    try
                    {
                        string adoAttachmentUrl = null;

                        // Check if attachment already exists in ADO
                        if (existingAttachmentUrls.TryGetValue(attachment.Name, out var existingUrl))
                        {
                            _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] Using existing attachment URL for {attachment.Name}");
                            adoAttachmentUrl = existingUrl;
                        }
                        else
                        {
                            // Upload new attachment and get ADO URL
                            adoAttachmentUrl = await _adoService.UploadAttachmentWithUrlAsync(settings, adoWorkItemId, attachment);
                            
                            if (string.IsNullOrEmpty(adoAttachmentUrl))
                            {
                                _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] Failed to upload {attachment.Name}");
                                continue;
                            }

                            _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] Uploaded {attachment.Name} -> {adoAttachmentUrl}");
                        }

                        // CRITICAL FIX: Match attachment by OriginalPath (contains ObjectID) instead of FileName
                        // Rally attachments can have duplicate filenames (e.g., "image.png") but unique ObjectIDs
                        // The OriginalPath should match the pattern: /slm/attachment/{ObjectID}/filename
                        RallyAttachmentReference matchingRef = null;
                        
                        // Try to find matching reference by ObjectID in the path
                        foreach (var attachRef in originalPathToReference.Values)
                        {
                            // OriginalPath format: /slm/attachment/833704383045/image.png
                            // Check if the attachment ObjectID matches what's in the OriginalPath
                            if (!string.IsNullOrEmpty(attachment.ObjectID) && 
                                !string.IsNullOrEmpty(attachRef.OriginalPath) &&
                                attachRef.OriginalPath.Contains(attachment.ObjectID))
                            {
                                matchingRef = attachRef;
                                _loggingService.LogDebug($"[MATCH] Found by ObjectID: {attachment.ObjectID} in path {attachRef.OriginalPath}");
                                break;
                            }
                        }
                        
                        if (matchingRef != null)
                        {
                            placeholderToUrl[matchingRef.PlaceholderToken] = adoAttachmentUrl;
                            _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] ? Mapped placeholder {matchingRef.PlaceholderToken} -> {adoAttachmentUrl}");
                        }
                        else
                        {
                            _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] Could not find placeholder for attachment {attachment.Name} (ObjectID: {attachment.ObjectID})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] Error processing {attachment.Name}: {ex.Message}");
                    }
                }

                // If we have any placeholder mappings, update the Description field
                if (placeholderToUrl.Any())
                {
                    return await ReplaceDescriptionPlaceholdersAsync(settings, adoWorkItemId, placeholderToUrl, rallyItem.FormattedID, descriptionWithPlaceholders);
                }
                else
                {
                    _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] No placeholder replacements needed for {rallyItem.FormattedID}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[ATTACHMENT_PLACEHOLDER] Failed to process attachments for {rallyItem?.FormattedID}: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Upload attachments without placeholder replacement (fallback)
        /// </summary>
        private async Task<bool> UploadAttachmentsWithoutPlaceholdersAsync(
            ConnectionSettings settings,
            int adoWorkItemId,
            List<RallyAttachment> attachments)
        {
            var successCount = 0;
            foreach (var attachment in attachments)
            {
                try
                {
                    var success = await _adoService.UploadAttachmentAsync(settings, adoWorkItemId, attachment);
                    if (success) successCount++;
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Error uploading attachment {attachment.Name}: {ex.Message}");
                }
            }

            _loggingService.LogInfo($"Uploaded {successCount}/{attachments.Count} attachments");
            return successCount > 0;
        }

        /// <summary>
        /// Get existing attachment URLs from ADO work item
        /// Returns mapping: FileName -> AttachmentURL
        /// </summary>
        private async Task<Dictionary<string, string>> GetExistingAttachmentUrlsAsync(ConnectionSettings settings, int workItemId)
        {
            var attachmentUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                var workItem = await _adoService.GetWorkItemByIdAsync(settings, workItemId);
                if (workItem == null)
                {
                    _loggingService.LogWarning($"Could not retrieve work item {workItemId} to check existing attachments");
                    return attachmentUrls;
                }

                // Check relations array for attachments
                var relations = workItem["relations"] as Newtonsoft.Json.Linq.JArray;
                if (relations == null || !relations.Any())
                {
                    _loggingService.LogDebug($"Work item {workItemId} has no relations");
                    return attachmentUrls;
                }

                // Look for AttachedFile relations and extract filename + URL
                foreach (var relation in relations)
                {
                    try
                    {
                        var rel = relation["rel"]?.ToString();
                        
                        if (string.Equals(rel, "AttachedFile", StringComparison.OrdinalIgnoreCase))
                        {
                            var url = relation["url"]?.ToString();
                            
                            if (string.IsNullOrEmpty(url))
                                continue;

                            // Extract fileName from URL
                            // URL format: https://dev.azure.com/{org}/{project}/_apis/wit/attachments/{guid}?fileName={filename}
                            var fileNameMatch = System.Text.RegularExpressions.Regex.Match(url, @"[?&]fileName=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            if (fileNameMatch.Success && fileNameMatch.Groups.Count > 1)
                            {
                                var filename = Uri.UnescapeDataString(fileNameMatch.Groups[1].Value);
                                attachmentUrls[filename] = url;
                                _loggingService.LogDebug($"[EXISTING_ATTACHMENT] {filename} -> {url}");
                            }
                        }
                    }
                    catch (Exception relEx)
                    {
                        _loggingService.LogDebug($"Error processing relation: {relEx.Message}");
                    }
                }

                _loggingService.LogInfo($"[EXISTING_ATTACHMENTS] Found {attachmentUrls.Count} attachment URLs in ADO");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error getting existing attachment URLs for work item {workItemId}: {ex.Message}");
            }

            return attachmentUrls;
        }

        /// <summary>
        /// Replace placeholders in Description field with actual ADO attachment URLs
        /// </summary>
        private async Task<bool> ReplaceDescriptionPlaceholdersAsync(
            ConnectionSettings settings,
            int adoWorkItemId,
            Dictionary<string, string> placeholderToUrl,
            string rallyFormattedId,
            string descriptionWithPlaceholders = null)
        {
            try
            {
                string description = descriptionWithPlaceholders;
                
                // If description wasn't provided, read from ADO
                if (string.IsNullOrEmpty(description))
                {
                    // Get current work item to read Description
                    var workItem = await _adoService.GetWorkItemByIdAsync(settings, adoWorkItemId);
                    if (workItem == null)
                    {
                        _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] Could not retrieve work item {adoWorkItemId}");
                        return false;
                    }

                    var descriptionField = workItem["fields"]?["System.Description"];
                    if (descriptionField == null)
                    {
                        _loggingService.LogDebug($"[ATTACHMENT_PLACEHOLDER] No Description field to update");
                        return true; // Not an error, just nothing to update
                    }

                    description = descriptionField.ToString();
                }
                
                if (string.IsNullOrEmpty(description))
                {
                    _loggingService.LogDebug($"[ATTACHMENT_PLACEHOLDER] Description is empty");
                    return true;
                }

                // Log the description for debugging
                _loggingService.LogDebug($"[ATTACHMENT_PLACEHOLDER] Current description length: {description.Length} chars");
                _loggingService.LogDebug($"[ATTACHMENT_PLACEHOLDER] Description preview (first 500): {description.Substring(0, Math.Min(500, description.Length))}");
                
                // Log placeholder tokens we're looking for
                _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] Looking for {placeholderToUrl.Count} placeholders:");
                foreach (var kvp in placeholderToUrl)
                {
                    _loggingService.LogDebug($"  - {kvp.Key}");
                }

                // Replace all placeholders with actual URLs
                var updatedDescription = description;
                var replacementCount = 0;

                foreach (var kvp in placeholderToUrl)
                {
                    if (updatedDescription.Contains(kvp.Key))
                    {
                        updatedDescription = updatedDescription.Replace(kvp.Key, kvp.Value);
                        replacementCount++;
                        _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] ? Replaced {kvp.Key} with {kvp.Value}");
                    }
                    else
                    {
                        _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] ? Placeholder {kvp.Key} NOT FOUND in description!");
                    }
                }

                if (replacementCount == 0)
                {
                    _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] No placeholders found in description for {rallyFormattedId}");
                    _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] This usually means the description was already updated without placeholders");
                    _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] Placeholders expected: {string.Join(", ", placeholderToUrl.Keys)}");
                    return true; // Not a critical error - attachments are still uploaded
                }

                // Update Description field with replaced content
                var updateFields = new Dictionary<string, object>
                {
                    ["System.Description"] = updatedDescription
                };

                var success = await _adoService.PatchWorkItemFieldsAsync(settings, adoWorkItemId, updateFields, false);
                
                if (success)
                {
                    _loggingService.LogInfo($"[ATTACHMENT_PLACEHOLDER] ? Replaced {replacementCount} placeholder(s) in Description for {rallyFormattedId}");
                }
                else
                {
                    _loggingService.LogWarning($"[ATTACHMENT_PLACEHOLDER] Failed to update Description for {rallyFormattedId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[ATTACHMENT_PLACEHOLDER] Error replacing placeholders for {rallyFormattedId}: {ex.Message}", ex);
                return false;
            }
        }
    }
}
