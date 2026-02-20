using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public class MigrationService
    {
        private readonly RallyApiService _rallyService;
        private readonly AdoApiService _adoService;
        private readonly JsonBasedFieldMappingService _mappingService; // ? CHANGED: Use JSON-based mapping
        private readonly LoggingService _loggingService;
        private readonly string _organizationUrl;
        private readonly string _projectName;
        private readonly string _personalAccessToken;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isPaused;

        public event EventHandler<MigrationProgress> ProgressUpdated;
        public event EventHandler<string> StatusUpdated;

        public MigrationService(LoggingService loggingService)
        {
            _loggingService = loggingService;
            _rallyService = new RallyApiService(_loggingService);
            _adoService = new AdoApiService(_loggingService, _organizationUrl, _projectName, _personalAccessToken);
            _mappingService = new JsonBasedFieldMappingService(_loggingService);
            
            // ? Load mapping configuration on initialization
            var configLoaded = _mappingService.LoadMappingConfiguration();
            if (!configLoaded)
            {
                _loggingService.LogWarning("Field mapping configuration not loaded");
                _loggingService.LogInfo("Configuration will be generated dynamically from Rally and ADO APIs during first migration");
            }
            else
            {
                _loggingService.LogInfo("? Field mapping configuration loaded successfully");
            }
        }

        /// <summary>
        /// Generate field mapping configuration dynamically from Rally and ADO APIs
        /// This is called automatically if FieldMappingConfiguration.json doesn't exist
        /// NOTE: Currently disabled - create FieldMappingConfiguration.json manually (see FINAL_IMPLEMENTATION_STATUS.md)
        /// </summary>
        public async Task<bool> GenerateDynamicFieldMappingAsync(ConnectionSettings settings, string sampleRallyId = null)
        {
            try
            {
                _loggingService.LogInfo("Dynamic field mapping generation requested...");
                _loggingService.LogWarning("Dynamic generation not available - please create FieldMappingConfiguration.json manually");
                _loggingService.LogInfo("See FINAL_IMPLEMENTATION_STATUS.md for JSON template");
                
                StatusUpdated?.Invoke(this, "Dynamic mapping generation not available - please create JSON manually");
                
                // TODO: Uncomment when CompleteDynamicMappingGenerator is implemented
                // var generator = new CompleteDynamicMappingGenerator(_loggingService);
                // var jsonPath = await generator.GenerateCompleteMappingFromApisAsync(settings, sampleRallyId);
                //
                // if (!string.IsNullOrEmpty(jsonPath) && System.IO.File.Exists(jsonPath))
                // {
                //     _loggingService.LogInfo($"? Field mapping configuration generated: {jsonPath}");
                //     StatusUpdated?.Invoke(this, "Field mappings generated successfully!");
                //
                //     // Reload the mapping service with new configuration
                //     var loaded = _mappingService.LoadMappingConfiguration(jsonPath);
                //     if (loaded)
                //     {
                //         _loggingService.LogInfo("? New configuration loaded into mapping service");
                //         return true;
                //     }
                // }

                _loggingService.LogError("Field mapping configuration must be created manually");
                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Dynamic field mapping generation failed", ex);
                StatusUpdated?.Invoke(this, $"Field mapping generation failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TestConnectionsAsync(ConnectionSettings settings)
        {
            try
            {
                StatusUpdated?.Invoke(this, "Testing Rally connection...");
                
                // First try the standard connection test
                var rallyTest = await _rallyService.TestConnectionAsync(settings);
                
                if (!rallyTest)
                {
                    StatusUpdated?.Invoke(this, "Standard Rally connection failed - testing authentication methods...");
                    
                    // If standard test fails, try different authentication methods
                    var authTestResults = await _rallyService.TestAuthenticationMethodsAsync(settings);
                    _loggingService.LogInfo("Rally Authentication Testing Results:");
                    _loggingService.LogInfo(authTestResults);
                    
                    // Run full diagnostics
                    var rallyDiagnostics = await _rallyService.DiagnoseConnectionAsync(settings);
                    _loggingService.LogInfo("Rally Connection Diagnostics:");
                    _loggingService.LogInfo(rallyDiagnostics);
                    
                    StatusUpdated?.Invoke(this, "Rally connection failed - check logs for authentication details");
                    _loggingService.LogError("Rally connection test failed after trying multiple authentication methods");
                    return false;
                }

                StatusUpdated?.Invoke(this, "Testing ADO connection...");
                var adoTest = await _adoService.TestConnectionAsync(settings);
                
                if (!adoTest)
                {
                    StatusUpdated?.Invoke(this, "ADO connection failed - running diagnostics...");
                    
                    // Run full ADO diagnostics
                    var adoDiagnostics = await _adoService.DiagnoseConnectionAsync(settings);
                    _loggingService.LogInfo("ADO Connection Diagnostics:");
                    _loggingService.LogInfo(adoDiagnostics);
                    
                    StatusUpdated?.Invoke(this, "ADO connection failed - check logs for details");
                    _loggingService.LogError("ADO connection test failed - check diagnostic output above");
                    return false;
                }

                StatusUpdated?.Invoke(this, "Connection tests successful");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Connection test failed", ex);
                StatusUpdated?.Invoke(this, $"Connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task<MigrationProgress> StartMigrationAsync(ConnectionSettings settings)
        {
            var progress = new MigrationProgress();
            _cancellationTokenSource = new CancellationTokenSource();
            _isPaused = false;

            try
            {
                _loggingService.LogInfo("Starting Rally to ADO migration");
                StatusUpdated?.Invoke(this, "Starting migration...");

                // Step 1: Fetch all Rally work items
                StatusUpdated?.Invoke(this, "Fetching work items from Rally...");
                var progressReporter = new Progress<string>(status => StatusUpdated?.Invoke(this, status));
                var rallyWorkItems = await _rallyService.GetAllWorkItemsAsync(settings, progressReporter);

                progress.TotalItems = rallyWorkItems.Count;
                _loggingService.LogInfo($"Retrieved {rallyWorkItems.Count} work items from Rally");

                if (rallyWorkItems.Count == 0)
                {
                    StatusUpdated?.Invoke(this, "No work items found in Rally");
                    progress.IsCompleted = true;
                    progress.EndTime = DateTime.Now;
                    return progress;
                }

                // Step 2: Process work items in batches
                StatusUpdated?.Invoke(this, "Processing work items...");
                await ProcessWorkItemsInBatches(settings, rallyWorkItems, progress);

                progress.IsCompleted = true;
                progress.EndTime = DateTime.Now;
                
                // Step 3: Generate reports
                await GenerateReports(progress);

                _loggingService.LogInfo($"Migration completed. Success: {progress.SuccessfulItems}, Failed: {progress.FailedItems}, Skipped: {progress.SkippedItems}");
                StatusUpdated?.Invoke(this, $"Migration completed successfully. {progress.SuccessfulItems}/{progress.TotalItems} items migrated.");

                return progress;
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogInfo("Migration was cancelled");
                StatusUpdated?.Invoke(this, "Migration cancelled");
                progress.EndTime = DateTime.Now;
                return progress;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Migration failed", ex);
                StatusUpdated?.Invoke(this, $"Migration failed: {ex.Message}");
                progress.EndTime = DateTime.Now;
                throw;
            }
        }

        private async Task ProcessWorkItemsInBatches(ConnectionSettings settings, List<RallyWorkItem> rallyWorkItems, MigrationProgress progress)
        {
            // Use settings for batch size and concurrency when available
            int batchSize = settings?.BatchSize > 0 ? settings.BatchSize : 100;
            int maxConcurrency = settings?.MaxConcurrency > 0 ? settings.MaxConcurrency : 8;
            
            var batches = CreateBatches(rallyWorkItems, batchSize);
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            _loggingService.LogInfo($"Processing {rallyWorkItems.Count} work items in {batches.Count} batches (size: {batchSize}, concurrency: {maxConcurrency})");

            // Pre-check which items already exist in ADO for better performance
            StatusUpdated?.Invoke(this, "Checking for existing work items...");
            var rallyObjectIds = rallyWorkItems.Select(wi => wi.ObjectID).ToList();
            var existenceResults = await _adoService.CheckWorkItemsExistBatchAsync(settings, rallyObjectIds, maxConcurrency);
            
            var existingCount = existenceResults.Count(r => r.Value);
            _loggingService.LogInfo($"Pre-check complete: {existingCount} items already exist in ADO");
            StatusUpdated?.Invoke(this, $"Found {existingCount} existing items, processing {rallyWorkItems.Count - existingCount} new items...");

            foreach (var batch in batches)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Handle pause functionality
                while (_isPaused && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(500, _cancellationTokenSource.Token);
                }

                // Process batch with controlled concurrency
                var batchTasks = batch.Select(async item =>
                {
                    await semaphore.WaitAsync(_cancellationTokenSource.Token);
                    try
                    {
                        // Use pre-checked existence results
                        var alreadyExists = existenceResults.ContainsKey(item.ObjectID) && existenceResults[item.ObjectID];
                        await ProcessSingleWorkItemOptimizedAsync(settings, item, progress, alreadyExists);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(batchTasks);
                ProgressUpdated?.Invoke(this, progress);

                // Small delay between batches to prevent API rate limiting
                if (batches.IndexOf(batch) < batches.Count - 1) // Not the last batch
                {
                    await Task.Delay(100, _cancellationTokenSource.Token);
                }
            }
        }

        private async Task ProcessSingleWorkItemAsync(ConnectionSettings settings, RallyWorkItem rallyItem, MigrationProgress progress)
        {
            await ProcessSingleWorkItemOptimizedAsync(settings, rallyItem, progress, null);
        }

        private async Task ProcessSingleWorkItemOptimizedAsync(ConnectionSettings settings, RallyWorkItem rallyItem, MigrationProgress progress, bool? alreadyExists = null)
        {
            var result = new MigrationResult
            {
                RallyId = rallyItem.ObjectID,
                RallyFormattedId = rallyItem.FormattedID,
                ProcessedAt = DateTime.Now
            };

            try
            {
                // Use pre-checked existence result if available, otherwise check now
                bool exists;
                if (alreadyExists.HasValue)
                {
                    exists = alreadyExists.Value;
                }
                else
                {
                    exists = await _adoService.WorkItemExistsAsync(settings, rallyItem.ObjectID);
                }

                if (exists)
                {
                    result.IsSkipped = true;
                    result.SkipReason = "Work item already exists in ADO";
                    lock (progress)
                    {
                        progress.SkippedItems++;
                        progress.ProcessedItems++; // CRITICAL FIX: Increment here so skipped items update progress immediately
                    }
                    _loggingService.LogDebug($"Skipped {rallyItem.FormattedID} - already exists");
                    
                    // Notify UI immediately for skipped items
                    // Note: This will be called from within Task.WhenAll, which is thread-safe
                    ProgressUpdated?.Invoke(this, progress);
                }
                else
                {
                    // Ensure mapping configuration is loaded
                    if (!_mappingService.LoadMappingConfiguration())
                    {
                        throw new InvalidOperationException("Field mapping configuration could not be loaded. Please ensure DefaultFieldMappingConfiguration.json exists in the Config directory.");
                    }

                    // Transform Rally work item to ADO fields
                    var adoFields = _mappingService.TransformRallyWorkItemToAdoFields(rallyItem);
                    
                    if (adoFields == null || !adoFields.Any())
                    {
                        throw new InvalidOperationException($"No fields were mapped for Rally item {rallyItem.FormattedID}. Please check field mapping configuration.");
                    }

                    // Create work item in ADO with identity fallback
                    var createdItem = await _adoService.CreateWorkItemWithFallbackAsync(settings, adoFields);
                    
                    if (createdItem == null)
                    {
                        throw new InvalidOperationException($"Failed to create ADO work item for {rallyItem.FormattedID}");
                    }

                    result.AdoId = createdItem.Id;
                    result.Success = true;
                    lock (progress)
                    {
                        progress.SuccessfulItems++;
                        progress.ProcessedItems++; // CRITICAL FIX: Increment here so created items update progress immediately
                    }
                    
                    // Notify UI immediately for created items
                    ProgressUpdated?.Invoke(this, progress);

                    // Queue secondary operations for batch processing (attachments, comments, children)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Process attachments, comments, and children in parallel
                            var attachmentTask = MigrateAttachmentsAsync(settings, createdItem.Id, rallyItem.Attachments);
                            var commentTask = MigrateCommentsAsync(settings, createdItem.Id, rallyItem.Comments);
                            var childrenTask = Task.CompletedTask;

                            // Migrate child tasks/work items (Rally children ? ADO linked child items)
                            if (rallyItem.Children != null && rallyItem.Children.Any())
                            {
                                _loggingService.LogDebug($"Found {rallyItem.Children.Count} children for Rally item {rallyItem.FormattedID}");
                                childrenTask = MigrateChildWorkItemsAsync(settings, createdItem.Id, rallyItem.Children);
                            }

                            await Task.WhenAll(attachmentTask, commentTask, childrenTask);
                            _loggingService.LogDebug($"Successfully migrated {rallyItem.FormattedID} to ADO work item {createdItem.Id}");
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogWarning($"Secondary migration operations failed for {rallyItem.FormattedID}: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                lock (progress)
                {
                    progress.FailedItems++;
                    progress.ProcessedItems++; // CRITICAL FIX: Increment here so failed items update progress immediately
                }
                _loggingService.LogError($"Failed to migrate {rallyItem.FormattedID}", ex);
                
                // Notify UI immediately for failed items
                ProgressUpdated?.Invoke(this, progress);
            }

            // Thread-safe add to results
            lock (progress.Results)
            {
                progress.Results.Add(result);
            }
        }

        private async Task MigrateAttachmentsAsync(ConnectionSettings settings, int adoWorkItemId, List<RallyAttachment> attachments)
        {
            if (attachments == null || !attachments.Any())
                return;

            // Process attachments concurrently with limited parallelism
            const int maxAttachmentConcurrency = 3;
            var semaphore = new SemaphoreSlim(maxAttachmentConcurrency, maxAttachmentConcurrency);

            var attachmentTasks = attachments.Select(async attachment =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var success = await _adoService.UploadAttachmentAsync(settings, adoWorkItemId, attachment);
                    if (!success)
                    {
                        _loggingService.LogWarning($"Failed to upload attachment {attachment.Name} to work item {adoWorkItemId}");
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Error uploading attachment {attachment.Name}: {ex.Message}");
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(attachmentTasks);
            var successCount = results.Count(r => r);
            
            if (successCount > 0)
            {
                _loggingService.LogDebug($"Uploaded {successCount}/{attachments.Count} attachments for work item {adoWorkItemId}");
            }
        }

        private async Task MigrateCommentsAsync(ConnectionSettings settings, int adoWorkItemId, List<RallyComment> comments)
        {
            if (comments == null || !comments.Any())
                return;

            // Process comments sequentially to maintain chronological order, but with faster timeout
            var orderedComments = comments.OrderBy(c => c.CreationDate).ToList();
            var successCount = 0;

            foreach (var comment in orderedComments)
            {
                try
                {
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
                _loggingService.LogDebug($"Added {successCount}/{orderedComments.Count} comments for work item {adoWorkItemId}");
            }
        }

        private async Task MigrateChildWorkItemsAsync(ConnectionSettings settings, int parentAdoId, List<string> childrenIds, HashSet<string> processedIds = null)
        {
            if (childrenIds == null || !childrenIds.Any())
                return;

            // Initialize processed IDs tracking to prevent infinite loops
            if (processedIds == null)
                processedIds = new HashSet<string>();

            _loggingService.LogInfo($"Migrating {childrenIds.Count} child work items for parent {parentAdoId}");

            foreach (var childId in childrenIds)
            {
                try
                {
                    // Skip if we've already processed this item (prevent infinite loops)
                    if (processedIds.Contains(childId))
                    {
                        _loggingService.LogInfo($"Child work item {childId} already processed, skipping to prevent loop");
                        continue;
                    }
                    
                    processedIds.Add(childId);

                    // Get the Rally child work item
                    var childRallyItem = await _rallyService.GetWorkItemByIdAsync(settings, childId);
                    if (childRallyItem == null)
                    {
                        _loggingService.LogWarning($"Child Rally work item {childId} not found, skipping");
                        continue;
                    }

                    // Check if child already exists in ADO
                    var childExists = await _adoService.WorkItemExistsAsync(settings, childRallyItem.ObjectID);
                    if (childExists)
                    {
                        _loggingService.LogInfo($"Child work item {childRallyItem.FormattedID} already exists in ADO, skipping creation but will link");
                        // TODO: Add logic to find existing work item ID and create link
                        continue;
                    }

                    // Transform child Rally work item to ADO fields
                    var childAdoFields = _mappingService.TransformRallyWorkItemToAdoFields(childRallyItem);

                    // Create child work item in ADO
                    var createdChild = await _adoService.CreateWorkItemWithFallbackAsync(settings, childAdoFields);

                    if (createdChild != null)
                    {
                        // Create parent-child link in ADO
                        var linkSuccess = await _adoService.LinkWorkItemsAsync(settings, parentAdoId, createdChild.Id, "Child");
                        
                        if (linkSuccess)
                        {
                            _loggingService.LogInfo($"Successfully migrated and linked child work item {childRallyItem.FormattedID} ? ADO {createdChild.Id}");
                        }
                        else
                        {
                            _loggingService.LogWarning($"Child work item {childRallyItem.FormattedID} created but linking failed");
                        }

                        // Recursively migrate nested children if any (with loop prevention)
                        if (childRallyItem.Children != null && childRallyItem.Children.Any())
                        {
                            await MigrateChildWorkItemsAsync(settings, createdChild.Id, childRallyItem.Children, processedIds);
                        }

                        // Migrate child's attachments and comments
                        await MigrateAttachmentsAsync(settings, createdChild.Id, childRallyItem.Attachments);
                        await MigrateCommentsAsync(settings, createdChild.Id, childRallyItem.Comments);
                    }
                    else
                    {
                        _loggingService.LogError($"Failed to create child work item for Rally {childRallyItem.FormattedID}");
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to migrate child work item {childId}: {ex.Message}");
                }
            }
        }

        private List<List<T>> CreateBatches<T>(List<T> source, int batchSize)
        {
            var batches = new List<List<T>>();
            
            for (int i = 0; i < source.Count; i += batchSize)
            {
                var batch = source.Skip(i).Take(batchSize).ToList();
                batches.Add(batch);
            }
            
            return batches;
        }

        private async Task GenerateReports(MigrationProgress progress)
        {
            try
            {
                StatusUpdated?.Invoke(this, "Generating reports...");

                // Export failed items
                var failedItems = progress.Results.Where(r => !r.Success && !r.IsSkipped).ToList();
                if (failedItems.Any())
                {
                    _loggingService.ExportFailedItems(failedItems);
                }

                // Export skipped items
                var skippedItems = progress.Results.Where(r => r.IsSkipped).ToList();
                if (skippedItems.Any())
                {
                    _loggingService.ExportSkippedItems(skippedItems);
                }

                // Export migration summary
                _loggingService.ExportMigrationSummary(progress);

                StatusUpdated?.Invoke(this, "Reports generated successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to generate reports", ex);
                StatusUpdated?.Invoke(this, "Warning: Failed to generate some reports");
            }
        }

        public void PauseMigration()
        {
            _isPaused = true;
            _loggingService.LogInfo("Migration paused");
            StatusUpdated?.Invoke(this, "Migration paused");
        }

        public void ResumeMigration()
        {
            _isPaused = false;
            _loggingService.LogInfo("Migration resumed");
            StatusUpdated?.Invoke(this, "Migration resumed");
        }

        public void CancelMigration()
        {
            _cancellationTokenSource?.Cancel();
            _loggingService.LogInfo("Migration cancellation requested");
            StatusUpdated?.Invoke(this, "Cancelling migration...");
        }

        /// <summary>
        /// Validates Rally IDs and checks if they exist in Rally system
        /// </summary>
        public async Task<RallyIdValidationResult> ValidateRallyIdsAsync(ConnectionSettings settings, List<string> rallyIds, bool includeParents)
        {
            var result = new RallyIdValidationResult();
            
            try
            {
                StatusUpdated?.Invoke(this, "Validating Rally IDs...");
                _loggingService.LogInfo($"Validating {rallyIds.Count} Rally IDs");

                foreach (var rallyId in rallyIds)
                {
                    try
                    {
                        // Check if item exists in Rally
                        var rallyItem = await _rallyService.GetWorkItemByIdAsync(settings, rallyId);
                        
                        if (rallyItem != null)
                        {
                            // Check if already migrated to ADO
                            var existsInAdo = await _adoService.WorkItemExistsAsync(settings, rallyItem.ObjectID);
                            
                            if (existsInAdo)
                            {
                                result.AlreadyMigrated.Add(rallyId);
                                _loggingService.LogInfo($"Rally ID {rallyId} already migrated to ADO");
                            }
                            else
                            {
                                result.ValidIds.Add(rallyId);
                                result.ValidatedItems[rallyId] = rallyItem;
                                _loggingService.LogInfo($"Rally ID {rallyId} is valid and ready for migration");

                                // Find parent items if requested
                                if (includeParents && !string.IsNullOrEmpty(rallyItem.Parent))
                                {
                                    await FindParentItemsAsync(settings, rallyItem.Parent, result);
                                }
                            }
                        }
                        else
                        {
                            result.InvalidIds.Add(rallyId);
                            _loggingService.LogWarning($"Rally ID {rallyId} not found in Rally");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.InvalidIds.Add(rallyId);
                        _loggingService.LogError($"Error validating Rally ID {rallyId}", ex);
                    }
                }

                StatusUpdated?.Invoke(this, $"Validation complete. Valid: {result.ValidIds.Count}, Invalid: {result.InvalidIds.Count}, Already migrated: {result.AlreadyMigrated.Count}");
                return result;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Rally ID validation failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Migrates selected Rally work items with retry logic and connection resilience
        /// </summary>
        public async Task<MigrationProgress> MigrateSelectedItemsAsync(ConnectionSettings settings, List<string> rallyIds, bool includeParents)
        {
            var progress = new MigrationProgress();
            _cancellationTokenSource = new CancellationTokenSource();
            _isPaused = false;

            try
            {
                _loggingService.LogInfo($"Starting selective migration of {rallyIds.Count} Rally items");
                StatusUpdated?.Invoke(this, "Starting selective migration...");

                // ? NEW: Check if field mapping configuration exists, generate if needed
                if (!_mappingService.LoadMappingConfiguration())
                {
                    _loggingService.LogWarning("Field mapping configuration not found!");
                    _loggingService.LogInfo("Please create FieldMappingConfiguration.json in bin\\Debug\\ folder");
                    _loggingService.LogInfo("See FINAL_IMPLEMENTATION_STATUS.md for complete JSON template");
                    
                    StatusUpdated?.Invoke(this, "Missing FieldMappingConfiguration.json - please create it manually");
                    
                    // Attempt dynamic generation (currently disabled)
                    var generated = await GenerateDynamicFieldMappingAsync(settings, rallyIds.FirstOrDefault());
                    if (!generated)
                    {
                        throw new InvalidOperationException(
                            "Field mapping configuration not found. Please create FieldMappingConfiguration.json manually. " +
                            "See FINAL_IMPLEMENTATION_STATUS.md for template.");
                    }
                }

                // First validate all IDs
                var validationResult = await ValidateRallyIdsAsync(settings, rallyIds, includeParents);
                
                // Combine valid items and parent items
                var itemsToMigrate = new List<RallyWorkItem>();
                
                // Add validated items
                foreach (var validId in validationResult.ValidIds)
                {
                    if (validationResult.ValidatedItems.ContainsKey(validId))
                    {
                        itemsToMigrate.Add(validationResult.ValidatedItems[validId]);
                    }
                }

                // Add parent items if requested
                if (includeParents && validationResult.ParentItemsFound.Any())
                {
                    foreach (var parentId in validationResult.ParentItemsFound)
                    {
                        try
                        {
                            var parentItem = await _rallyService.GetWorkItemByIdAsync(settings, parentId);
                            if (parentItem != null && !itemsToMigrate.Any(i => i.ObjectID == parentItem.ObjectID))
                            {
                                itemsToMigrate.Add(parentItem);
                            }
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogWarning($"Could not retrieve parent item {parentId}: {ex.Message}");
                        }
                    }
                }

                progress.TotalItems = itemsToMigrate.Count;

                if (itemsToMigrate.Count == 0)
                {
                    StatusUpdated?.Invoke(this, "No valid items to migrate");
                    progress.IsCompleted = true;
                    progress.EndTime = DateTime.Now;
                    return progress;
                }

                StatusUpdated?.Invoke(this, $"Migrating {itemsToMigrate.Count} work items...");

                // Process items with retry logic
                await ProcessSelectedWorkItemsAsync(settings, itemsToMigrate, progress);

                progress.IsCompleted = true;
                progress.EndTime = DateTime.Now;

                // Generate reports
                await GenerateReports(progress);

                _loggingService.LogInfo($"Selective migration completed. Success: {progress.SuccessfulItems}, Failed: {progress.FailedItems}, Skipped: {progress.SkippedItems}");
                StatusUpdated?.Invoke(this, $"Selective migration completed. {progress.SuccessfulItems}/{progress.TotalItems} items migrated.");

                return progress;
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogInfo("Selective migration was cancelled");
                StatusUpdated?.Invoke(this, "Selective migration cancelled");
                progress.EndTime = DateTime.Now;
                return progress;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Selective migration failed", ex);
                StatusUpdated?.Invoke(this, $"Selective migration failed: {ex.Message}");
                progress.EndTime = DateTime.Now;
                throw;
            }
        }

        private async Task FindParentItemsAsync(ConnectionSettings settings, string parentId, RallyIdValidationResult result)
        {
            try
            {
                if (string.IsNullOrEmpty(parentId) || result.ParentItemsFound.Contains(parentId))
                    return;

                var parentItem = await _rallyService.GetWorkItemByIdAsync(settings, parentId);
                if (parentItem != null)
                {
                    // Check if parent already exists in ADO
                    var existsInAdo = await _adoService.WorkItemExistsAsync(settings, parentItem.ObjectID);
                    if (!existsInAdo)
                    {
                        result.ParentItemsFound.Add(parentId);
                        _loggingService.LogInfo($"Found parent item {parentId} ({parentItem.FormattedID}) to include in migration");

                        // Recursively find grandparents
                        if (!string.IsNullOrEmpty(parentItem.Parent))
                        {
                            await FindParentItemsAsync(settings, parentItem.Parent, result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error finding parent item {parentId}: {ex.Message}");
            }
        }

        private async Task ProcessSelectedWorkItemsAsync(ConnectionSettings settings, List<RallyWorkItem> workItems, MigrationProgress progress)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 2000;

            foreach (var workItem in workItems)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Handle pause functionality
                while (_isPaused && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(500, _cancellationTokenSource.Token);
                }

                var retryCount = 0;
                var success = false;

                while (!success && retryCount < maxRetries && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessSingleWorkItemWithResilienceAsync(settings, workItem, progress);
                        success = true;
                    }
                    catch (Exception ex) when (IsRetryableException(ex))
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            _loggingService.LogWarning($"Retrying migration of {workItem.FormattedID} (attempt {retryCount + 1}/{maxRetries}): {ex.Message}");
                            StatusUpdated?.Invoke(this, $"Connection lost - retrying migration of {workItem.FormattedID}...");
                            await Task.Delay(retryDelayMs * retryCount, _cancellationTokenSource.Token);
                        }
                        else
                        {
                            _loggingService.LogError($"Failed to migrate {workItem.FormattedID} after {maxRetries} attempts", ex);
                            await ProcessSingleWorkItemAsync(settings, workItem, progress); // Final attempt with error logging
                        }
                    }
                    catch (Exception ex)
                    {
                        // Non-retryable exception
                        _loggingService.LogError($"Non-retryable error migrating {workItem.FormattedID}", ex);
                        await ProcessSingleWorkItemAsync(settings, workItem, progress);
                        success = true; // Don't retry
                    }
                }

                ProgressUpdated?.Invoke(this, progress);
            }
        }

        private async Task ProcessSingleWorkItemWithResilienceAsync(ConnectionSettings settings, RallyWorkItem rallyItem, MigrationProgress progress)
        {
            // Test connections before processing
            var connectionsOk = await TestConnectionsWithRetryAsync(settings);
            if (!connectionsOk)
            {
                throw new InvalidOperationException("Unable to establish stable connections to Rally and ADO");
            }

            await ProcessSingleWorkItemAsync(settings, rallyItem, progress);
        }

        private async Task<bool> TestConnectionsWithRetryAsync(ConnectionSettings settings)
        {
            const int maxConnectionRetries = 5;
            const int connectionRetryDelayMs = 3000;

            for (int i = 0; i < maxConnectionRetries; i++)
            {
                try
                {
                    StatusUpdated?.Invoke(this, $"Testing connections (attempt {i + 1}/{maxConnectionRetries})...");
                    var success = await TestConnectionsAsync(settings);
                    if (success)
                    {
                        if (i > 0 // Only log if we had to retry
                        )
                        {
                            StatusUpdated?.Invoke(this, "Connections restored - resuming migration");
                        }
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"Connection test failed (attempt {i + 1}): {ex.Message}");
                }

                if (i < maxConnectionRetries - 1)
                {
                    StatusUpdated?.Invoke(this, $"Connection lost - retrying in {connectionRetryDelayMs / 1000} seconds...");
                    await Task.Delay(connectionRetryDelayMs, _cancellationTokenSource.Token);
                }
            }

            return false;
        }

        private bool IsRetryableException(Exception ex)
        {
            // Define which exceptions are worth retrying
            var retryableExceptions = new[]
            {
                "timeout", "connection", "network", "http", "socket",
                "temporarily unavailable", "service unavailable", "gateway timeout"
            };

            var message = ex.Message.ToLower();
            return retryableExceptions.Any(keyword => message.Contains(keyword)) ||
                   ex is TaskCanceledException ||
                   ex is System.Net.Http.HttpRequestException ||
                   ex is System.Net.Sockets.SocketException;
        }

        public bool IsPaused => _isPaused;
        public bool IsCancellationRequested => _cancellationTokenSource?.Token.IsCancellationRequested ?? false;

        public void Dispose()
        {
            _rallyService?.Dispose();
            _adoService?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}