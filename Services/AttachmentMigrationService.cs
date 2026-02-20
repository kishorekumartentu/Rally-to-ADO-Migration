using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Rally_to_ADO_Migration.Models;
using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Handles migration of attachments from Rally to Azure DevOps
    /// including download, upload and reference updating
    /// </summary>
    public class AttachmentMigrationService
    {
        private readonly LoggingService _loggingService;
        private readonly HttpClient _httpClient;
        private readonly string _tempPath;

        public AttachmentMigrationService(LoggingService loggingService)
        {
            _loggingService = loggingService;
            _httpClient = new HttpClient();
            _tempPath = Path.Combine(Path.GetTempPath(), "RallyAdoMigration");
            if (!Directory.Exists(_tempPath))
                Directory.CreateDirectory(_tempPath);
        }

        /// <summary>
        /// Get list of attachments for a Rally work item
        /// Attachments are already fetched by RallyApiService during work item retrieval
        /// </summary>
        public List<RallyAttachment> GetRallyAttachments(RallyWorkItem item)
        {
            try
            {
                if (item?.Attachments == null || !item.Attachments.Any())
                    return new List<RallyAttachment>();

                _loggingService.LogInfo($"Processing {item.Attachments.Count} attachments for {item.FormattedID}");
                return item.Attachments;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to get Rally attachments for {item?.FormattedID}", ex);
                return new List<RallyAttachment>();
            }
        }

        /// <summary>
        /// Save Rally attachment to temp folder (attachment content is already in memory as byte[])
        /// </summary>
        public async Task<string> DownloadRallyAttachmentAsync(RallyAttachment attachment, ConnectionSettings settings)
        {
            try
            {
                if (attachment == null || attachment.Content == null || attachment.Content.Length == 0)
                    return null;

                // Sanitize filename to avoid path issues
                var safeFileName = Path.GetFileName(attachment.Name);
                var tempFile = Path.Combine(_tempPath, safeFileName);
                
                // Content is already in memory as byte[], just save it to temp file
                // .NET Framework 4.8 - use synchronous File.WriteAllBytes
                File.WriteAllBytes(tempFile, attachment.Content);
                
                _loggingService.LogDebug($"Saved attachment {attachment.Name} ({attachment.Content.Length} bytes) to temp file");
                
                return tempFile;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to save attachment {attachment?.Name}", ex);
                return null;
            }
        }

        /// <summary>
        /// Upload attachment to Azure DevOps and return URL
        /// </summary>
        // Update the UploadToAdoAsync method to match the required parameters for AdoApiService.UploadAttachmentAsync
        public async Task<string> UploadToAdoAsync(string filePath, AdoApiService adoApi, int workItemId, ConnectionSettings settings)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var fileName = Path.GetFileName(filePath);

                // .NET Framework 4.8 - use synchronous File.ReadAllBytes
                var fileBytes = File.ReadAllBytes(filePath);

                // Prepare the attachment object as required by the AdoApiService
                var attachment = new
                {
                    fileName = fileName,
                    bytes = fileBytes
                };

                var result = await adoApi.UploadAttachmentAsync(settings, workItemId, attachment);

                if (!result)
                    return null;

                // Add the attachment to the work item
                var fields = new Dictionary<string, object>
                {
                    { "System.AttachedFiles", new[] { fileName } }
                };
                await adoApi.PatchWorkItemFieldsAsync(settings, workItemId, fields);

                return fileName;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to upload attachment to ADO: {ex.Message}", ex);
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch { }
            }
        }

        /// <summary>
        /// Update attachment references in rich text fields
        /// </summary>
        public string UpdateAttachmentReferences(string html, Dictionary<string, string> attachmentMap)
        {
            foreach (var kvp in attachmentMap)
            {
                html = html.Replace(kvp.Key, kvp.Value);
            }
            return html;
        }

        /// <summary>
        /// Migrate all attachments for a work item
        /// </summary>
        public async Task MigrateAttachmentsAsync(
            RallyWorkItem rallyItem,
            int adoItemId,
            ConnectionSettings settings,
            AdoApiService adoApi,
            List<RallyAttachmentReference> inlineReferences = null)
        {
            try
            {
                var attachments = GetRallyAttachments(rallyItem);
                if (!attachments.Any())
                {
                    _loggingService.LogInfo($"No attachments found for {rallyItem.FormattedID}");
                    return;
                }

                _loggingService.LogInfo($"Migrating {attachments.Count} attachments for {rallyItem.FormattedID}");
                var referenceMap = new Dictionary<string, string>();

                foreach (var attachment in attachments)
                {
                    try
                    {
                        // Download from Rally
                        var tempFile = await DownloadRallyAttachmentAsync(attachment, settings);
                        if (string.IsNullOrEmpty(tempFile))
                            continue;

                        // Upload to ADO
                        var adoReference = await UploadToAdoAsync(tempFile, adoApi, adoItemId, settings);
                        if (string.IsNullOrEmpty(adoReference))
                            continue;

                        // Track reference mapping
                        var inlineRef = inlineReferences?.FirstOrDefault(r => 
                            r.FileName?.Equals(attachment.Name, StringComparison.OrdinalIgnoreCase) == true);
                        
                        if (inlineRef != null)
                        {
                            referenceMap[inlineRef.PlaceholderToken] = adoReference;
                        }

                        _loggingService.LogInfo($"Migrated attachment {attachment.Name} for {rallyItem.FormattedID}");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Failed to migrate attachment {attachment.Name}: {ex.Message}");
                    }
                }

                // Update any inline references
                if (referenceMap.Any())
                {
                    var fields = new Dictionary<string, object>();

                    // Get the work item to read current field values
                    var workItem = await adoApi.GetWorkItemByIdAsync(settings, adoItemId);
                    if (workItem != null)
                    {
                        // Update repro steps if it exists and has placeholder tokens
                        if (workItem["fields"] != null && workItem["fields"]["Microsoft.VSTS.TCM.ReproSteps"] != null)
                        {
                            var reproSteps = workItem["fields"]["Microsoft.VSTS.TCM.ReproSteps"].ToString();
                            if (!string.IsNullOrEmpty(reproSteps))
                            {
                                fields["Microsoft.VSTS.TCM.ReproSteps"] = UpdateAttachmentReferences(reproSteps, referenceMap);
                            }
                        }

                        // Update description if it exists and has placeholder tokens
                        if (workItem["fields"] != null && workItem["fields"]["System.Description"] != null)
                        {
                            var description = workItem["fields"]["System.Description"].ToString();
                            if (!string.IsNullOrEmpty(description))
                            {
                                fields["System.Description"] = UpdateAttachmentReferences(description, referenceMap);
                            }
                        }

                        if (fields.Any())
                        {
                            // Use PatchWorkItemFieldsAsync which is available in AdoApiService
                            await adoApi.PatchWorkItemFieldsAsync(settings, adoItemId, fields, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to migrate attachments for {rallyItem.FormattedID}", ex);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            try
            {
                if (Directory.Exists(_tempPath))
                    Directory.Delete(_tempPath, true);
            }
            catch { }
        }
    }
}