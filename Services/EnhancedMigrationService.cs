using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Simple enhanced migration service that preserves Rally content properly
    /// Use this instead of the regular MigrationService for better field mapping
    /// </summary>
    public class EnhancedMigrationService : IDisposable
    {
        private readonly LoggingService _loggingService;
        private readonly string _organizationUrl;
        private readonly string _projectName;
        private readonly string _personalAccessToken;
        private readonly Action<string, Color> _uiLogger;
        private readonly RallyApiService _rallyService;
        private readonly AdoApiService _adoService;
        private readonly JsonBasedFieldMappingService _fieldMappingService;
        private readonly EnhancedDuplicateDetectionService _duplicateDetectionService;

        public bool DryRunMode { get; set; }
        public bool EnableDifferencePatch { get; set; } = true;
        public bool PreserveHistoricalFields { get; set; } = true;

        public EnhancedMigrationService(LoggingService loggingService, Action<string, Color> uiLogger = null)
        {
            _loggingService = loggingService;
            _uiLogger = uiLogger;
            _rallyService = new RallyApiService(loggingService);
            _adoService = new AdoApiService(_loggingService, _organizationUrl, _projectName, _personalAccessToken);
            _fieldMappingService = new JsonBasedFieldMappingService(loggingService);
            _fieldMappingService.LoadMappingConfiguration();
            _duplicateDetectionService = new EnhancedDuplicateDetectionService(loggingService, _adoService);
        }

        /// <summary>
        /// Migrate Rally items with enhanced field mapping for better content preservation
        /// </summary>
        public async Task<MigrationProgress> MigrateWithEnhancedFieldsAsync(
            ConnectionSettings settings, 
            List<string> rallyIds, 
            bool includeParents, 
            MigrationProgress existingProgress = null)
        {
            DryRunMode = settings?.DryRun ?? DryRunMode;
            EnableDifferencePatch = settings?.EnableDifferencePatch ?? EnableDifferencePatch;
            var bypassRules = settings?.BypassRules ?? false;

            var progress = existingProgress ?? new MigrationProgress();
            if (progress.ProcessedItems > 0) LogToUI($"Resuming migration from checkpoint index {progress.LastCheckpointIndex}", Color.Blue);

            try
            {
                LogToUI("🚀 Starting Enhanced Migration with Better Field Preservation", Color.Blue);
                var rallyItems = new List<RallyWorkItem>();

                int startIndex = progress.LastCheckpointIndex;
                var idsToProcess = rallyIds.Skip(startIndex).ToList();

                foreach (var rallyId in idsToProcess)
                {
                    LogToUI($"📥 Retrieving Rally item: {rallyId}", Color.Blue);
                    var rallyItem = await _rallyService.GetWorkItemByIdAsync(settings, rallyId);
                    if (rallyItem != null)
                    {
                        // Enrich Test Cases with test steps
                        if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                        {
                            await _rallyService.EnrichTestCaseStepsAsync(rallyItem, settings);
                            if (rallyItem.Steps != null && rallyItem.Steps.Any())
                            {
                                LogToUI($"   📝 Fetched {rallyItem.Steps.Count} test steps from Rally", Color.Cyan);
                            }
                        }
                        
                        rallyItems.Add(rallyItem);
                        LogToUI($"✅ Retrieved: {rallyItem.FormattedID} - {rallyItem.Name}", Color.Green);
                    }
                    else
                    {
                        LogToUI($"❌ Not found: {rallyId}", Color.Red);
                    }
                }

                if (existingProgress == null) progress.TotalItems = rallyIds.Count; // original total

                if (rallyItems.Count == 0)
                {
                    LogToUI("⚠️ No valid Rally items found to migrate", Color.Orange);
                    return progress;
                }

                LogToUI($"🗺️ Starting enhanced field mapping for {rallyItems.Count} items", Color.Blue);
                
                // Process each item with enhanced mapping
                foreach (var rallyItem in rallyItems)
                {
                    var result = new MigrationResult
                    {
                        RallyId = rallyItem.ObjectID,
                        RallyFormattedId = rallyItem.FormattedID,
                        ProcessedAt = DateTime.Now,
                        PatchedFields = new List<string>()
                    };

                    try
                    {
                        LogToUI($"🗺️ Processing: {rallyItem.FormattedID}", Color.Blue);
                        
                        // Log Rally Type for debugging
                        _loggingService.LogInfo($"📋 Rally Item Type: '{rallyItem.Type}' for {rallyItem.FormattedID}");
                        
                        // Enhanced duplicate detection - find existing work item
                        var (existsInAdo, existingAdoId, existingWorkItemData) = await _duplicateDetectionService.FindExistingWorkItemAsync(settings, rallyItem);
                        
                        var (creationFields, postFields) = _fieldMappingService.TransformRallyWorkItemToAdoFieldsSplit(rallyItem);
                        
                        // Log what ADO WorkItemType was mapped
                        if (creationFields.ContainsKey("System.WorkItemType"))
                        {
                            _loggingService.LogInfo($"🎯 Mapped to ADO WorkItemType: '{creationFields["System.WorkItemType"]}' for {rallyItem.FormattedID}");
                            LogToUI($"   🎯 ADO Type: {creationFields["System.WorkItemType"]}", Color.Cyan);
                        }

                        if (existsInAdo && existingAdoId > 0)
                        {
                            result.IsSkipped = false; // Not skipping, we're updating
                            result.AdoId = existingAdoId;
                            LogToUI($"🔄 Found existing work item {existingAdoId} for {rallyItem.FormattedID}, checking for updates...", Color.Blue);

                            if (EnableDifferencePatch)
                            {
                                // Merge creation and post fields for comparison
                                var allFields = new Dictionary<string, object>(creationFields);
                                foreach (var kv in postFields)
                                {
                                    if (!allFields.ContainsKey(kv.Key))
                                        allFields[kv.Key] = kv.Value;
                                }

                                // Compare and get only differences
                                var differences = _duplicateDetectionService.CompareAndGetDifferences(rallyItem, existingWorkItemData, allFields);
                                
                                if (differences.Any())
                                {
                                    LogToUI($"   📝 Updating {differences.Count} changed fields: {string.Join(", ", differences.Keys.Take(5))}", Color.Blue);
                                    
                                    var patched = await _adoService.PatchWorkItemFieldsAsync(settings, existingAdoId, differences, bypassRules);
                                    if (patched)
                                    {
                                        result.WasPatched = true;
                                        result.Success = true;
                                        result.PatchedFields.AddRange(differences.Keys);
                                        progress.SuccessfulItems++;
                                        LogToUI($"   ✅ Updated {differences.Count} fields on existing work item {existingAdoId}", Color.Green);
                                    }
                                    else
                                    {
                                        LogToUI($"   ⚠️ Failed to update fields on existing work item", Color.Orange);
                                        result.ErrorMessage = "Patch failed";
                                        progress.FailedItems++;
                                    }
                                }
                                else
                                {
                                    result.IsSkipped = true;
                                    result.SkipReason = "No changes detected";
                                    progress.SkippedItems++;
                                    LogToUI($"   ℹ️ No changes detected, work item is up-to-date", Color.Blue);
                                }
                            }
                            else
                            {
                                result.IsSkipped = true;
                                result.SkipReason = "Difference patching disabled";
                                progress.SkippedItems++;
                                LogToUI($"   ⏭️ Skipped (difference patching disabled)", Color.Orange);
                            }

                            // **NEW: Handle test steps for existing Test Cases**
                            if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase) &&
                                rallyItem.Steps != null && rallyItem.Steps.Any())
                            {
                                LogToUI($"   📝 Updating {rallyItem.Steps.Count} test steps on existing Test Case", Color.Blue);
                                
                                // Log each step for debugging
                                for (int i = 0; i < Math.Min(3, rallyItem.Steps.Count); i++)
                                {
                                    var step = rallyItem.Steps[i];
                                    _loggingService.LogDebug($"Step {i + 1}: Index={step.StepIndex}");
                                    _loggingService.LogDebug($"  Input: {step.Input?.Substring(0, Math.Min(100, step.Input?.Length ?? 0))}");
                                    _loggingService.LogDebug($"  Expected: {step.ExpectedResult?.Substring(0, Math.Min(100, step.ExpectedResult?.Length ?? 0))}");
                                }
                                
                                try
                                {
                                    var stepsXml = TestStepsXmlBuilder.BuildTestStepsXml(rallyItem.Steps);
                                    
                                    // Save XML to file for debugging
                                    var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"TestSteps_{existingAdoId}.xml");
                                    System.IO.File.WriteAllText(debugPath, stepsXml);
                                    _loggingService.LogInfo($"Full Steps XML saved to: {debugPath}");
                                    
                                    // Log the first 1000 chars of XML
                                    _loggingService.LogInfo($"Generated Steps XML (first 1000 chars): {stepsXml?.Substring(0, Math.Min(1000, stepsXml?.Length ?? 0))}");
                                    
                                    if (!string.IsNullOrEmpty(stepsXml))
                                    {
                                        var stepsField = new Dictionary<string, object>
                                        {
                                            ["Microsoft.VSTS.TCM.Steps"] = stepsXml
                                        };
                                        
                                        // Get work item BEFORE patching to see current state
                                        var beforePatch = await _adoService.GetWorkItemByIdAsync(settings, existingAdoId);
                                        var stepsBeforePatch = beforePatch?["fields"]?["Microsoft.VSTS.TCM.Steps"]?.ToString();
                                        _loggingService.LogDebug($"Steps field BEFORE patch: {(string.IsNullOrEmpty(stepsBeforePatch) ? "EMPTY" : $"{stepsBeforePatch.Length} chars")}");
                                        
                                        var stepsPatched = await _adoService.PatchWorkItemFieldsAsync(settings, existingAdoId, stepsField, false);
                                        
                                        if (stepsPatched)
                                        {
                                            // Verify the patch worked by reading back
                                            var afterPatch = await _adoService.GetWorkItemByIdAsync(settings, existingAdoId);
                                            var stepsAfterPatch = afterPatch?["fields"]?["Microsoft.VSTS.TCM.Steps"]?.ToString();
                                            _loggingService.LogDebug($"Steps field AFTER patch: {(string.IsNullOrEmpty(stepsAfterPatch) ? "EMPTY" : $"{stepsAfterPatch.Length} chars")}");
                                            
                                            if (!string.IsNullOrEmpty(stepsAfterPatch))
                                            {
                                                // Save the result
                                                var resultPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"TestSteps_{existingAdoId}_Result.xml");
                                                System.IO.File.WriteAllText(resultPath, stepsAfterPatch);
                                                LogToUI($"   ✅ Updated {rallyItem.Steps.Count} test steps (XML format) on existing Test Case", Color.Green);
                                                _loggingService.LogInfo($"Test steps VERIFIED in ADO! Result saved to: {resultPath}");
                                                
                                                // **WORKAROUND: Also append steps to Description as HTML table**
                                                // This ensures steps are visible even if ADO UI doesn't render the XML
                                                LogToUI($"   📄 Adding test steps to Description field as HTML table (workaround)", Color.Blue);
                                                try
                                                {
                                                    var testStepsService = new TestStepsToDescriptionService(_loggingService);
                                                    var currentDescription = afterPatch?["fields"]?["System.Description"]?.ToString() ?? string.Empty;
                                                    
                                                    // Check if steps are already in description (avoid duplicates)
                                                    if (!currentDescription.Contains("Test Steps (Migrated from Rally)"))
                                                    {
                                                        var updatedDescription = testStepsService.AppendTestStepsToDescription(currentDescription, rallyItem.Steps);
                                                        
                                                        var descriptionField = new Dictionary<string, object>
                                                        {
                                                            ["System.Description"] = updatedDescription
                                                        };
                                                        
                                                        var descPatched = await _adoService.PatchWorkItemFieldsAsync(settings, existingAdoId, descriptionField, false);
                                                        if (descPatched)
                                                        {
                                                            LogToUI($"   ✅ Test steps also added to Description as HTML table", Color.Green);
                                                            _loggingService.LogInfo($"Test steps added to Description field as workaround");
                                                        }
                                                    }
                                                    else
                                                    {
                                                        LogToUI($"   ℹ️ Test steps already in Description, skipping duplicate", Color.Blue);
                                                    }
                                                }
                                                catch (Exception descEx)
                                                {
                                                    _loggingService.LogWarning($"Failed to add test steps to Description: {descEx.Message}");
                                                }
                                            }
                                            else
                                            {
                                                LogToUI($"   ⚠️ Steps field is EMPTY after patch - ADO might have rejected the XML format", Color.Orange);
                                                _loggingService.LogWarning($"CRITICAL: Test steps field is EMPTY after successful PATCH. ADO may have rejected the XML format.");
                                                
                                                // **FALLBACK: Add to Description since XML didn't work**
                                                LogToUI($"   🔄 Fallback: Adding test steps to Description field instead", Color.Orange);
                                                try
                                                {
                                                    var testStepsService = new TestStepsToDescriptionService(_loggingService);
                                                    var currentDescription = afterPatch?["fields"]?["System.Description"]?.ToString() ?? string.Empty;
                                                    var updatedDescription = testStepsService.AppendTestStepsToDescription(currentDescription, rallyItem.Steps);
                                                    
                                                    var descriptionField = new Dictionary<string, object>
                                                    {
                                                        ["System.Description"] = updatedDescription
                                                    };
                                                    
                                                    var descPatched = await _adoService.PatchWorkItemFieldsAsync(settings, existingAdoId, descriptionField, false);
                                                    if (descPatched)
                                                    {
                                                        LogToUI($"   ✅ Test steps added to Description field as HTML table (fallback)", Color.Green);
                                                    }
                                                }
                                                catch (Exception descEx)
                                                {
                                                    LogToUI($"   ❌ Fallback also failed: {descEx.Message}", Color.Red);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            LogToUI($"   ⚠️ Failed to update test steps - check ADO API response", Color.Orange);
                                        }
                                    }
                                    else
                                    {
                                        LogToUI($"   ⚠️ Generated XML is empty", Color.Orange);
                                    }
                                }
                                catch (Exception stepsEx)
                                {
                                    _loggingService.LogWarning($"Failed to update test steps for {rallyItem.FormattedID}: {stepsEx.Message}");
                                    LogToUI($"   ⚠️ Test steps update error: {stepsEx.Message}", Color.Orange);
                                }
                            }

                            // Record mapping for hierarchy linking
                            if (!string.IsNullOrEmpty(rallyItem.ObjectID) && existingAdoId > 0)
                                progress.RallyToAdoIdMap[rallyItem.ObjectID] = existingAdoId;

                            if (DryRunMode)
                            {
                                progress.RallyToAdoIdMap[rallyItem.ObjectID] = existingAdoId;
                            }
                        }
                        else
                        {
                            LogToUI($"📊 Creation fields: {creationFields.Count} | Post fields: {postFields.Count}", Color.Blue);

                            if (DryRunMode)
                            {
                                LogToUI($"🔍 Dry-Run: Would create ADO item for {rallyItem.FormattedID} with {creationFields.Count} fields", Color.Green);
                                result.Success = true; // treat as success for dry-run summary
                                progress.SuccessfulItems++;
                            }
                            else
                            {
                                LogToUI($"⬆️ Creating work item in ADO: {rallyItem.FormattedID}", Color.Blue);
                                // Ensure iteration path exists before creation
                                if (creationFields.ContainsKey("System.IterationPath") && !DryRunMode)
                                {
                                    var iterPath = creationFields["System.IterationPath"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(iterPath))
                                    {
                                        LogToUI($"   📅 Ensuring iteration path exists: {iterPath}", Color.Blue);
                                        var iterOk = await _adoService.EnsureIterationPathExistsAsync(settings, iterPath);
                                        if (!iterOk) LogToUI("   ⚠️ Iteration path creation failed or partial", Color.Orange);
                                    }
                                }
                                // Merge Description + Notes HTML if both present
                                if (creationFields.ContainsKey("System.Description"))
                                {
                                    var desc = creationFields["System.Description"]?.ToString();
                                    if (postFields.ContainsKey("System.Description"))
                                    {
                                        // if Notes mapped also as System.Description via HTML_APPEND
                                        var notes = postFields["System.Description"]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(notes) && notes != desc)
                                        {
                                            creationFields["System.Description"] = desc + "<hr/><h3>Rally Notes</h3>" + notes;
                                            postFields.Remove("System.Description");
                                        }
                                    }
                                }
                                // Build rich HTML description before creation
                                try
                                {
                                    var richAssembler = new RichContentAssembler();
                                    var richHtml = richAssembler.BuildUnifiedDescription(rallyItem);
                                    if (!string.IsNullOrWhiteSpace(richHtml))
                                    {
                                        creationFields["System.Description"] = richHtml;
                                        if (postFields.ContainsKey("System.Description")) postFields.Remove("System.Description");
                                    }
                                }
                                catch (Exception rcEx)
                                {
                                    LogToUI($"   ⚠️ Rich content assembly failed: {rcEx.Message}", Color.Orange);
                                }
                                
                                // Validate AssignedTo user exists in ADO before creation with domain fallback
                                // This needs to happen AFTER field mapping but BEFORE work item creation
                                await ValidateUserAndUpdateFieldsAsync(settings, creationFields, rallyItem.Owner);
                                
                                // **CRITICAL FIX**: Remove Microsoft.VSTS.TCM.Steps from creationFields to avoid duplicate update error
                                // Steps will be added separately via AddTestCaseStepsAsync after work item creation
                                if (creationFields.ContainsKey("Microsoft.VSTS.TCM.Steps"))
                                {
                                    _loggingService.LogDebug("Removed Microsoft.VSTS.TCM.Steps from creationFields - will be added separately");
                                    creationFields.Remove("Microsoft.VSTS.TCM.Steps");
                                }
                                
                                var createdItem = await _adoService.CreateWorkItemWithFallbackAsync(settings, creationFields);
                                if (createdItem != null)
                                {
                                    result.AdoId = createdItem.Id;
                                    result.Success = true;
                                    progress.SuccessfulItems++;
                                    LogToUI($"✅ Created ADO ID {createdItem.Id} for {rallyItem.FormattedID}", Color.Green);

                                    if (PreserveHistoricalFields && postFields.Any())
                                    {
                                        LogToUI("   🕒 Applying post-creation historical fields", Color.Blue);
                                        var patchOk = await _adoService.PatchWorkItemFieldsAsync(settings, createdItem.Id, postFields, bypassRules);
                                        if (patchOk)
                                        {
                                            result.WasPatched = true;
                                            result.PatchedFields.AddRange(postFields.Keys);
                                            LogToUI($"   Patched fields: {string.Join(", ", postFields.Keys)}", Color.Green);
                                        }
                                        else
                                        {
                                            LogToUI("   ⚠️ Historical field patch failed", Color.Orange);
                                        }
                                    }

                                    // Record mapping for later hierarchy linking
                                    if (!string.IsNullOrEmpty(rallyItem.ObjectID) && createdItem.Id > 0)
                                        progress.RallyToAdoIdMap[rallyItem.ObjectID] = createdItem.Id;

                                    // Migrate and link parent (Feature/Epic) if not already migrated
                                    if (!string.IsNullOrEmpty(rallyItem.Parent))
                                    {
                                        LogToUI($"   📤 Processing parent: {rallyItem.Parent}", Color.Blue);
                                        var parentAdoId = await MigrateParentWorkItemAsync(settings, rallyItem.Parent, progress, bypassRules);
                                        
                                        if (parentAdoId > 0)
                                        {
                                            LogToUI($"   🔗 Linking to parent ADO ID {parentAdoId}", Color.Blue);
                                            var linkOk = await _adoService.LinkWorkItemsAsync(settings, parentAdoId, createdItem.Id, "Child");
                                            if (linkOk)
                                            {
                                                LogToUI($"   ✅ Successfully linked to parent", Color.Green);
                                            }
                                            else
                                            {
                                                LogToUI($"   ⚠️ Parent link failed", Color.Orange);
                                            }
                                        }
                                        else
                                        {
                                            LogToUI($"   ⚠️ Parent migration/lookup failed", Color.Orange);
                                        }
                                    }

                                    // Migrate and link child tasks/work items
                                    if (rallyItem.Children != null && rallyItem.Children.Any())
                                    {
                                        LogToUI($"   👶 Processing {rallyItem.Children.Count} child items", Color.Blue);
                                        await MigrateChildWorkItemsAsync(settings, createdItem.Id, rallyItem.Children, progress, bypassRules);
                                    }

                                    // Migrate and link Test Cases (for User Stories and Defects)
                                    if ((string.Equals(rallyItem.Type, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase) || 
                                         string.Equals(rallyItem.Type, "Defect", StringComparison.OrdinalIgnoreCase)) &&
                                        rallyItem.TestCases != null && rallyItem.TestCases.Any())
                                    {
                                        LogToUI($"   🧪 Processing {rallyItem.TestCases.Count} linked Test Cases", Color.Blue);
                                        await MigrateTestCasesAsync(settings, createdItem.Id, rallyItem.TestCases, progress, bypassRules);
                                    }

                                    // After successful creation and before VerifyCreatedItem
                                    if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase) && rallyItem.Steps != null && rallyItem.Steps.Any())
                                    {
                                        LogToUI($"   🧪 Adding {rallyItem.Steps.Count} test case steps", Color.Blue);
                                        var stepsOk = await _adoService.AddTestCaseStepsAsync(settings, createdItem.Id, rallyItem.Steps, bypassRules);
                                        if (stepsOk)
                                        {
                                            LogToUI($"   ✅ Test steps added to XML field", Color.Green);
                                            
                                            // **CRITICAL: Also add test steps to Description as HTML table**
                                            // This is the guaranteed workaround that ensures steps are always visible
                                            LogToUI($"   📄 Adding test steps to Description field as HTML table (workaround)", Color.Blue);
                                            try
                                            {
                                                var testStepsService = new TestStepsToDescriptionService(_loggingService);
                                                
                                                // Get current description
                                                var workItem = await _adoService.GetWorkItemByIdAsync(settings, createdItem.Id);
                                                var currentDescription = workItem?["fields"]?["System.Description"]?.ToString() ?? string.Empty;
                                                
                                                // Append test steps table to description
                                                var updatedDescription = testStepsService.AppendTestStepsToDescription(currentDescription, rallyItem.Steps);
                                                
                                                var descriptionField = new Dictionary<string, object>
                                                {
                                                    ["System.Description"] = updatedDescription
                                                };
                                                
                                                var descPatched = await _adoService.PatchWorkItemFieldsAsync(settings, createdItem.Id, descriptionField, false);
                                                if (descPatched)
                                                {
                                                    LogToUI($"   ✅ Test steps also added to Description as HTML table", Color.Green);
                                                    _loggingService.LogInfo($"Test steps added to Description field for new Test Case {createdItem.Id}");
                                                }
                                                else
                                                {
                                                    LogToUI($"   ⚠️ Failed to add test steps to Description", Color.Orange);
                                                }
                                            }
                                            catch (Exception descEx)
                                            {
                                                _loggingService.LogWarning($"Failed to add test steps to Description: {descEx.Message}");
                                                LogToUI($"   ⚠️ Description update error: {descEx.Message}", Color.Orange);
                                            }
                                        }
                                        else
                                        {
                                            LogToUI("   ⚠️ Failed to add test case steps", Color.Orange);
                                        }
                                    }

                                    // Migrate comments to ADO Discussion
                                    if (rallyItem.Comments != null && rallyItem.Comments.Any())
                                    {
                                        LogToUI($"   💬 Migrating {rallyItem.Comments.Count} comments/discussions", Color.Blue);
                                        await MigrateCommentsAsync(settings, createdItem.Id, rallyItem.Comments);
                                    }

                                    // Migrate attachments to ADO
                                    if (rallyItem.Attachments != null && rallyItem.Attachments.Any())
                                    {
                                        LogToUI($"   📎 Migrating {rallyItem.Attachments.Count} attachments", Color.Blue);
                                        await MigrateAttachmentsAsync(settings, createdItem.Id, rallyItem.Attachments);
                                    }

                                    await VerifyCreatedItem(settings, createdItem.Id, rallyItem.FormattedID);
                                }
                                else
                                {
                                    result.Success = false;
                                    result.ErrorMessage = "ADO work item creation returned null";
                                    progress.FailedItems++;
                                    LogToUI($"❌ FAILED: {rallyItem.FormattedID} - creation returned null", Color.Red);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;
                        progress.FailedItems++;
                        LogToUI($"❌ ERROR: {rallyItem.FormattedID} - {ex.Message}", Color.Red);
                        
                        // Log detailed error for diagnostics
                        _loggingService.LogError($"Enhanced migration failed for {rallyItem.FormattedID}", ex);
                    }

                    progress.ProcessedItems++;
                    progress.LastCheckpointIndex = progress.ProcessedItems; // update checkpoint
                    progress.Results.Add(result);

                    // After progress.Results.Add(result); add persistence
                    try
                    {
                        PersistCheckpoint(progress);
                    }
                    catch (Exception cpEx)
                    {
                        LogToUI($"Checkpoint persistence failed: {cpEx.Message}", Color.Orange);
                    }
                }

                progress.IsCompleted = progress.ProcessedItems >= progress.TotalItems;
                progress.EndTime = DateTime.Now;

                LogSummary(progress);
                return progress;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Enhanced Migration failed: {ex.Message}", Color.Red);
                _loggingService.LogError("Enhanced migration failed", ex);
                throw;
            }
        }

        private async Task<int> FindExistingAdoId(ConnectionSettings settings, string formattedId, string objectId)
        {
            var existingId = await _adoService.FindExistingWorkItemIdByRallyTagsAsync(settings, formattedId, objectId);
            return existingId; // -1 if not found
        }

        private async Task VerifyCreatedItem(ConnectionSettings settings, int adoId, string formattedId)
        {
            try
            {
                var adoWorkItemJson = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                var title = adoWorkItemJson? ["fields"]? ["System.Title"]?.ToString();
                var state = adoWorkItemJson? ["fields"]? ["System.State"]?.ToString();
                LogToUI($"   Title: {title ?? "<unknown>"} | State: {state ?? "<unknown>"}", Color.Green);
            }
            catch (Exception exVerify)
            {
                LogToUI($"   ⚠️ Verification failed for {formattedId}: {exVerify.Message}", Color.Orange);
            }
        }

        /// <summary>
        /// Migrate Rally comments/discussions to ADO work item comments
        /// </summary>
        private async Task MigrateCommentsAsync(ConnectionSettings settings, int adoWorkItemId, List<RallyComment> comments)
        {
            if (comments == null || !comments.Any())
                return;

            try
            {
                // Sort comments by creation date to maintain chronological order
                var orderedComments = comments.OrderBy(c => c.CreationDate).ToList();
                var successCount = 0;

                foreach (var comment in orderedComments)
                {
                    try
                    {
                        // Fix unicode-escaped HTML in comment text
                        if (!string.IsNullOrEmpty(comment.Text))
                        {
                            // Decode unicode escape sequences like \u003C to <
                            comment.Text = System.Text.RegularExpressions.Regex.Unescape(comment.Text);
                        }
                        
                        var success = await _adoService.AddCommentAsync(settings, adoWorkItemId, comment);
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            _loggingService.LogWarning($"Failed to add comment to work item {adoWorkItemId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Error adding comment to work item {adoWorkItemId}: {ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    LogToUI($"   ✅ Migrated {successCount}/{orderedComments.Count} comments", Color.Green);
                }
                else if (orderedComments.Count > 0)
                {
                    LogToUI($"   ⚠️ Failed to migrate {orderedComments.Count} comments", Color.Orange);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to migrate comments for work item {adoWorkItemId}", ex);
                LogToUI($"   ⚠️ Comment migration error: {ex.Message}", Color.Orange);
            }
        }

        /// <summary>
        /// Migrate parent work item (Feature/Epic) from Rally to ADO if not already migrated
        /// Returns the ADO ID of the parent, or -1 if migration fails
        /// </summary>
        private async Task<int> MigrateParentWorkItemAsync(
            ConnectionSettings settings,
            string parentRallyObjectId,
            MigrationProgress progress,
            bool bypassRules)
        {
            try
            {
                // Check if already mapped (migrated in this session or previous checkpoint)
                if (progress.RallyToAdoIdMap.ContainsKey(parentRallyObjectId))
                {
                    var parentAdoId = progress.RallyToAdoIdMap[parentRallyObjectId];
                    LogToUI($"      ↳ Parent already migrated: ADO ID {parentAdoId}", Color.Blue);
                    return parentAdoId;
                }

                // Fetch parent from Rally
                LogToUI($"      📥 Fetching parent from Rally: {parentRallyObjectId}", Color.Blue);
                var parentRallyItem = await _rallyService.GetWorkItemByIdAsync(settings, parentRallyObjectId);
                
                if (parentRallyItem == null)
                {
                    LogToUI($"      ⚠️ Parent not found in Rally: {parentRallyObjectId}", Color.Orange);
                    return -1;
                }

                LogToUI($"      📋 Parent: {parentRallyItem.FormattedID} ({parentRallyItem.Type}) - {parentRallyItem.Name}", Color.Cyan);

                // Check if already exists in ADO
                var existsInAdo = await _adoService.WorkItemExistsAsync(settings, parentRallyItem.ObjectID);
                int parentAdoWorkItemId;

                if (existsInAdo)
                {
                    // Find existing ADO ID
                    parentAdoWorkItemId = await FindExistingAdoId(settings, parentRallyItem.FormattedID, parentRallyItem.ObjectID);
                    
                    if (parentAdoWorkItemId > 0)
                    {
                        LogToUI($"      ↳ Parent exists in ADO: {parentRallyItem.FormattedID} (ADO ID: {parentAdoWorkItemId})", Color.Blue);
                        progress.RallyToAdoIdMap[parentRallyObjectId] = parentAdoWorkItemId;
                        return parentAdoWorkItemId;
                    }
                    else
                    {
                        LogToUI($"      ⚠️ Parent exists but ADO ID not found: {parentRallyItem.FormattedID}", Color.Orange);
                        return -1;
                    }
                }
                else
                {
                    // Create parent in ADO
                    LogToUI($"      ➕ Creating parent in ADO: {parentRallyItem.FormattedID} - {parentRallyItem.Name}", Color.Blue);
                    
                    var (creationFields, postFields) = _fieldMappingService.TransformRallyWorkItemToAdoFieldsSplit(parentRallyItem);
                    
                    // Log parent details for debugging
                    _loggingService.LogInfo($"📋 Rally Parent Details for {parentRallyItem.FormattedID}:");
                    _loggingService.LogInfo($"   - ObjectID: {parentRallyItem.ObjectID}");
                    _loggingService.LogInfo($"   - Type: {parentRallyItem.Type}");
                    _loggingService.LogInfo($"   - Name: {parentRallyItem.Name}");
                    
                    // Set default Iteration Path if missing
                    if (!creationFields.ContainsKey("System.IterationPath"))
                    {
                        creationFields["System.IterationPath"] = "Acute Meds Management\\Emerson\\Rally Migration";
                        _loggingService.LogDebug($"Set default IterationPath for {parentRallyItem.FormattedID}");
                    }
                    
                    // Build rich description for parent
                    try
                    {
                        var richAssembler = new RichContentAssembler();
                        var richHtml = richAssembler.BuildUnifiedDescription(parentRallyItem);
                        if (!string.IsNullOrWhiteSpace(richHtml))
                        {
                            creationFields["System.Description"] = richHtml;
                            if (postFields.ContainsKey("System.Description")) 
                                postFields.Remove("System.Description");
                        }
                    }
                    catch (Exception rcEx)
                    {
                        _loggingService.LogWarning($"Rich content assembly failed for parent {parentRallyItem.FormattedID}: {rcEx.Message}");
                    }
                    
                    // Validate AssignedTo user
                    await ValidateUserAndUpdateFieldsAsync(settings, creationFields, parentRallyItem.Owner, " for parent");

                    var createdParent = await _adoService.CreateWorkItemWithFallbackAsync(settings, creationFields);
                    
                    if (createdParent != null)
                    {
                        parentAdoWorkItemId = createdParent.Id;
                        progress.RallyToAdoIdMap[parentRallyObjectId] = parentAdoWorkItemId;
                        
                        LogToUI($"      ✅ Created parent: ADO ID {parentAdoWorkItemId}", Color.Green);

                        // Apply post-creation fields if any
                        if (postFields.Any())
                        {
                            LogToUI($"         🔧 Applying post-creation fields for parent: {string.Join(", ", postFields.Keys)}", Color.Blue);
                            
                            var patchSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, parentAdoWorkItemId, postFields, bypassRules);
                            if (patchSuccess)
                            {
                                LogToUI($"         ✅ Post-creation fields applied successfully", Color.Green);
                            }
                            else
                            {
                                LogToUI($"         ⚠️ Post-creation field patch failed", Color.Orange);
                                _loggingService.LogWarning($"Failed to patch post-creation fields for parent {parentRallyItem.FormattedID}");
                            }
                        }

                        // Migrate parent's comments
                        if (parentRallyItem.Comments != null && parentRallyItem.Comments.Any())
                        {
                            LogToUI($"         💬 Migrating {parentRallyItem.Comments.Count} comments for parent", Color.Blue);
                            await MigrateCommentsAsync(settings, parentAdoWorkItemId, parentRallyItem.Comments);
                        }

                        // Migrate parent's attachments
                        if (parentRallyItem.Attachments != null && parentRallyItem.Attachments.Any())
                        {
                            LogToUI($"         📎 Migrating {parentRallyItem.Attachments.Count} attachments for parent", Color.Blue);
                            await MigrateAttachmentsAsync(settings, parentAdoWorkItemId, parentRallyItem.Attachments);
                        }
                        
                        // Recursively migrate parent's parent (grandparent) if exists
                        if (!string.IsNullOrEmpty(parentRallyItem.Parent))
                        {
                            LogToUI($"         📤 Parent has a grandparent: {parentRallyItem.Parent}", Color.Blue);
                            var grandparentAdoId = await MigrateParentWorkItemAsync(settings, parentRallyItem.Parent, progress, bypassRules);
                            
                            if (grandparentAdoId > 0)
                            {
                                LogToUI($"         🔗 Linking parent to grandparent ADO ID {grandparentAdoId}", Color.Blue);
                                var linkOk = await _adoService.LinkWorkItemsAsync(settings, grandparentAdoId, parentAdoWorkItemId, "Child");
                                if (linkOk)
                                {
                                    LogToUI($"         ✅ Successfully linked parent to grandparent", Color.Green);
                                }
                                else
                                {
                                    LogToUI($"         ⚠️ Grandparent link failed", Color.Orange);
                                }
                            }
                        }

                        return parentAdoWorkItemId;
                    }
                    else
                    {
                        LogToUI($"      ❌ Failed to create parent: {parentRallyItem.FormattedID}", Color.Red);
                        return -1;
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to migrate parent {parentRallyObjectId}", ex);
                LogToUI($"      ❌ Error migrating parent: {ex.Message}", Color.Red);
                return -1;
            }
        }

        /// <summary>
        /// Migrate child work items (Tasks) from Rally to ADO and link them to parent
        /// Similar to Jira-to-Rally migration approach: create if missing, then link
        /// </summary>
        private async Task MigrateChildWorkItemsAsync(
            ConnectionSettings settings, 
            int parentAdoId, 
            List<string> childObjectIds, 
            MigrationProgress progress,
            bool bypassRules)
        {
            if (childObjectIds == null || !childObjectIds.Any())
                return;

            var migratedCount = 0;
            var linkedCount = 0;

            foreach (var childObjectId in childObjectIds)
            {
                try
                {
                    // Check if already mapped (migrated in this session or previous checkpoint)
                    if (progress.RallyToAdoIdMap.ContainsKey(childObjectId))
                    {
                        var childAdoId = progress.RallyToAdoIdMap[childObjectId];
                        LogToUI($"      ↳ Child already migrated, linking to ADO ID {childAdoId}", Color.Blue);
                        
                        var linkOk = await _adoService.LinkWorkItemsAsync(settings, parentAdoId, childAdoId, "Child");
                        if (linkOk)
                        {
                            linkedCount++;
                        }
                        else
                        {
                            LogToUI($"      ⚠️ Failed to link child {childAdoId}", Color.Orange);
                        }
                        continue;
                    }

                    // Fetch child from Rally
                    LogToUI($"      📥 Fetching child task from Rally: {childObjectId}", Color.Blue);
                    var childRallyItem = await _rallyService.GetWorkItemByIdAsync(settings, childObjectId);
                    
                    if (childRallyItem == null)
                    {
                        LogToUI($"      ⚠️ Child not found in Rally: {childObjectId}", Color.Orange);
                        continue;
                    }

                    // Check if already exists in ADO
                    var existsInAdo = await _adoService.WorkItemExistsAsync(settings, childRallyItem.ObjectID);
                    int childAdoWorkItemId;

                    if (existsInAdo)
                    {
                        // Find existing ADO ID
                        childAdoWorkItemId = await FindExistingAdoId(settings, childRallyItem.FormattedID, childRallyItem.ObjectID);
                        
                        if (childAdoWorkItemId > 0)
                        {
                            LogToUI($"      ↳ Child exists in ADO: {childRallyItem.FormattedID} (ADO ID: {childAdoWorkItemId})", Color.Blue);
                            progress.RallyToAdoIdMap[childObjectId] = childAdoWorkItemId;
                        }
                        else
                        {
                            LogToUI($"      ⚠️ Child exists but ADO ID not found: {childRallyItem.FormattedID}", Color.Orange);
                            continue;
                        }
                    }
                    else
                    {
                        // Create child in ADO
                        LogToUI($"      ➕ Creating child in ADO: {childRallyItem.FormattedID} - {childRallyItem.Name}", Color.Blue);
                        
                        var (creationFields, postFields) = _fieldMappingService.TransformRallyWorkItemToAdoFieldsSplit(childRallyItem);
                        
                        // Log Rally work item details for debugging
                        _loggingService.LogInfo($"📋 Rally Task Details for {childRallyItem.FormattedID}:");
                        _loggingService.LogInfo($"   - ObjectID: {childRallyItem.ObjectID}");
                        _loggingService.LogInfo($"   - Type: {childRallyItem.Type}");
                        _loggingService.LogInfo($"   - State (from RallyWorkItem.State): '{childRallyItem.State}'");
                        _loggingService.LogInfo($"   - Name: {childRallyItem.Name}");
                        
                        // Set default Iteration Path for Tasks
                        if (!creationFields.ContainsKey("System.IterationPath"))
                        {
                            creationFields["System.IterationPath"] = "Acute Meds Management\\Emerson\\Rally Migration";
                            _loggingService.LogDebug($"Set default IterationPath for {childRallyItem.FormattedID}");
                        }
                        
                        // Ensure Rally Tasks are created as ADO Tasks (not Bugs)
                        if (string.Equals(childRallyItem.Type, "Task", StringComparison.OrdinalIgnoreCase))
                        {
                            // Override work item type to ensure it's created as Task in ADO
                            if (!creationFields.ContainsKey("System.WorkItemType"))
                            {
                                creationFields["System.WorkItemType"] = "Task";
                                _loggingService.LogDebug($"Set WorkItemType to Task for {childRallyItem.FormattedID}");
                            }
                            else if (creationFields["System.WorkItemType"]?.ToString() != "Task")
                            {
                                creationFields["System.WorkItemType"] = "Task";
                                _loggingService.LogDebug($"Overrode WorkItemType to Task for {childRallyItem.FormattedID} (was {creationFields["System.WorkItemType"]})");
                            }
                        }
                        
                        // Move State to creation with correct mapping (not post-creation)
                        // Rally Task states: Defined, In-Progress, Completed, Open
                        // ADO Task states: New, Active, Closed
                        // User lacks bypass rules permission, so we must set correct state during creation
                        
                        // Get Rally State from the actual Rally work item (not from field mapping which may be in postFields)
                        var rallyState = childRallyItem.State;
                        
                        // Remove State from postFields if present (we'll set it during creation)
                        if (postFields.ContainsKey("System.State"))
                        {
                            rallyState = postFields["System.State"]?.ToString() ?? rallyState;
                            postFields.Remove("System.State");
                            _loggingService.LogDebug($"Moved System.State from postFields to creationFields for {childRallyItem.FormattedID}");
                        }
                        
                        // Log the original Rally State for debugging
                        _loggingService.LogInfo($"📋 Rally Task State (from childRallyItem): '{rallyState}' for {childRallyItem.FormattedID}");
                        LogToUI($"         📋 Rally State: '{rallyState}'", Color.Cyan);
                        
                        // Map Rally Task states to ADO Task states
                        // ADO Standard Agile Process: Tasks can only be created with State="New"
                        // Then transition: New → Active → Resolved → Closed
                        string adoState = "New"; // MUST be "New" for creation in Agile process
                        string targetState = null; // Final state to patch to after creation
                        
                        if (!string.IsNullOrEmpty(rallyState))
                        {
                            // Normalize the state string (trim, lowercase, handle variations)
                            var normalizedState = rallyState.Trim().ToLower().Replace("-", "").Replace(" ", "");
                            
                            switch (normalizedState)
                            {
                                case "defined":
                                case "open": // Rally "Open" state for tasks → New
                                    adoState = "New";
                                    targetState = null; // Already New, no patch needed
                                    break;
                                case "inprogress":
                                    adoState = "New"; // Create as New
                                    targetState = "Active"; // Then patch to Active
                                    break;
                                case "completed":
                                case "closed": // Rally "Closed" state → Closed
                                case "resolved": // FieldTransformationService may have already converted Completed→Resolved
                                    // Create as New, then patch to Closed
                                    adoState = "New";
                                    targetState = "Closed";
                                    _loggingService.LogInfo($"⚠️ Task will be created as 'New' then patched to 'Closed'");
                                    break;
                                default:
                                    // Try partial matches for safety
                                    if (normalizedState.Contains("progress"))
                                    {
                                        adoState = "New";
                                        targetState = "Active";
                                    }
                                    else if (normalizedState.Contains("complet") || normalizedState.Contains("done") || normalizedState.Contains("closed") || normalizedState.Contains("resolved"))
                                    {
                                        adoState = "New";
                                        targetState = "Closed";
                                        _loggingService.LogInfo($"⚠️ Task will be created as 'New' then patched to 'Closed'");
                                    }
                                    else
                                        adoState = "New"; // Safe default
                                    break;
                            }
                            _loggingService.LogInfo($"🔄 State mapping: Rally '{rallyState}' → ADO '{adoState}'{(targetState != null ? $" → '{targetState}'" : "")} for {childRallyItem.FormattedID}");
                            LogToUI($"         🔄 Mapped State: '{rallyState}' → '{adoState}'{(targetState != null ? $" → '{targetState}'" : "")}", Color.Blue);
                        }
                        
                        // Set the mapped state in creation fields
                        creationFields["System.State"] = adoState;
                        _loggingService.LogDebug($"Set System.State='{adoState}' in creationFields for {childRallyItem.FormattedID}");
                        
                        // Build rich description for child
                        try
                        {
                            var richAssembler = new RichContentAssembler();
                            var richHtml = richAssembler.BuildUnifiedDescription(childRallyItem);
                            if (!string.IsNullOrWhiteSpace(richHtml))
                            {
                                creationFields["System.Description"] = richHtml;
                                if (postFields.ContainsKey("System.Description")) 
                                    postFields.Remove("System.Description");
                            }
                            
                            // Append Rally historical dates to description since ADO won't accept them as field values
                            if (postFields.ContainsKey("System.CreatedDate") || postFields.ContainsKey("System.ChangedDate"))
                            {
                                var dateMetadata = new StringBuilder();
                                dateMetadata.Append("<hr/><div style='color: #666; font-size: 0.9em;'><strong>Rally Historical Dates:</strong><br/>");
                                
                                if (postFields.ContainsKey("System.CreatedDate"))
                                {
                                    dateMetadata.Append($"Created: {postFields["System.CreatedDate"]}<br/>");
                                }
                                if (postFields.ContainsKey("System.ChangedDate"))
                                {
                                    dateMetadata.Append($"Last Updated: {postFields["System.ChangedDate"]}<br/>");
                                }
                                
                                dateMetadata.Append("</div>");
                                
                                // Append to existing description
                                if (creationFields.ContainsKey("System.Description"))
                                {
                                    creationFields["System.Description"] = creationFields["System.Description"]?.ToString() + dateMetadata.ToString();
                                }
                                else
                                {
                                    creationFields["System.Description"] = dateMetadata.ToString();
                                }
                                
                                _loggingService.LogDebug($"Added Rally historical dates to description for {childRallyItem.FormattedID}");
                            }
                        }
                        catch (Exception rcEx)
                        {
                            _loggingService.LogWarning($"Rich content assembly failed for child {childRallyItem.FormattedID}: {rcEx.Message}");
                        }
                        
                        // Validate AssignedTo user exists in ADO before creation with domain fallback
                        await ValidateUserAndUpdateFieldsAsync(settings, creationFields, childRallyItem.Owner, " for child Task");

                        var createdChild = await _adoService.CreateWorkItemWithFallbackAsync(settings, creationFields);
                        
                        if (createdChild != null)
                        {
                            childAdoWorkItemId = createdChild.Id;
                            progress.RallyToAdoIdMap[childObjectId] = childAdoWorkItemId;
                            migratedCount++;
                            
                            LogToUI($"      ✅ Created child: ADO ID {childAdoWorkItemId}", Color.Green);

                            // If we need to patch to Closed state (ADO limitation workaround)
                            if (!string.IsNullOrEmpty(targetState) && targetState != adoState)
                            {
                                LogToUI($"         🔄 Patching state from '{adoState}' to '{targetState}'...", Color.Blue);
                                var statePatchFields = new Dictionary<string, object>
                                {
                                    ["System.State"] = targetState
                                };
                                
                                var statePatchSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, childAdoWorkItemId, statePatchFields, false);
                                if (statePatchSuccess)
                                {
                                    LogToUI($"         ✅ State patched to '{targetState}'", Color.Green);
                                    _loggingService.LogInfo($"Successfully patched Task {childRallyItem.FormattedID} state to '{targetState}'");
                                }
                                else
                                {
                                    LogToUI($"         ⚠️ Failed to patch state to '{targetState}', will remain as '{adoState}'", Color.Orange);
                                    _loggingService.LogWarning($"Failed to patch Task {childRallyItem.FormattedID} state to '{targetState}'");
                                }
                            }

                            // Apply post-creation fields if any (historical dates removed, State set during creation)
                            if (postFields.Any())
                            {
                                // Remove historical date fields for Tasks to avoid "dates must be increasing" error
                                // ADO doesn't allow setting CreatedDate/ChangedDate to past dates on child items
                                var fieldsToRemove = new List<string>();
                                if (postFields.ContainsKey("System.CreatedDate"))
                                {
                                    fieldsToRemove.Add("System.CreatedDate");
                                    _loggingService.LogDebug($"Removed System.CreatedDate from post-fields for {childRallyItem.FormattedID} (historical dates not supported for Tasks)");
                                }
                                if (postFields.ContainsKey("System.ChangedDate"))
                                {
                                    fieldsToRemove.Add("System.ChangedDate");
                                    _loggingService.LogDebug($"Removed System.ChangedDate from post-fields for {childRallyItem.FormattedID} (historical dates not supported for Tasks)");
                                }
                                // Also remove State if present (now set during creation)
                                if (postFields.ContainsKey("System.State"))
                                {
                                    fieldsToRemove.Add("System.State");
                                    _loggingService.LogDebug($"Removed System.State from post-fields for {childRallyItem.FormattedID} (State set during creation to avoid bypass rules requirement)");
                                }
                                
                                foreach (var fieldToRemove in fieldsToRemove)
                                {
                                    postFields.Remove(fieldToRemove);
                                }
                                
                                if (postFields.Any()) // Only patch if there are remaining fields
                                {
                                    LogToUI($"         🔧 Applying post-creation fields: {string.Join(", ", postFields.Keys)}", Color.Blue);
                                    
                                    // Try without bypass rules first since user lacks permission
                                    var patchSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, childAdoWorkItemId, postFields, false);
                                    if (patchSuccess)
                                    {
                                        LogToUI($"         ✅ Post-creation fields applied successfully", Color.Green);
                                    }
                                    else
                                    {
                                        LogToUI($"         ⚠️ Post-creation field patch failed", Color.Orange);
                                        _loggingService.LogWarning($"Failed to patch post-creation fields for {childRallyItem.FormattedID}");
                                    }
                                }
                                else
                                {
                                    _loggingService.LogDebug($"No post-creation fields to apply for {childRallyItem.FormattedID} after removing unsupported fields");
                                }
                            }

                            // Migrate child's comments
                            if (childRallyItem.Comments != null && childRallyItem.Comments.Any())
                            {
                                await MigrateCommentsAsync(settings, childAdoWorkItemId, childRallyItem.Comments);
                            }

                            // Migrate child's attachments
                            if (childRallyItem.Attachments != null && childRallyItem.Attachments.Any())
                            {
                                await MigrateAttachmentsAsync(settings, childAdoWorkItemId, childRallyItem.Attachments);
                            }
                        }
                        else
                        {
                            LogToUI($"      ❌ Failed to create child: {childRallyItem.FormattedID}", Color.Red);
                            continue;
                        }
                    }

                    // Link child to parent
                    var linkSuccess = await _adoService.LinkWorkItemsAsync(settings, parentAdoId, childAdoWorkItemId, "Child");
                    
                    if (linkSuccess)
                    {
                        linkedCount++;
                        LogToUI($"      🔗 Linked child {childAdoWorkItemId} to parent {parentAdoId}", Color.Green);
                    }
                    else
                    {
                        LogToUI($"      ⚠️ Failed to link child {childAdoWorkItemId} to parent {parentAdoId}", Color.Orange);
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to migrate child {childObjectId}", ex);
                    LogToUI($"      ❌ Error migrating child: {ex.Message}", Color.Red);
                }
            }

            if (migratedCount > 0 || linkedCount > 0)
            {
                LogToUI($"   ✅ Child migration complete: {migratedCount} created, {linkedCount} linked", Color.Green);
            }
        }

        /// <summary>
        /// Migrate Test Cases linked to a User Story or Defect from Rally to ADO
        /// Creates Test Cases in ADO and links them using "Tests" relationship
        /// </summary>
        private async Task MigrateTestCasesAsync(
            ConnectionSettings settings,
            int workProductAdoId, // User Story or Defect ADO ID
            List<string> testCaseObjectIds,
            MigrationProgress progress,
            bool bypassRules)
        {
            if (testCaseObjectIds == null || !testCaseObjectIds.Any())
                return;

            var migratedCount = 0;
            var linkedCount = 0;

            foreach (var testCaseObjectId in testCaseObjectIds)
            {
                try
                {
                    // Check if already mapped (migrated in this session)
                    if (progress.RallyToAdoIdMap.ContainsKey(testCaseObjectId))
                    {
                        var testCaseAdoId = progress.RallyToAdoIdMap[testCaseObjectId];
                        LogToUI($"      ↳ Test Case already migrated, linking to ADO ID {testCaseAdoId}", Color.Blue);
                        
                        var linkOk = await _adoService.LinkWorkItemsAsync(settings, workProductAdoId, testCaseAdoId, "Tests");
                        if (linkOk)
                        {
                            linkedCount++;
                            LogToUI($"      🔗 Linked Test Case {testCaseAdoId} to work item {workProductAdoId}", Color.Green);
                        }
                        else
                        {
                            LogToUI($"      ⚠️ Failed to link Test Case {testCaseAdoId}", Color.Orange);
                        }
                        continue;
                    }

                    // Fetch Test Case from Rally
                    LogToUI($"      📥 Fetching Test Case from Rally: {testCaseObjectId}", Color.Blue);
                    var testCaseRallyItem = await _rallyService.GetWorkItemByIdAsync(settings, testCaseObjectId);
                    
                    if (testCaseRallyItem == null)
                    {
                        LogToUI($"      ⚠️ Test Case not found in Rally: {testCaseObjectId}", Color.Orange);
                        continue;
                    }

                    // Enrich with test steps
                    await _rallyService.EnrichTestCaseStepsAsync(testCaseRallyItem, settings);

                    LogToUI($"      📋 Test Case: {testCaseRallyItem.FormattedID} - {testCaseRallyItem.Name}", Color.Cyan);
                    if (testCaseRallyItem.Steps != null && testCaseRallyItem.Steps.Any())
                    {
                        LogToUI($"      📝 Found {testCaseRallyItem.Steps.Count} test steps", Color.Cyan);
                    }

                    // Check if already exists in ADO
                    var existsInAdo = await _adoService.WorkItemExistsAsync(settings, testCaseRallyItem.ObjectID);
                    int testCaseAdoWorkItemId;

                    if (existsInAdo)
                    {
                        // Find existing ADO ID
                        testCaseAdoWorkItemId = await FindExistingAdoId(settings, testCaseRallyItem.FormattedID, testCaseRallyItem.ObjectID);
                        
                        if (testCaseAdoWorkItemId > 0)
                        {
                            LogToUI($"      ↳ Test Case exists in ADO: {testCaseRallyItem.FormattedID} (ADO ID: {testCaseAdoWorkItemId})", Color.Blue);
                            progress.RallyToAdoIdMap[testCaseObjectId] = testCaseAdoWorkItemId;
                        }
                        else
                        {
                            LogToUI($"      ⚠️ Test Case exists but ADO ID not found: {testCaseRallyItem.FormattedID}", Color.Orange);
                            continue;
                        }
                    }
                    else
                    {
                        // Create Test Case in ADO
                        LogToUI($"      ➕ Creating Test Case in ADO: {testCaseRallyItem.FormattedID}", Color.Blue);
                        
                        var (creationFields, postFields) = _fieldMappingService.TransformRallyWorkItemToAdoFieldsSplit(testCaseRallyItem);
                        
                        _loggingService.LogInfo($"📋 Rally Test Case Details for {testCaseRallyItem.FormattedID}:");
                        _loggingService.LogInfo($"   - ObjectID: {testCaseRallyItem.ObjectID}");
                        _loggingService.LogInfo($"   - Type: {testCaseRallyItem.Type}");
                        _loggingService.LogInfo($"   - Name: {testCaseRallyItem.Name}");
                        
                        // Set default Iteration Path
                        if (!creationFields.ContainsKey("System.IterationPath"))
                        {
                            creationFields["System.IterationPath"] = "Acute Meds Management\\Emerson\\Rally Migration";
                            _loggingService.LogDebug($"Set default IterationPath for {testCaseRallyItem.FormattedID}");
                        }
                        
                        // Build rich description
                        try
                        {
                            var richAssembler = new RichContentAssembler();
                            var richHtml = richAssembler.BuildUnifiedDescription(testCaseRallyItem);
                            if (!string.IsNullOrWhiteSpace(richHtml))
                            {
                                creationFields["System.Description"] = richHtml;
                                if (postFields.ContainsKey("System.Description")) 
                                    postFields.Remove("System.Description");
                            }
                        }
                        catch (Exception rcEx)
                        {
                            _loggingService.LogWarning($"Rich content assembly failed for Test Case {testCaseRallyItem.FormattedID}: {rcEx.Message}");
                        }
                        
                        // Validate AssignedTo user
                        await ValidateUserAndUpdateFieldsAsync(settings, creationFields, testCaseRallyItem.Owner, " for Test Case");

                        var createdTestCase = await _adoService.CreateWorkItemWithFallbackAsync(settings, creationFields);
                        
                        if (createdTestCase != null)
                        {
                            testCaseAdoWorkItemId = createdTestCase.Id;
                            progress.RallyToAdoIdMap[testCaseObjectId] = testCaseAdoWorkItemId;
                            migratedCount++;
                            
                            LogToUI($"      ✅ Created Test Case: ADO ID {testCaseAdoWorkItemId}", Color.Green);

                            // Apply test steps if present
                            if (testCaseRallyItem.Steps != null && testCaseRallyItem.Steps.Any())
                            {
                                LogToUI($"         📝 Adding {testCaseRallyItem.Steps.Count} test steps", Color.Blue);
                                
                                // Log each step for debugging
                                for (int i = 0; i < testCaseRallyItem.Steps.Count; i++)
                                {
                                    var step = testCaseRallyItem.Steps[i];
                                    _loggingService.LogDebug($"Step {i + 1}: Index={step.StepIndex}");
                                    _loggingService.LogDebug($"  Input: {step.Input?.Substring(0, Math.Min(100, step.Input?.Length ?? 0))}");
                                    _loggingService.LogDebug($"  Expected: {step.ExpectedResult?.Substring(0, Math.Min(100, step.ExpectedResult?.Length ?? 0))}");
                                }
                                
                                try
                                {
                                    var stepsXml = TestStepsXmlBuilder.BuildTestStepsXml(testCaseRallyItem.Steps);
                                    
                                    // Log the generated XML (first 500 chars)
                                    _loggingService.LogInfo($"Generated Steps XML (preview): {stepsXml?.Substring(0, Math.Min(500, stepsXml?.Length ?? 0))}");
                                    
                                    if (!string.IsNullOrEmpty(stepsXml))
                                    {
                                        var stepsField = new Dictionary<string, object>
                                        {
                                            ["Microsoft.VSTS.TCM.Steps"] = stepsXml
                                        };
                                        
                                        var stepsPatched = await _adoService.PatchWorkItemFieldsAsync(settings, testCaseAdoWorkItemId, stepsField, false);
                                        if (stepsPatched)
                                        {
                                            LogToUI($"         ✅ Added {testCaseRallyItem.Steps.Count} test steps", Color.Green);
                                        }
                                        else
                                        {
                                            LogToUI($"         ⚠️ Failed to add test steps - check ADO API response", Color.Orange);
                                        }
                                    }
                                    else
                                    {
                                        LogToUI($"         ⚠️ Generated XML is empty", Color.Orange);
                                    }
                                }
                                catch (Exception stepsEx)
                                {
                                    _loggingService.LogWarning($"Failed to add test steps for {testCaseRallyItem.FormattedID}: {stepsEx.Message}");
                                    LogToUI($"         ⚠️ Test steps migration error: {stepsEx.Message}", Color.Orange);
                                }
                            }

                            // Apply post-creation fields if any
                            if (postFields.Any())
                            {
                                // Remove unsupported fields
                                postFields.Remove("System.CreatedDate");
                                postFields.Remove("System.ChangedDate");
                                postFields.Remove("System.State");
                                
                                if (postFields.Any())
                                {
                                    LogToUI($"         🔧 Applying post-creation fields: {string.Join(", ", postFields.Keys)}", Color.Blue);
                                    var patchSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, testCaseAdoWorkItemId, postFields, false);
                                    if (!patchSuccess)
                                    {
                                        LogToUI($"         ⚠️ Post-creation field patch failed", Color.Orange);
                                    }
                                }
                            }

                            // Migrate Test Case comments
                            if (testCaseRallyItem.Comments != null && testCaseRallyItem.Comments.Any())
                            {
                                await MigrateCommentsAsync(settings, testCaseAdoWorkItemId, testCaseRallyItem.Comments);
                            }

                            // Migrate Test Case attachments
                            if (testCaseRallyItem.Attachments != null && testCaseRallyItem.Attachments.Any())
                            {
                                await MigrateAttachmentsAsync(settings, testCaseAdoWorkItemId, testCaseRallyItem.Attachments);
                            }
                        }
                        else
                        {
                            LogToUI($"      ❌ Failed to create Test Case: {testCaseRallyItem.FormattedID}", Color.Red);
                            continue;
                        }
                    }

                    // Link Test Case to User Story/Defect using "Tests" relationship
                    var linkSuccess = await _adoService.LinkWorkItemsAsync(settings, workProductAdoId, testCaseAdoWorkItemId, "Tests");
                    
                    if (linkSuccess)
                    {
                        linkedCount++;
                        LogToUI($"      🔗 Linked Test Case {testCaseAdoWorkItemId} (Tests) to work item {workProductAdoId}", Color.Green);
                    }
                    else
                    {
                        LogToUI($"      ⚠️ Failed to link Test Case {testCaseAdoWorkItemId}", Color.Orange);
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to migrate Test Case {testCaseObjectId}", ex);
                    LogToUI($"      ❌ Error migrating Test Case: {ex.Message}", Color.Red);
                }
            }

            if (migratedCount > 0 || linkedCount > 0)
            {
                LogToUI($"   ✅ Test Case migration complete: {migratedCount} created, {linkedCount} linked", Color.Green);
            }
        }

        /// <summary>
        /// Migrate Rally attachments to ADO work item attachments
        /// </summary>
        private async Task MigrateAttachmentsAsync(ConnectionSettings settings, int adoWorkItemId, List<RallyAttachment> attachments)
        {
            if (attachments == null || !attachments.Any())
                return;

            try
            {
                var successCount = 0;

                foreach (var attachment in attachments)
                {
                    try
                    {
                        var success = await _adoService.UploadAttachmentAsync(settings, adoWorkItemId, attachment);
                        if (success)
                        {
                            successCount++;
                            LogToUI($"      ✓ {attachment.Name} ({FormatFileSize(attachment.Size)})", Color.Green);
                        }
                        else
                        {
                            _loggingService.LogWarning($"Failed to upload attachment {attachment.Name} to work item {adoWorkItemId}");
                            LogToUI($"      ✗ {attachment.Name} - upload failed", Color.Orange);
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Error uploading attachment {attachment.Name}: {ex.Message}");
                        LogToUI($"      ✗ {attachment.Name} - {ex.Message}", Color.Orange);
                    }
                }

                if (successCount > 0)
                {
                    LogToUI($"   ✅ Migrated {successCount}/{attachments.Count} attachments", Color.Green);
                }
                else if (attachments.Count > 0)
                {
                    LogToUI($"   ⚠️ Failed to migrate {attachments.Count} attachments", Color.Orange);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Failed to migrate attachments for work item {adoWorkItemId}", ex);
                LogToUI($"   ⚠️ Attachment migration error: {ex.Message}", Color.Orange);
            }
        }

        /// <summary>
        /// Format file size for display
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Quick method to test enhanced migration for a single Rally ID
        /// </summary>
        public async Task<MigrationProgress> TestEnhancedMigrationAsync(string rallyId, ConnectionSettings settings)
        {
            LogToUI($"🧪 Testing enhanced migration for {rallyId}", Color.Blue);
            return await MigrateWithEnhancedFieldsAsync(settings, new List<string> { rallyId }, false);
        }

        /// <summary>
        /// Validates user email with domain fallback: @optum.com first, then @emishealth.com
        /// Returns the validated email or null if user doesn't exist in either domain
        /// </summary>
        private async Task<string> ValidateAndGetUserEmailAsync(ConnectionSettings settings, string originalEmail)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(originalEmail)) return null;
                
                // If already has @, try as-is first
                if (originalEmail.Contains("@"))
                {
                    var exists = await _adoService.FindUserByEmailAsync(settings, originalEmail);
                    if (exists) return originalEmail;
                    
                    // Extract username part before @
                    var username = originalEmail.Split('@')[0];
                    originalEmail = username;
                }
                
                // Try @optum.com first (priority domain)
                var optumEmail = $"{originalEmail}@optum.com";
                var optumExists = await _adoService.FindUserByEmailAsync(settings, optumEmail);
                if (optumExists)
                {
                    _loggingService.LogInfo($"✅ User found with @optum.com domain: {optumEmail}");
                    return optumEmail;
                }
                
                // Fallback to @emishealth.com
                var emishealthEmail = $"{originalEmail}@emishealth.com";
                var emishealthExists = await _adoService.FindUserByEmailAsync(settings, emishealthEmail);
                if (emishealthExists)
                {
                    _loggingService.LogInfo($"✅ User found with @emishealth.com domain: {emishealthEmail}");
                    return emishealthEmail;
                }
                
                // User not found in either domain
                return null;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error validating user email: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Enhanced user validation that adds Rally username to Tags when user is not found in ADO
        /// and assigns to migration account
        /// </summary>
        private async Task<bool> ValidateUserAndUpdateFieldsAsync(
            ConnectionSettings settings, 
            Dictionary<string, object> creationFields, 
            string rallyUsername, 
            string logContext = "")
        {
            try
            {
                // Debug logging to track what we're working with
                _loggingService.LogDebug($"ValidateUserAndUpdateFieldsAsync called{logContext} - Rally username: '{rallyUsername}'");
                
                if (!creationFields.ContainsKey("System.AssignedTo"))
                {
                    _loggingService.LogDebug($"No System.AssignedTo field found{logContext}");
                    return true; // No assignment to validate
                }

                var assignedToEmail = creationFields["System.AssignedTo"]?.ToString();
                if (string.IsNullOrWhiteSpace(assignedToEmail))
                {
                    _loggingService.LogDebug($"System.AssignedTo field is empty{logContext}");
                    return true; // No user to validate
                }

                _loggingService.LogDebug($"Validating user '{assignedToEmail}'{logContext}");
                var validatedEmail = await ValidateAndGetUserEmailAsync(settings, assignedToEmail);
                if (validatedEmail != null)
                {
                    // User found - use validated email, no Rally user tag needed
                    creationFields["System.AssignedTo"] = validatedEmail;
                    _loggingService.LogDebug($"User '{validatedEmail}' validated in ADO{logContext}");
                    _loggingService.LogDebug($"Rally username '{rallyUsername}' will be added to Tags (tracked automatically)");
                    
                    return true;
                }
                else
                {
                    // User not found - assign to migration account and add Rally username to tags
                    _loggingService.LogWarning($"User '{assignedToEmail}' not found in ADO (tried @optum.com and @emishealth.com){logContext}");
                    LogToUI($"   ⚠️ User '{assignedToEmail}' not found (tried both domains), assigning to migration account{logContext}", Color.Orange);
                    
                    // Assign to migration account (assuming it's the current user running the migration)
                    var migrationAccount = await GetMigrationAccountEmailAsync(settings);
                    if (!string.IsNullOrEmpty(migrationAccount))
                    {
                        creationFields["System.AssignedTo"] = migrationAccount;
                        _loggingService.LogInfo($"Assigned to migration account: {migrationAccount}");
                        LogToUI($"   👤 Assigned to migration account: {migrationAccount}", Color.Cyan);
                    }
                    else
                    {
                        // Keep the original assignment if migration account can't be determined
                        // The FieldTransformationService should have already assigned to migration user during transformation
                        _loggingService.LogWarning($"Could not get migration account from settings, keeping existing assignment: {assignedToEmail}");
                        LogToUI($"   ⚠️ Using existing assignment: {assignedToEmail} (migration account not available)", Color.Orange);
                    }

                    // Rally username tagging - automatically handled by FieldTransformationService
                    if (!string.IsNullOrWhiteSpace(rallyUsername))
                    {
                        _loggingService.LogInfo($"Rally user '{rallyUsername}' automatically tracked for tagging (user not found in ADO)");
                        LogToUI($"   🏷️ Rally user '{rallyUsername}' will be added to Tags (user not found in ADO)", Color.Cyan);
                    }
                    else
                    {
                        _loggingService.LogWarning($"Rally username is null/empty, cannot add to Tags{logContext}");
                        LogToUI($"   ⚠️ Rally username is empty, cannot add to Tags", Color.Orange);
                    }
                    
                    return false; // User not found but handled gracefully
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error in enhanced user validation{logContext}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds Rally username to the Tags field, preserving existing tags
        /// </summary>
        private void AddRallyUserToTags(Dictionary<string, object> fields, string rallyUsername)
        {
            var rallyUserTag = $"RallyUser-{rallyUsername}";
            _loggingService.LogInfo($"🏷️ ADDING RALLY USER TAG: {rallyUserTag}");
            // Extract just the username part if it's an email
            var cleanUsername = rallyUsername.Contains("@") ? rallyUsername.Split('@')[0] : rallyUsername;
            var finalTag = $"RallyUser-{cleanUsername}";
            
            _loggingService.LogDebug($"Adding Rally user tag: {finalTag} (from original: {rallyUsername})");
            
            if (fields.TryGetValue("System.Tags", out var existingTags))
            {
                var tagStr = existingTags.ToString();
                _loggingService.LogDebug($"Existing tags: '{tagStr}'");
                
                var tagList = new List<string>(tagStr.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries));
                
                // Only add if not already present
                if (!tagList.Contains(finalTag))
                {
                    tagList.Add(finalTag);
                    var newTagString = string.Join(";", tagList);  
                    fields["System.Tags"] = newTagString;
                    _loggingService.LogInfo($"✅ RALLY USER TAG ADDED! Updated tags: '{newTagString}'");
                }
                else
                {
                    _loggingService.LogDebug($"Rally user tag '{finalTag}' already exists in tags");
                }
            }
            else
            {
                fields["System.Tags"] = finalTag;
                _loggingService.LogInfo($"✅ RALLY USER TAG CREATED! New tags field: '{finalTag}'");
            }
        }
        
        private void LogSummary(MigrationProgress progress)
        {
            LogToUI("", Color.Black);
            LogToUI("🎉 ENHANCED MIGRATION SUMMARY", Color.Blue);
            LogToUI("============================", Color.Blue);
            LogToUI($"📊 Total Items: {progress.TotalItems}", Color.Blue);
            LogToUI($"✅ Successful: {progress.SuccessfulItems}", Color.Green);
            LogToUI($"❌ Failed: {progress.FailedItems}", progress.FailedItems > 0 ? Color.Red : Color.Blue);
            LogToUI($"⏭️ Skipped: {progress.SkippedItems}", progress.SkippedItems > 0 ? Color.Orange : Color.Blue);
            if (progress.SuccessfulItems > 0)
            {
                LogToUI("", Color.Black);
                LogToUI("🌟 ENHANCED FEATURES APPLIED:", Color.Green);
                LogToUI("• Split creation/post historical fields", Color.Green);
                LogToUI("• Checkpoint & resume support", Color.Green);
                LogToUI("• Dry-run & difference patching", Color.Green);
                LogToUI("• Historical field patch with bypass rules option", Color.Green);
            }
        }

        /// <summary>
        /// Helper method to log to both file and UI
        /// </summary>
        private void LogToUI(string message, Color color)
        {
            // Log to file
            if (color == Color.Red)
                _loggingService.LogError(message);
            else if (color == Color.Orange)
                _loggingService.LogWarning(message);
            else
                _loggingService.LogInfo(message);
            
            // Log to UI if available
            _uiLogger?.Invoke(message, color);
        }

        private string GetCheckpointPath() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rally to ADO Migration", "checkpoint.json");
        private void PersistCheckpoint(MigrationProgress progress)
        {
            var path = GetCheckpointPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var payload = new
            {
                progress.LastCheckpointIndex,
                progress.RallyToAdoIdMap,
                Timestamp = DateTime.UtcNow
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }
        public Dictionary<string,int> LoadExistingMappings()
        {
            var path = GetCheckpointPath();
            if (!System.IO.File.Exists(path)) return new Dictionary<string,int>();
            try
            {
                var text = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                var obj = Newtonsoft.Json.Linq.JObject.Parse(text);
                var mapObj = obj["RallyToAdoIdMap"] as Newtonsoft.Json.Linq.JObject;
                var dict = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                if (mapObj != null)
                {
                    foreach (var prop in mapObj.Properties())
                    {
                        dict[prop.Name] = prop.Value.ToObject<int>();
                    }
                }
                return dict;
            }
            catch { return new Dictionary<string,int>(); }
        }

        /// <summary>
        /// Final safety check to ensure Rally username tagging when user is not found
        /// This catches users that may have been transformed by field mapping but are still invalid
        /// </summary>
        private async Task FinalUserValidationAndTagging(
            ConnectionSettings settings, 
            Dictionary<string, object> creationFields, 
            string rallyUsername)
        {
            try
            {
                if (!creationFields.ContainsKey("System.AssignedTo") || string.IsNullOrWhiteSpace(rallyUsername))
                    return;

                var assignedToEmail = creationFields["System.AssignedTo"]?.ToString();
                if (string.IsNullOrWhiteSpace(assignedToEmail))
                    return;

                _loggingService.LogDebug($"Final user validation check for '{assignedToEmail}' (Rally user: '{rallyUsername}')");

                // Double-check if this user actually exists in ADO
                var userExists = await _adoService.FindUserByEmailAsync(settings, assignedToEmail);
                if (!userExists)
                {
                    _loggingService.LogWarning($"Final validation: User '{assignedToEmail}' not found in ADO - adding Rally user tag and assigning to migration account");
                    LogToUI($"   🔍 Final check: User '{assignedToEmail}' not found, applying fallback logic", Color.Orange);

                    // Get migration account 
                    var migrationAccount = await GetMigrationAccountEmailAsync(settings);
                    if (!string.IsNullOrEmpty(migrationAccount))
                    {
                        creationFields["System.AssignedTo"] = migrationAccount;
                        LogToUI($"   👤 Assigned to migration account: {migrationAccount}", Color.Cyan);
                    }
                    else
                    {
                        creationFields.Remove("System.AssignedTo");
                        LogToUI($"   ❌ Could not determine migration account, removing assignment", Color.Orange);
                    }

                    // Add Rally username to Tags for traceability
                    AddRallyUserToTags(creationFields, rallyUsername);
                    LogToUI($"   🏷️ Added Rally user '{rallyUsername}' to Tags", Color.Green);
                    _loggingService.LogInfo($"Added Rally user '{rallyUsername}' to Tags via final validation");
                }
                else
                {
                    _loggingService.LogDebug($"Final validation: User '{assignedToEmail}' confirmed to exist in ADO");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error in final user validation: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the migration account email to assign work items when Rally user is not found in ADO.
        /// Uses the configured migration user from FieldTransformationService.
        /// </summary>
        private async Task<string> GetMigrationAccountEmailAsync(ConnectionSettings settings)
        {
            try
            {
                // Option 1: Get migration user from field mapping service (already configured)
                var configuredMigrationUser = _fieldMappingService.GetMigrationUserEmail();
                if (!string.IsNullOrEmpty(configuredMigrationUser))
                {
                    _loggingService.LogInfo($"Using configured migration account: {configuredMigrationUser}");
                    return configuredMigrationUser;
                }
                
                // Option 2: Try to get current user from ADO API as fallback
                try 
                {
                    var currentUser = await _adoService.GetCurrentUserAsync(settings);
                    if (!string.IsNullOrEmpty(currentUser))
                    {
                        _loggingService.LogInfo($"Using current ADO user as migration account: {currentUser}");
                        return currentUser;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogDebug($"Could not get current user from ADO API: {ex.Message}");
                }
                
                _loggingService.LogWarning("Could not determine migration account - no configured migration user and current user unavailable");
                LogToUI("   ⚠️ Migration account not configured. Please set migration user in configuration.", Color.Orange);
                return null;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error getting migration account: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _rally_service_dispose();
        }
        private void _rally_service_dispose()
        {
            _rallyService?.Dispose();
            _adoService?.Dispose();
        }
    }
}