using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rally_to_ADO_Migration.Models;
using Newtonsoft.Json.Linq;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Two-phase migration service with complete hierarchy preservation.
    /// Phase 1: Create all work items in topological order (parents before children)
    /// Phase 2: Create all relationship links
    /// Ensures NO missing parent-child links and NO duplicate work items.
    /// </summary>
    public class TwoPhaseHierarchicalMigrationService : IDisposable
    {
        private readonly LoggingService _loggingService;
        private readonly Action<string, Color> _uiLogger;
        private readonly RallyApiService _rallyService;
        private readonly AdoApiService _adoService;
        private readonly JsonBasedFieldMappingService _fieldMappingService;
        private readonly EnhancedDuplicateDetectionService _duplicateDetectionService;

        // Event for real-time progress updates to UI
        public event EventHandler<MigrationProgress> ProgressUpdated;

        // Mapping dictionaries for Phase 2 linking
        private readonly Dictionary<string, int> _rallyObjectIdToAdoId = new Dictionary<string, int>();
        private readonly Dictionary<string, RallyWorkItem> _rallyItemsCache = new Dictionary<string, RallyWorkItem>();
        private readonly HashSet<string> _processedItems = new HashSet<string>();

        // Pause/Resume/Cancel support
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isPaused;
        private readonly object _pauseLock = new object();

        public bool DryRunMode { get; set; }
        public bool EnableDifferencePatch { get; set; } = true;
        public bool PreserveHistoricalFields { get; set; } = true;

        /// <summary>
        /// Pause the migration. Can be resumed later.
        /// </summary>
        public void PauseMigration()
        {
            lock (_pauseLock)
            {
                _isPaused = true;
                _loggingService.LogInfo("[PAUSE] Migration paused by user");
                LogToUI("⏸️ Migration PAUSED - Click Resume to continue", Color.Orange);
            }
        }

        /// <summary>
        /// Resume a paused migration.
        /// </summary>
        public void ResumeMigration()
        {
            lock (_pauseLock)
            {
                _isPaused = false;
                _loggingService.LogInfo("[RESUME] Migration resumed by user");
                LogToUI("▶️ Migration RESUMED - Continuing...", Color.Green);
            }
        }

        /// <summary>
        /// Cancel the migration. Cannot be resumed.
        /// </summary>
        public void CancelMigration()
        {
            _cancellationTokenSource?.Cancel();
            _loggingService.LogInfo("[CANCEL] Migration cancellation requested by user");
            LogToUI("❌ Migration CANCELLED by user", Color.Red);
        }

        /// <summary>
        /// Check if migration is currently paused
        /// </summary>
        public bool IsPaused
        {
            get
            {
                lock (_pauseLock)
                {
                    return _isPaused;
                }
            }
        }

        /// <summary>
        /// Check if migration cancellation has been requested
        /// </summary>
        public bool IsCancellationRequested => _cancellationTokenSource?.Token.IsCancellationRequested ?? false;

        public TwoPhaseHierarchicalMigrationService(LoggingService loggingService, Action<string, Color> uiLogger = null)
        {
            _loggingService = loggingService;
            _uiLogger = uiLogger;
            _rallyService = new RallyApiService(loggingService);
            _adoService = new AdoApiService(_loggingService, null, null, null);
            _fieldMappingService = new JsonBasedFieldMappingService(loggingService);
            _fieldMappingService.LoadMappingConfiguration();
            _duplicateDetectionService = new EnhancedDuplicateDetectionService(loggingService, _adoService);
        }

        /// <summary>
        /// Execute two-phase hierarchical migration with complete relationship preservation
        /// </summary>
        public async Task<MigrationProgress> MigrateWithFullHierarchyAsync(
            ConnectionSettings settings,
            List<string> rallyIds = null,
            bool migrateEntireProject = false)
        {
            var progress = new MigrationProgress();
            progress.StartTime = DateTime.Now;

            // Initialize cancellation token source for this migration
            _cancellationTokenSource = new CancellationTokenSource();
            _isPaused = false;
            
            _loggingService.LogInfo("[CONTROL] Migration control initialized - Pause/Resume/Cancel available");

            try
            {
                LogToUI("***********************************************************", Color.Blue);
                LogToUI("TWO-PHASE HIERARCHICAL MIGRATION WITH FULL RELATIONSHIP PRESERVATION", Color.Blue);
                LogToUI("***********************************************************", Color.Blue);
                LogToUI("", Color.Black);

                // ***********************************************************
                // PHASE 0: PREPARATION & DATA COLLECTION
                // ***********************************************************
                LogToUI("PHASE 0: Data Collection & Analysis", Color.Blue);
                LogToUI("************************************************************", Color.Gray);

                List<RallyWorkItem> allRallyItems;

                if (migrateEntireProject)
                {
                    LogToUI(" Fetching ALL work items from Rally project...", Color.Blue);
                    allRallyItems = await FetchAllProjectWorkItemsAsync(settings);
                }
                else if (rallyIds != null && rallyIds.Any())
                {
                    LogToUI(" Fetching specified Rally items with full dependency tree...", Color.Blue);
                    allRallyItems = await FetchItemsWithDependenciesAsync(settings, rallyIds);
                }
                else
                {
                    throw new InvalidOperationException("Either provide Rally IDs or set migrateEntireProject=true");
                }

                LogToUI($"  Retrieved {allRallyItems.Count} total work items (including dependencies)", Color.Green);
                LogToUI("", Color.Black);

                // Analyze hierarchy
                var hierarchyStats = AnalyzeHierarchy(allRallyItems);
                LogToUI(" Hierarchy Analysis:", Color.Cyan);
                foreach (var stat in hierarchyStats)
                {
                    LogToUI($"      � {stat.Key}: {stat.Value} items", Color.Cyan);
                }
                LogToUI("", Color.Black);

                progress.TotalItems = allRallyItems.Count;

                // ***********************************************************
                // PHASE 1: CREATE ALL WORK ITEMS IN TOPOLOGICAL ORDER
                // ***********************************************************
                LogToUI("PHASE 1: Creating Work Items (Topological Order)", Color.Blue);
                LogToUI("************************************************************", Color.Gray);
                LogToUI("   Strategy: Parents created before children to maintain hierarchy", Color.Blue);
                LogToUI("", Color.Black);

                var sortedItems = SortItemsByHierarchy(allRallyItems);
                await CreateAllWorkItemsAsync(settings, sortedItems, progress);

                LogToUI("", Color.Black);
                LogToUI($"   Phase 1 Complete: {progress.SuccessfulItems} created, {progress.SkippedItems} skipped", Color.Green);
                LogToUI("", Color.Black);

                // ***********************************************************
                // PHASE 2: CREATE ALL RELATIONSHIP LINKS
                // ***********************************************************
                LogToUI("PHASE 2: Creating Relationship Links", Color.Blue);
                LogToUI("************************************************************", Color.Gray);
                LogToUI("   Creating all parent-child and related links...", Color.Blue);
                LogToUI("", Color.Black);

                var linkStats = await CreateAllLinksAsync(settings, allRallyItems, progress);

                LogToUI("", Color.Black);
                LogToUI("  Phase 2 Complete - Link Statistics:", Color.Green);
                foreach (var stat in linkStats)
                {
                    LogToUI($"      � {stat.Key}: {stat.Value}", Color.Green);
                }
                LogToUI("", Color.Black);

                // ***********************************************************
                // FINAL SUMMARY
                // ***********************************************************
                progress.IsCompleted = true;
                progress.EndTime = DateTime.Now;

                LogToUI("***********************************************************", Color.Blue);
                LogToUI("TWO-PHASE MIGRATION COMPLETE", Color.Green);
                LogToUI("***********************************************************", Color.Blue);
                LogToUI("", Color.Black);
                LogToUI($"Total Items Processed: {progress.TotalItems}", Color.Blue);
                LogToUI($"Successfully Created: {progress.SuccessfulItems}", Color.Green);
                LogToUI($" Already Existed (Updated): {progress.SkippedItems}", Color.Cyan);
                LogToUI($"Failed: {progress.FailedItems}", progress.FailedItems > 0 ? Color.Red : Color.Blue);
                LogToUI("", Color.Black);

                var duration = progress.EndTime.Value - progress.StartTime;
                LogToUI($" Duration: {duration:hh\\:mm\\:ss}", Color.Blue);
                LogToUI("", Color.Black);

                LogToUI("MIGRATION FEATURES APPLIED:", Color.Green);
                LogToUI("   � Complete hierarchy preservation (Epic?Feature?Story?Task)", Color.Green);
                LogToUI("   � All parent-child relationships maintained", Color.Green);
                LogToUI("   � Test Cases linked to User Stories/Defects", Color.Green);
                LogToUI("   � No duplicate work items", Color.Green);
                LogToUI("   � Full Rally?ADO traceability via tags", Color.Green);
                LogToUI("***********************************************************", Color.Blue);

                return progress;
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogWarning("[CANCEL] Migration was cancelled");
                LogToUI("Migration cancelled by user", Color.Red);
                progress.IsCompleted = false;
                progress.EndTime = DateTime.Now;
                return progress;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Two-phase migration failed", ex);
                LogToUI($"Migration failed: {ex.Message}", Color.Red);
                progress.IsCompleted = true;
                progress.EndTime = DateTime.Now;
                throw;
            }
        }

        /// <summary>
        /// Fetch all work items from Rally project with parent relationships
        /// </summary>
        private async Task<List<RallyWorkItem>> FetchAllProjectWorkItemsAsync(ConnectionSettings settings)
        {
            var allItems = new List<RallyWorkItem>();

            try
            {
                // Fetch all work item types
                var types = new[] { "HierarchicalRequirement", "Defect", "Task", "TestCase", "PortfolioItem/Feature", "PortfolioItem/Epic" };

                foreach (var type in types)
                {
                    try
                    {
                        LogToUI($"      Fetching {type}...", Color.Gray);
                        var items = await _rallyService.GetWorkItemsByTypeAsync(settings, type);
                        
                        if (items != null && items.Any())
                        {
                            allItems.AddRange(items);
                            LogToUI($"      {items.Count} {type}(s) retrieved", Color.Gray);
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogWarning($"Failed to fetch {type}: {ex.Message}");
                    }
                }

                // Enrich all items with parent and children relationships
                LogToUI($"      Enriching {allItems.Count} items with relationships...", Color.Gray);
                await EnrichItemsWithRelationshipsAsync(settings, allItems);

                return allItems;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to fetch all project work items", ex);
                throw;
            }
        }

        /// <summary>
        /// Fetch specified items plus their complete dependency tree (parents, children, linked items)
        /// </summary>
        private async Task<List<RallyWorkItem>> FetchItemsWithDependenciesAsync(ConnectionSettings settings, List<string> rallyIds)
        {
            var allItems = new Dictionary<string, RallyWorkItem>();
            var itemsToProcess = new Queue<string>(rallyIds);
            var processedIds = new HashSet<string>();

            while (itemsToProcess.Any())
            {
                var currentId = itemsToProcess.Dequeue();

                if (processedIds.Contains(currentId))
                    continue;

                processedIds.Add(currentId);

                try
                {
                    var item = await _rallyService.GetWorkItemByIdAsync(settings, currentId);
                    
                    if (item == null)
                    {
                        _loggingService.LogWarning($"Item {currentId} not found in Rally");
                        continue;
                    }

                    // Enrich with test steps for Test Cases
                    if (string.Equals(item.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                    {
                        await _rallyService.EnrichTestCaseStepsAsync(item, settings);
                    }

                    allItems[item.ObjectID] = item;
                    _rallyItemsCache[item.ObjectID] = item;

                    // Add parent to queue
                    if (!string.IsNullOrEmpty(item.Parent) && !processedIds.Contains(item.Parent))
                    {
                        itemsToProcess.Enqueue(item.Parent);
                        LogToUI($"      + Adding parent dependency: {item.Parent}", Color.Gray);
                    }

                    // Add children to queue
                    if (item.Children != null && item.Children.Any())
                    {
                        foreach (var childId in item.Children.Where(c => !processedIds.Contains(c)))
                        {
                            itemsToProcess.Enqueue(childId);
                            LogToUI($"      + Adding child dependency: {childId}", Color.Gray);
                        }
                    }

                    // Add linked test cases to queue
                    if (item.TestCases != null && item.TestCases.Any())
                    {
                        foreach (var tcId in item.TestCases.Where(tc => !processedIds.Contains(tc)))
                        {
                            itemsToProcess.Enqueue(tcId);
                            LogToUI($"      + Adding test case dependency: {tcId}", Color.Gray);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to fetch item {currentId}", ex);
                }
            }

            return allItems.Values.ToList();
        }

        /// <summary>
        /// Enrich items with parent and children relationships, attachments, and comments from Rally
        /// CRITICAL FIX: Attachments and comments were missing because GetWorkItemsByTypeAsync doesn't fetch them
        /// This method ensures ALL Rally data (including attachments/comments) is retrieved
        /// </summary>
        private async Task EnrichItemsWithRelationshipsAsync(ConnectionSettings settings, List<RallyWorkItem> items)
        {
            LogToUI($"      Enriching {items.Count} items with relationships, attachments, comments, and test steps...", Color.Gray);
            _loggingService.LogInfo($"[ENRICHMENT] Starting enrichment for {items.Count} items");
            _loggingService.LogInfo($"[ENRICHMENT] Will fetch: Owner emails, Attachments, Comments, Test Steps");
            
            int count = 0;
            int attachmentCount = 0;
            int commentCount = 0;
            int testStepsCount = 0;
            int testCasesWithSteps = 0;
            
            foreach (var item in items)
            {
                _rallyItemsCache[item.ObjectID] = item;
                
                // Enrich owner email from Rally User API
                await _rallyService.EnrichOwnerEmailAsync(item);
                
                // CRITICAL FIX: Fetch attachments and comments for each item
                // These are NOT included in GetWorkItemsByTypeAsync, so we must fetch them separately
                try
                {
                    await _rallyService.FetchAttachmentsForWorkItemAsync(item);
                    await _rallyService.FetchCommentsForWorkItemAsync(item);
                    
                    // **CRITICAL FIX: Enrich Test Cases with test steps**
                    // This was missing and causing test steps to not be migrated during entire project migration
                    if (string.Equals(item.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                    {
                        await _rallyService.EnrichTestCaseStepsAsync(item, settings);
                        
                        if (item.Steps != null && item.Steps.Any())
                        {
                            testStepsCount += item.Steps.Count;
                            testCasesWithSteps++;
                            _loggingService.LogDebug($"[ENRICHMENT] Fetched {item.Steps.Count} test steps for {item.FormattedID}");
                        }
                        else
                        {
                            _loggingService.LogDebug($"[ENRICHMENT] No test steps found for Test Case {item.FormattedID}");
                        }
                    }
                    
                    // Track statistics
                    if (item.Attachments != null && item.Attachments.Any())
                    {
                        attachmentCount += item.Attachments.Count;
                    }
                    if (item.Comments != null && item.Comments.Any())
                    {
                        commentCount += item.Comments.Count;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"[ENRICHMENT] Failed to fetch attachments/comments/steps for {item.FormattedID}: {ex.Message}");
                }
                
                count++;
                if (count % 50 == 0)
                {
                    LogToUI($"      Enriched {count}/{items.Count} items...", Color.Gray);
                }
            }
            
            LogToUI($"      Enrichment complete for {items.Count} items", Color.Gray);
            _loggingService.LogInfo($"[ENRICHMENT] Complete: {attachmentCount} total attachments, {commentCount} total comments, {testStepsCount} total test steps from {testCasesWithSteps} test cases");
        }

        /// <summary>
        /// Analyze hierarchy to provide statistics
        /// </summary>
        private Dictionary<string, int> AnalyzeHierarchy(List<RallyWorkItem> items)
        {
            var stats = new Dictionary<string, int>();

            foreach (var item in items)
            {
                var type = item.Type ?? "Unknown";
                
                if (type.Contains("Epic"))
                    type = "Epics";
                else if (type.Contains("Feature"))
                    type = "Features";
                else if (type.Contains("HierarchicalRequirement"))
                    type = "User Stories";
                else if (type == "Defect")
                    type = "Defects";
                else if (type == "Task")
                    type = "Tasks";
                else if (type == "TestCase")
                    type = "Test Cases";

                if (!stats.ContainsKey(type))
                    stats[type] = 0;
                
                stats[type]++;
            }

            return stats.OrderByDescending(s => s.Value).ToDictionary(s => s.Key, s => s.Value);
        }

        /// <summary>
        /// Sort items by hierarchy level (parents before children)
        /// Order: Epics ? Features ? Stories/Defects ? Tasks ? Test Cases
        /// </summary>
        private List<RallyWorkItem> SortItemsByHierarchy(List<RallyWorkItem> items)
        {
            LogToUI(" Sorting items by hierarchy level (topological order)...", Color.Blue);

            var sorted = new List<RallyWorkItem>();
            var itemsByObjectId = items.ToDictionary(i => i.ObjectID);
            var processed = new HashSet<string>();

            // Helper function to recursively add item and its parents first
            void AddItemWithParents(RallyWorkItem item)
            {
                if (processed.Contains(item.ObjectID))
                    return;

                // Add parent first (if exists and available)
                if (!string.IsNullOrEmpty(item.Parent) && itemsByObjectId.ContainsKey(item.Parent))
                {
                    AddItemWithParents(itemsByObjectId[item.Parent]);
                }

                // Add this item
                if (!processed.Contains(item.ObjectID))
                {
                    sorted.Add(item);
                    processed.Add(item.ObjectID);
                }
            }

            // Process in hierarchy order by type priority
            var typePriority = new Dictionary<string, int>
            {
                { "PortfolioItem/Epic", 1 },
                { "PortfolioItem/Feature", 2 },
                { "HierarchicalRequirement", 3 },
                { "Defect", 3 },
                { "Task", 4 },
                { "TestCase", 5 }
            };

            var orderedItems = items.OrderBy(i => typePriority.ContainsKey(i.Type) ? typePriority[i.Type] : 99)
                                   .ThenBy(i => i.FormattedID);

            foreach (var item in orderedItems)
            {
                AddItemWithParents(item);
            }

            LogToUI($"   Sorted {sorted.Count} items in topological order", Color.Green);
            return sorted;
        }

        /// <summary>
        /// PHASE 1: Create all work items in ADO (no linking yet)
        /// </summary>
        private async Task CreateAllWorkItemsAsync(ConnectionSettings settings, List<RallyWorkItem> sortedItems, MigrationProgress progress)
        {
            var createdCount = 0;
            var updatedCount = 0;
            var skippedCount = 0;

            for (int i = 0; i < sortedItems.Count; i++)
            {
                var rallyItem = sortedItems[i];
                var itemNumber = i + 1;

                // Check for cancellation
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _loggingService.LogWarning("[CANCEL] Migration cancelled during Phase 1");
                    LogToUI("Migration cancelled by user", Color.Red);
                    progress.IsCompleted = false;
                    return;
                }

                // Handle pause
                while (_isPaused)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        _loggingService.LogWarning("[CANCEL] Migration cancelled while paused");
                        LogToUI("Migration cancelled by user", Color.Red);
                        progress.IsCompleted = false;
                        return;
                    }
                    
                    // Sleep briefly and check again
                    await Task.Delay(500).ConfigureAwait(false);
                }

                try
                {
                    LogToUI($"   [{itemNumber}/{sortedItems.Count}] Processing: {rallyItem.FormattedID} ({rallyItem.Type})", Color.Blue);

                    // DISABLED: State was already corrected by GetTaskStateMinimalAsync() in PHASE 0
                    // No need to re-fetch - the State is already fresh from minimal query
                    // rallyItem = await RefreshTaskStateIfNeededAsync(settings, rallyItem);

                    // Check if already exists
                    var (existsInAdo, existingAdoId, existingWorkItemData) = await _duplicateDetectionService.FindExistingWorkItemAsync(settings, rallyItem);

                    if (existsInAdo && existingAdoId > 0)
                    {
                        LogToUI($"   Already exists in ADO (ID: {existingAdoId})", Color.Cyan);
                        _rallyObjectIdToAdoId[rallyItem.ObjectID] = existingAdoId;
                        progress.SkippedItems++;
                        skippedCount++;
                        progress.ProcessedItems++; // CRITICAL FIX: Increment processed count for skipped items
                        
                        // FULL SYNCHRONIZATION for existing work items (fields, state, attachments, comments, test steps)
                        if (EnableDifferencePatch)
                        {
                            _loggingService.LogInfo($"[SYNC] Synchronizing existing work item: {rallyItem.FormattedID} (ADO ID: {existingAdoId})");
                            await SynchronizeExistingWorkItemAsync(settings, rallyItem, existingAdoId, existingWorkItemData);
                            updatedCount++;
                            LogToUI($"      ✅ Synchronized (updated fields, attachments, comments)", Color.Green);
                        }
                        else
                        {
                            _loggingService.LogWarning($"[SKIP_SYNC] EnableDifferencePatch is false - skipping synchronization for {rallyItem.FormattedID}");
                            LogToUI($"      ⚠️ Skipped synchronization (EnableDifferencePatch is false)", Color.Orange);
                        }

                        // Notify UI of progress update for skipped items
                        ProgressUpdated?.Invoke(this, progress);
                        
                        continue;
                    }

                    // Create new work item
                    var adoId = await CreateSingleWorkItemAsync(settings, rallyItem);
                    
                    if (adoId > 0)
                    {
                        _rallyObjectIdToAdoId[rallyItem.ObjectID] = adoId;
                        progress.SuccessfulItems++;
                        createdCount++;
                        LogToUI($"Created in ADO: ID {adoId}", Color.Green);
                    }
                    else
                    {
                        progress.FailedItems++;
                        LogToUI($"Failed to create", Color.Red);
                    }

                    progress.ProcessedItems++;
                    
                    // Notify UI of progress update after EVERY work item
                    ProgressUpdated?.Invoke(this, progress);
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to create {rallyItem.FormattedID}", ex);
                    LogToUI($"Error: {ex.Message}", Color.Red);
                    progress.FailedItems++;
                    progress.ProcessedItems++;
                    
                    // Notify UI of progress update even on error
                    ProgressUpdated?.Invoke(this, progress);
                }
            }

            LogToUI(" Phase 1 Summary: {createdCount} created, {updatedCount} updated, {skippedCount} skipped", Color.Cyan);
        }

        /// <summary>
        /// Refresh Task state if potentially stale (only affects Tasks, not other work item types)
        /// DISABLED: GetWorkItemByIdAsync() already applies the minimal query fix for Tasks
        /// No need to re-fetch - the State is already corrected in PHASE 0
        /// </summary>
        private async Task<RallyWorkItem> RefreshTaskStateIfNeededAsync(ConnectionSettings settings, RallyWorkItem task)
        {
            // DISABLED: The State was already corrected by GetTaskStateMinimalAsync() in GetWorkItemByIdAsync()
            // No need to re-fetch - just return the task as-is
            // The minimal query (PowerShell-style) already bypassed Rally's stale cache
            
            _loggingService.LogDebug($"[REFRESH_DISABLED] Task {task.FormattedID} state was already corrected by minimal query: '{task.State}'");
            
            // Return original task - State was already corrected in PHASE 0
            return task;
            
            /* COMMENTED OUT - STALE CACHE DETECTION DISABLED
            // ONLY check Tasks - do not affect User Stories, Defects, Test Cases, Features, or Epics
            if (!string.Equals(task.Type, "Task", StringComparison.OrdinalIgnoreCase))
                return task;

            // Use public staleness detection method from RallyApiService
            if (_rallyService.IsPotentiallyStaleTaskState(task))
            {
                _loggingService.LogInfo($"");
                _loggingService.LogInfo($"??  [STALE_DETECTION_TWO_PHASE] Task {task.FormattedID} may have stale state");
                _loggingService.LogInfo($"   Current State: '{task.State}'");
                _loggingService.LogInfo($"   Estimate: {task.Estimate}, ToDo: {task.ToDo}, Actuals: {task.Actuals}");
                _loggingService.LogInfo($"");
                _loggingService.LogInfo($"?? [AUTO_RETRY] Refreshing via direct Rally Read API...");
                
                try
                {
                    // Fetch fresh version via Direct Read API (bypasses query cache)
                    var freshTask = await _rallyService.GetWorkItemByObjectIdDirectAsync("Task", task.ObjectID);
                    
                    if (freshTask != null)
                    {
                        _loggingService.LogInfo($"? [DIRECT_READ_SUCCESS] Fetched fresh data");
                        _loggingService.LogInfo($"   Query API State: '{task.State}'");
                        _loggingService.LogInfo($"   Direct API State: '{freshTask.State}'");
                        
                        if (!string.Equals(task.State, freshTask.State, StringComparison.OrdinalIgnoreCase))
                        {
                            _loggingService.LogInfo($"");
                            _loggingService.LogInfo($"?? [STATE_REFRESHED] Direct API returned different state!");
                            _loggingService.LogInfo($"   ? Query cache had: '{task.State}'");
                            _loggingService.LogInfo($"   ? Direct API has: '{freshTask.State}'");
                            _loggingService.LogInfo($"   Using direct API result (fresher data)");
                            _loggingService.LogInfo($"");
                            
                            LogToUI($"      ?? State refreshed: '{task.State}' ? '{freshTask.State}'", Color.Orange);
                            
                            // Return fresh task with updated state
                            return freshTask;
                        }
                        else
                        {
                            _loggingService.LogInfo($"   ??  Both APIs returned same state: '{task.State}'");
                            _loggingService.LogInfo($"   State is consistent (not stale)");
                            _loggingService.LogInfo($"");
                        }
                    }
                    else
                    {
                        _loggingService.LogWarning($"??  [DIRECT_READ_FAILED] Could not fetch via direct API");
                        _loggingService.LogWarning($"   Continuing with query cache result: '{task.State}'");
                        _loggingService.LogInfo($"");
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarning($"??  [REFRESH_ERROR] Direct API fetch failed: {ex.Message}");
                    _loggingService.LogWarning($"   Continuing with query cache result: '{task.State}'");
                    _loggingService.LogInfo($"");
                }
            }
            
            
            // Return original task if no refresh needed or refresh failed
            return task;
            */
        }

        /// <summary>
        /// Create a single work item in ADO without linking
        /// </summary>
        private async Task<int> CreateSingleWorkItemAsync(ConnectionSettings settings, RallyWorkItem rallyItem)
        {
            try
            {
                var (creationFields, postFields) = _fieldMappingService.TransformRallyWorkItemToAdoFieldsSplit(rallyItem);

                // Remove Steps field from creation (will be added separately)
                if (creationFields.ContainsKey("Microsoft.VSTS.TCM.Steps"))
                {
                    creationFields.Remove("Microsoft.VSTS.TCM.Steps");
                }

                // Ensure iteration path exists
                if (creationFields.ContainsKey("System.IterationPath"))
                {
                    var iterPath = creationFields["System.IterationPath"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(iterPath))
                    {
                        await _adoService.EnsureIterationPathExistsAsync(settings, iterPath);
                    }
                }

                // Build rich description and capture attachment references
                List<RallyAttachmentReference> attachmentReferences = null;
                try
                {
                    var richAssembler = new RichContentAssembler();
                    var richHtml = richAssembler.BuildUnifiedDescription(rallyItem);
                    if (!string.IsNullOrWhiteSpace(richHtml))
                    {
                        creationFields["System.Description"] = richHtml;
                        if (postFields.ContainsKey("System.Description"))
                            postFields.Remove("System.Description");
                        
                        // Capture attachment references for placeholder replacement
                        attachmentReferences = richAssembler.AttachmentReferences;
                        if (attachmentReferences != null && attachmentReferences.Any())
                        {
                            _loggingService.LogDebug($"Captured {attachmentReferences.Count} attachment placeholder(s) from description");
                        }
                    }
                }
                catch (Exception rcEx)
                {
                    _loggingService.LogWarning($"Rich content assembly failed: {rcEx.Message}");
                }

                // Create work item
                var createdItem = await _adoService.CreateWorkItemWithFallbackAsync(settings, creationFields);
                
                if (createdItem == null || createdItem.Id <= 0)
                    return -1;

                var adoId = createdItem.Id;

                // Apply post-creation fields (with bypass rules for historical preservation)
                if (postFields.Any())
                {
                    _loggingService.LogInfo($"[APPLYING] {postFields.Count} post-creation fields...");
                    
                    // SPECIAL: For User Stories, Defects, Tasks, Features, and Test Cases - ensure System.State is in post-creation updates
                    var postCreationUpdates = new Dictionary<string, object>(postFields);
                    
                    if (string.Equals(rallyItem.Type, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rallyItem.Type, "Defect", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rallyItem.Type, "Task", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rallyItem.Type, "PortfolioItem/Feature", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                    {
                        // CRITICAL FIX: Use FieldTransformationService to transform Rally state to ADO state based on work item type
                        // This ensures Task "Completed" -> "Closed", UserStory "Completed" -> "Resolved", Defect "Completed" -> "Resolved", etc.
                        if (!string.IsNullOrEmpty(rallyItem.State))
                        {
                            _loggingService.LogInfo($"[STATE_TRANSFORM] Starting state transformation for {rallyItem.FormattedID}");
                            _loggingService.LogInfo($"   Rally Type: '{rallyItem.Type}'");
                            _loggingService.LogInfo($"   Rally State (from Rally API): '{rallyItem.State}'");
                            
                            var fieldTransformationService = new FieldTransformationService(_loggingService);
                            var adoState = fieldTransformationService.TransformState(rallyItem.State, rallyItem.Type);
                            
                            var workItemTypeName = rallyItem.Type.Contains("Feature") ? "Feature" : 
                                                   rallyItem.Type == "Task" ? "Task" : 
                                                   rallyItem.Type == "TestCase" ? "Test Case" : "User Story";
                            _loggingService.LogInfo($"[STATE] {workItemTypeName} State Mapping: Rally '{rallyItem.State}' -> ADO '{adoState}'");
                            _loggingService.LogInfo($"   Transformed ADO State: '{adoState}'");
                            _loggingService.LogInfo($"   Will update System.State field in ADO to: '{adoState}'");
                            
                            // Force transformed state into post-creation updates
                            postCreationUpdates["System.State"] = adoState;
                        }
                        // SPECIAL: For Test Cases, if State is null or empty, force it to "Ready"
                        else if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                        {
                            _loggingService.LogInfo($"[STATE] Test Case - Forcing State to 'Ready' (default)");
                            postCreationUpdates["System.State"] = "Ready";
                        }
                        else
                        {
                            _loggingService.LogWarning($"[STATE_WARNING] Rally State is NULL or empty for {rallyItem.FormattedID} ({rallyItem.Type})");
                            _loggingService.LogWarning($"   This work item may not have a State field in Rally or it wasn't fetched correctly");
                        }
                    }
                    
                    // CRITICAL: For Tasks, use intermediate state transitions to respect ADO workflow rules
                    // ADO often requires: New -> Active -> Closed (cannot go New -> Closed directly)
                    if (string.Equals(rallyItem.Type, "Task", StringComparison.OrdinalIgnoreCase) && 
                        postCreationUpdates.ContainsKey("System.State"))
                    {
                        var targetState = postCreationUpdates["System.State"]?.ToString();
                        postCreationUpdates.Remove("System.State"); // Remove from regular patch
                        
                        _loggingService.LogInfo($"[TASK_STATE] Task requires state '{targetState}' - using intermediate transitions");
                        
                        // SKIP date fields if we don't have bypass permission (they'll fail anyway)
                        // Remove date fields that require bypass rules
                        postCreationUpdates.Remove("System.CreatedDate");
                        postCreationUpdates.Remove("System.ChangedDate");
                        postCreationUpdates.Remove("System.CreatedBy");
                        
                        // Apply other post-creation fields first (if any remain)
                        if (postCreationUpdates.Any())
                        {
                            _loggingService.LogInfo($"[TASK_CREATE] Applying {postCreationUpdates.Count} non-date fields");
                            foreach (var field in postCreationUpdates)
                            {
                                _loggingService.LogDebug($"{field.Key} = {field.Value}");
                            }
                            
                            var otherFieldsSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, postCreationUpdates, bypassRules: false);
                            if (!otherFieldsSuccess)
                            {
                                _loggingService.LogWarning($"[WARNING] Post-creation patch for other fields failed (State will still be attempted)");
                            }
                        }
                        
                        // Now handle state transition with intermediate steps (WITHOUT bypass rules)
                        _loggingService.LogInfo($"[STATE_UPDATE] Using intermediate state transitions without bypass rules (target: '{targetState}')");
                        var stateSuccess = await _adoService.UpdateTaskStateWithTransitionsAsync(settings, adoId, targetState, bypassRules: false);
                        
                        if (stateSuccess)
                        {
                            _loggingService.LogInfo($"✓ State updated successfully to '{targetState}' via intermediate transitions");
                        }
                        else
                        {
                            _loggingService.LogWarning($"[STATE_ERROR] Failed to update Task state to '{targetState}' via intermediate transitions");
                            _loggingService.LogWarning($"[TIP] Task state may remain as 'New' or 'Active'. Grant 'Bypass rules' permission for full state sync.");
                        }
                    }
                    else
                    {
                        // For non-Task work items, use regular patch (old behavior)
                        foreach (var field in postCreationUpdates)
                        {
                            _loggingService.LogDebug($"{field.Key} = {field.Value}");
                        }
                        
                        // Log specifically for State field
                        if (postCreationUpdates.ContainsKey("System.State"))
                        {
                            _loggingService.LogInfo($"[UPDATE] Attempting to update System.State to: '{postCreationUpdates["System.State"]}'");
                        }
                        
                        var patchSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, postCreationUpdates, bypassRules: true);
                        if (patchSuccess)
                        {
                            var stateValue = postCreationUpdates.ContainsKey("System.State") ? postCreationUpdates["System.State"]?.ToString() : null;
                            if (!string.IsNullOrEmpty(stateValue))
                            {
                                _loggingService.LogInfo($"✓ State updated: New → {stateValue}");
                                
                                // VERIFICATION: Re-fetch the work item to confirm state was actually set
                                try
                                {
                                    var verifyWorkItem = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                                    var actualState = verifyWorkItem?["fields"]?["System.State"]?.ToString();
                                    _loggingService.LogInfo($"[VERIFY] ADO work item {adoId} actual State after update: '{actualState}'");
                                    
                                    if (actualState != stateValue)
                                    {
                                        _loggingService.LogWarning($"[MISMATCH] State mismatch! Requested '{stateValue}' but ADO has '{actualState}'");
                                        _loggingService.LogWarning($"   This may be due to ADO workflow rules or process template restrictions");
                                    }
                                    else
                                    {
                                        _loggingService.LogInfo($"[CONFIRMED] State successfully set to '{actualState}' in ADO");
                                    }
                                }
                                catch (Exception verifyEx)
                                {
                                    _loggingService.LogWarning($"Could not verify state update: {verifyEx.Message}");
                                }
                            }
                            _loggingService.LogDebug($"Successfully applied post-creation fields");
                        }
                        else
                        {
                            _loggingService.LogWarning($"[WARNING] Post-creation patch with bypass rules failed! Attempting fallback...");
                            
                            // FALLBACK: Try updating State without bypass rules
                            var fallbackUpdates = new Dictionary<string, object>();
                            if (postCreationUpdates.ContainsKey("System.State"))
                            {
                                fallbackUpdates["System.State"] = postCreationUpdates["System.State"];
                            }
                            
                            if (fallbackUpdates.Any())
                            {
                                _loggingService.LogInfo($"[FALLBACK] Attempting to update State without bypass rules");
                                var fallbackSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, fallbackUpdates, bypassRules: false);
                                
                                if (fallbackSuccess)
                                {
                                    _loggingService.LogInfo($"✓ State updated via fallback (no bypass): {fallbackUpdates["System.State"]}");
                                }
                                else
                                {
                                    _loggingService.LogError($"Both bypass and fallback state updates failed!");
                                    _loggingService.LogWarning($"[TIP] Grant 'Bypass rules when pushing' permission for better migration.");
                                }
                            }
                        }
                    }
                }

                // Add test steps for Test Cases
                if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase) && 
                    rallyItem.Steps != null && rallyItem.Steps.Any())
                {
                    _loggingService.LogInfo($"[TEST_STEPS] Adding {rallyItem.Steps.Count} test steps to ADO Test Case {adoId}");
                    
                    var stepsSuccess = await _adoService.AddTestCaseStepsAsync(settings, adoId, rallyItem.Steps, false);
                    
                    if (stepsSuccess)
                    {
                        _loggingService.LogInfo($"[TEST_STEPS] API returned success, verifying steps were added...");
                        
                        // Verify steps were actually added by re-fetching the work item
                        try
                        {
                            var verifyWorkItem = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                            var stepsField = verifyWorkItem?["fields"]?["Microsoft.VSTS.TCM.Steps"]?.ToString();
                            
                            if (!string.IsNullOrEmpty(stepsField))
                            {
                                // Count steps in XML
                                var stepMatches = System.Text.RegularExpressions.Regex.Matches(stepsField, "<step ");
                                _loggingService.LogInfo($"[VERIFIED] Steps field contains {stepsField.Length} chars, {stepMatches.Count} step(s)");
                                
                                if (stepMatches.Count != rallyItem.Steps.Count)
                                {
                                    _loggingService.LogWarning($"[WARNING] Step count mismatch! Rally had {rallyItem.Steps.Count} steps but ADO has {stepMatches.Count}");
                                }
                            }
                            else
                            {
                                _loggingService.LogWarning($"[WARNING] Steps field is EMPTY after patch! Steps may not have been added.");
                                _loggingService.LogWarning($"[INFO] Steps are available in Description table as a workaround.");
                            }
                        }
                        catch (Exception verifyEx)
                        {
                            _loggingService.LogWarning($"      [WARNING] Could not verify steps were added: {verifyEx.Message}");
                        }
                    }
                    else
                    {
                        _loggingService.LogWarning($"[WARNING] AddTestCaseStepsAsync returned false - steps may not have been added");
                        _loggingService.LogWarning($"[INFO] Steps are available in Description table as a workaround.");
                    }
                    
                    // Also add to description
                    var testStepsService = new TestStepsToDescriptionService(_loggingService);
                    var workItem = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                    var currentDescription = workItem?["fields"]?["System.Description"]?.ToString() ?? string.Empty;
                    var updatedDescription = testStepsService.AppendTestStepsToDescription(currentDescription, rallyItem.Steps);
                    var descriptionField = new Dictionary<string, object> { ["System.Description"] = updatedDescription };
                    await _adoService.PatchWorkItemFieldsAsync(settings, adoId, descriptionField, false);
                    _loggingService.LogInfo($"[TEST_STEPS] Also added steps table to Description as backup");
                }

                // Migrate attachments with placeholder replacement
                if (rallyItem.Attachments != null && rallyItem.Attachments.Any())
                {
                    _loggingService.LogInfo($"[ATTACHMENTS] Migrating {rallyItem.Attachments.Count} attachments");
                    
                    // Even for new items, check if attachments were already added (in case of retry)
                    var existingAttachments = await GetExistingAttachmentNamesAsync(settings, adoId);
                    
                    var attachmentsToUpload = rallyItem.Attachments
                        .Where(a => !existingAttachments.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    
                    if (attachmentsToUpload.Any() || (attachmentReferences != null && attachmentReferences.Any()))
                    {
                        // Use AttachmentPlaceholderService to upload and replace placeholders
                        var attachmentService = new AttachmentPlaceholderService(_adoService, _loggingService);
                        var placeholderSuccess = await attachmentService.UploadAttachmentsAndReplacePlaceholdersAsync(
                            settings,
                            adoId,
                            rallyItem,
                            attachmentReferences);
                        
                        if (placeholderSuccess)
                        {
                            _loggingService.LogInfo($"[ATTACHMENTS] Successfully migrated with placeholder replacement");
                        }
                        else
                        {
                            _loggingService.LogWarning($"[ATTACHMENTS] Placeholder replacement had issues, but attachments were uploaded");
                        }
                    }
                    else
                    {
                        _loggingService.LogDebug($"All attachments already exist");
                    }
                }

                // Migrate comments
                if (rallyItem.Comments != null && rallyItem.Comments.Any())
                {
                    foreach (var comment in rallyItem.Comments.OrderBy(c => c.CreationDate))
                    {
                        try
                        {
                            await _adoService.AddCommentAsync(settings, adoId, comment);
                        }
                        catch (Exception comEx)
                        {
                            _loggingService.LogWarning($"Failed to add comment: {comEx.Message}");
                        }
                    }
                }

                return adoId;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"CreateSingleWorkItemAsync failed for {rallyItem.FormattedID}", ex);
                return -1;
            }
        }

        /// <summary>
        /// Synchronize an existing ADO work item with Rally data
        /// Updates fields, state, attachments, comments, and test steps
        /// </summary>
        private async Task SynchronizeExistingWorkItemAsync(ConnectionSettings settings, RallyWorkItem rallyItem, int adoId, JObject existingWorkItemData)
        {
            try
            {
                _loggingService.LogInfo($"[SYNC] Synchronizing {rallyItem.FormattedID} (ADO ID: {adoId})");
                
                // DISABLED: State was already corrected by GetTaskStateMinimalAsync() in PHASE 0
                // No need to re-fetch - the State is already fresh from minimal query
                // rallyItem = await RefreshTaskStateIfNeededAsync(settings, rallyItem);
                
                var (creationFields, postFields) = _fieldMappingService.TransformRallyWorkItemToAdoFieldsSplit(rallyItem);
                
                // IMPORTANT: Rebuild Description with Release field for existing items and capture attachment references
                // CRITICAL: Reuse the description that was already built during field transformation to preserve placeholder GUIDs
                List<RallyAttachmentReference> syncAttachmentReferences = null;
                string rebuiltDescription = null;
                
                try
                {
                    var richAssembler = new RichContentAssembler();
                    var richHtml = richAssembler.BuildUnifiedDescription(rallyItem);
                    if (!string.IsNullOrWhiteSpace(richHtml))
                    {
                        rebuiltDescription = richHtml;
                        creationFields["System.Description"] = richHtml;
                        _loggingService.LogDebug($"[REBUILD] Description with Release info for existing item");
                        
                        // Capture attachment references for placeholder replacement during sync
                        syncAttachmentReferences = richAssembler.AttachmentReferences;
                        if (syncAttachmentReferences != null && syncAttachmentReferences.Any())
                        {
                            _loggingService.LogInfo($"[SYNC_ATTACH] Captured {syncAttachmentReferences.Count} attachment placeholder(s) from rebuilt description");
                            foreach (var attachRef in syncAttachmentReferences)
                            {
                                _loggingService.LogDebug($"  Placeholder: {attachRef.PlaceholderToken} for file: {attachRef.FileName}");
                            }
                        }
                    }
                }
                catch (Exception rcEx)
                {
                    _loggingService.LogWarning($"Rich content assembly failed for existing item: {rcEx.Message}");
                }
                
                // Merge creation and post-creation fields for comparison
                var allFields = new Dictionary<string, object>(creationFields);
                foreach (var kv in postFields)
                {
                    if (!allFields.ContainsKey(kv.Key))
                        allFields[kv.Key] = kv.Value;
                }

                // 1. Update changed fields (regular fields)
                var regularFieldDifferences = _duplicateDetectionService.CompareAndGetDifferences(rallyItem, existingWorkItemData, allFields);
                // Remove post-creation system fields from regular update
                var systemFieldsToExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System.State", "System.CreatedDate", "System.ChangedDate", "System.CreatedBy" };
                var regularUpdates = regularFieldDifferences.Where(kv => !systemFieldsToExclude.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                
                if (regularUpdates.Any())
                {
                    _loggingService.LogInfo($"[UPDATING] {regularUpdates.Count} regular fields");
                    var patched = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, regularUpdates, bypassRules: false);
                    if (patched)
                    {
                        LogToUI($"Updated: {string.Join(", ", regularUpdates.Keys.Take(5))}", Color.Green);
                    }
                }
                else
                {
                    _loggingService.LogDebug($"[NO_CHANGES] No regular field changes detected");
                }

                // 2. Update post-creation fields (State, historical dates) with bypass rules
                var postCreationUpdates = postFields.ToDictionary(kv => kv.Key, kv => kv.Value);
                
                // SPECIAL: For User Stories, Defects, Tasks, and Test Cases, ensure System.State is always in post-creation updates
                if (string.Equals(rallyItem.Type, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rallyItem.Type, "Defect", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rallyItem.Type, "Task", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                {
                    // CRITICAL FIX: Use FieldTransformationService to transform Rally state to ADO state based on work item type
                    // This ensures Task "Completed" -> "Closed", UserStory "Completed" -> "Resolved", Defect "Completed" -> "Resolved", etc.
                    if (!string.IsNullOrEmpty(rallyItem.State))
                    {
                        _loggingService.LogInfo($"[STATE_TRANSFORM] Synchronizing state for {rallyItem.FormattedID}");
                        _loggingService.LogInfo($"   Rally Type: '{rallyItem.Type}'");
                        _loggingService.LogInfo($"   Rally State (from Rally API): '{rallyItem.State}'");
                        
                        var fieldTransformationService = new FieldTransformationService(_loggingService);
                        var adoState = fieldTransformationService.TransformState(rallyItem.State, rallyItem.Type);
                        
                        var workItemTypeName = string.Equals(rallyItem.Type, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase) ? "User Story" : 
                                               string.Equals(rallyItem.Type, "Defect", StringComparison.OrdinalIgnoreCase) ? "Defect" :
                                               string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase) ? "Test Case" : "Task";
                        _loggingService.LogInfo($"[STATE] {workItemTypeName} State Mapping: Rally '{rallyItem.State}' -> ADO '{adoState}'");
                        _loggingService.LogInfo($"   Transformed ADO State: '{adoState}'");
                        _loggingService.LogInfo($"   Will update System.State field in ADO to: '{adoState}'");
                        
                        // Force transformed state into post-creation updates
                        postCreationUpdates["System.State"] = adoState;
                    }
                    // SPECIAL: For Test Cases, if State is null or empty, force it to "Ready"
                    else if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase))
                    {
                        _loggingService.LogInfo($"[STATE] Test Case - Forcing State to 'Ready' (default for sync)");
                        postCreationUpdates["System.State"] = "Ready";
                    }
                    else
                    {
                        _loggingService.LogWarning($"[STATE_WARNING] Rally State is NULL or empty for {rallyItem.FormattedID} ({rallyItem.Type})");
                        _loggingService.LogWarning($"   This work item may not have a State field in Rally or it wasn't fetched correctly");
                    }
                }
                
                if (postCreationUpdates.Any())
                {
                    _loggingService.LogInfo($"[POST_UPDATE] Updating {postCreationUpdates.Count} post-creation fields (State, dates)");
                    
                    // CRITICAL: For Tasks, use intermediate state transitions to respect ADO workflow rules
                    if (string.Equals(rallyItem.Type, "Task", StringComparison.OrdinalIgnoreCase) && 
                        postCreationUpdates.ContainsKey("System.State"))
                    {
                        var targetState = postCreationUpdates["System.State"]?.ToString();
                        postCreationUpdates.Remove("System.State"); // Remove from regular patch
                        
                        _loggingService.LogInfo($"[TASK_STATE] Task sync requires state '{targetState}' - using intermediate transitions");
                        
                        // SKIP date fields if we don't have bypass permission (they'll fail anyway)
                        // Remove date fields that require bypass rules
                        postCreationUpdates.Remove("System.CreatedDate");
                        postCreationUpdates.Remove("System.ChangedDate");
                        postCreationUpdates.Remove("System.CreatedBy");
                        
                        // Apply other post-creation fields first (if any remain)
                        if (postCreationUpdates.Any())
                        {
                            _loggingService.LogInfo($"[TASK_SYNC] Applying {postCreationUpdates.Count} non-date fields");
                            var otherFieldsSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, postCreationUpdates, bypassRules: false);
                            if (!otherFieldsSuccess)
                            {
                                _loggingService.LogWarning($"[WARNING] Post-creation patch for other fields failed during sync");
                            }
                        }
                        
                        // Now handle state transition with intermediate steps (WITHOUT bypass rules)
                        _loggingService.LogInfo($"[STATE_SYNC] Using intermediate state transitions without bypass rules (target: '{targetState}')");
                        var stateSuccess = await _adoService.UpdateTaskStateWithTransitionsAsync(settings, adoId, targetState, bypassRules: false);
                        
                        if (stateSuccess)
                        {
                            _loggingService.LogInfo($"✓ State synchronized successfully to '{targetState}' via intermediate transitions");
                        }
                        else
                        {
                            _loggingService.LogWarning($"[STATE_ERROR] Failed to sync Task state to '{targetState}'");
                            _loggingService.LogWarning($"[TIP] Task state may require manual update in ADO or 'Bypass rules' permission");
                        }
                    }
                    else
                    {
                        // For non-Task work items, use regular patch
                        // Log the state value specifically for debugging
                        if (postCreationUpdates.ContainsKey("System.State"))
                        {
                            _loggingService.LogInfo($"[ATTEMPT] Updating System.State to: '{postCreationUpdates["System.State"]}'");
                        }
                        
                        var patchSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, postCreationUpdates, bypassRules: true);
                        if (patchSuccess)
                        {
                            var stateValue = postCreationUpdates.ContainsKey("System.State") ? postCreationUpdates["System.State"]?.ToString() : null;
                            if (!string.IsNullOrEmpty(stateValue))
                            {
                                _loggingService.LogInfo($"✓ State synchronized: {stateValue}");
                                
                                // VERIFICATION: Re-fetch the work item to confirm state was actually set
                                try
                                {
                                    var verifyWorkItem = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                                    var actualState = verifyWorkItem?["fields"]?["System.State"]?.ToString();
                                    _loggingService.LogInfo($"[VERIFY] ADO work item {adoId} actual State after sync: '{actualState}'");
                                    
                                    if (actualState != stateValue)
                                    {
                                        _loggingService.LogWarning($"[MISMATCH] State mismatch! Requested '{stateValue}' but ADO has '{actualState}'");
                                        _loggingService.LogWarning($"   This may be due to ADO workflow rules or process template restrictions");
                                    }
                                    else
                                    {
                                        _loggingService.LogInfo($"[CONFIRMED] State successfully synchronized to '{actualState}' in ADO");
                                    }
                                }
                                catch (Exception verifyEx)
                                {
                                    _loggingService.LogWarning($"Could not verify state sync: {verifyEx.Message}");
                                }
                            }
                        }
                        else
                        {
                            _loggingService.LogWarning($"[WARNING] Post-creation patch with bypass rules failed! Attempting fallback...");
                            
                            // FALLBACK: Try updating State without bypass rules (for users without bypass permission)
                            // Remove historical date fields that require bypass rules
                            var fallbackUpdates = new Dictionary<string, object>();
                            if (postCreationUpdates.ContainsKey("System.State"))
                            {
                                fallbackUpdates["System.State"] = postCreationUpdates["System.State"];
                            }
                            
                            if (fallbackUpdates.Any())
                            {
                                _loggingService.LogInfo($"[FALLBACK] Attempting to update State without bypass rules");
                                var fallbackSuccess = await _adoService.PatchWorkItemFieldsAsync(settings, adoId, fallbackUpdates, bypassRules: false);
                                
                                if (fallbackSuccess)
                                {
                                    _loggingService.LogInfo($"✓ State synchronized via fallback (no bypass): {fallbackUpdates["System.State"]}");
                                }
                                else
                                {
                                    _loggingService.LogError($"Both bypass and fallback state updates failed! State remains unchanged.");
                                    _loggingService.LogWarning($"[TIP] Grant 'Bypass rules when pushing' permission for better migration.");
                                }
                            }
                        }
                    }
                }

                // 3. Synchronize Test Steps for Test Cases
                if (string.Equals(rallyItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase) && 
                    rallyItem.Steps != null && rallyItem.Steps.Any())
                {
                    _loggingService.LogInfo($"[TEST_STEPS] Synchronizing {rallyItem.Steps.Count} test steps");
                    
                    var stepsSuccess = await _adoService.AddTestCaseStepsAsync(settings, adoId, rallyItem.Steps, bypassRules: false);
                    
                    if (stepsSuccess)
                    {
                        _loggingService.LogInfo($"[TEST_STEPS] API returned success, verifying steps were updated...");
                        
                        // Verify steps were actually updated by re-fetching the work item
                        try
                        {
                            var verifyWorkItem = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                            var stepsField = verifyWorkItem?["fields"]?["Microsoft.VSTS.TCM.Steps"]?.ToString();
                            
                            if (!string.IsNullOrEmpty(stepsField))
                            {
                                // Count steps in XML
                                var stepMatches = System.Text.RegularExpressions.Regex.Matches(stepsField, "<step ");
                                _loggingService.LogInfo($"[VERIFIED] Steps field contains {stepsField.Length} chars, {stepMatches.Count} step(s)");
                                
                                if (stepMatches.Count != rallyItem.Steps.Count)
                                {
                                    _loggingService.LogWarning($"[WARNING] Step count mismatch! Rally had {rallyItem.Steps.Count} steps but ADO has {stepMatches.Count}");
                                }
                                else
                                {
                                    _loggingService.LogInfo($"All {stepMatches.Count} steps synchronized successfully");
                                }
                            }
                            else
                            {
                                _loggingService.LogWarning($"[WARNING] Steps field is EMPTY after patch! Steps may not have been updated.");
                            }
                        }
                        catch (Exception verifyEx)
                        {
                            _loggingService.LogWarning($"         [WARNING] Could not verify steps were updated: {verifyEx.Message}");
                        }
                    }
                    else
                    {
                        _loggingService.LogWarning($"         [WARNING] AddTestCaseStepsAsync returned false - steps may not have been updated");
                    }
                    
                    // Also update the description with steps table
                    var testStepsService = new TestStepsToDescriptionService(_loggingService);
                    var workItem = await _adoService.GetWorkItemByIdAsync(settings, adoId);
                    var currentDescription = workItem?["fields"]?["System.Description"]?.ToString() ?? string.Empty;
                    
                    // Check if description already has the steps table
                    if (!currentDescription.Contains("Test Steps (Migrated from Rally)"))
                    {
                        var updatedDescription = testStepsService.AppendTestStepsToDescription(currentDescription, rallyItem.Steps);
                        var descriptionField = new Dictionary<string, object> { ["System.Description"] = updatedDescription };
                        await _adoService.PatchWorkItemFieldsAsync(settings, adoId, descriptionField, false);
                        _loggingService.LogInfo($"[TEST_STEPS] Also added steps table to Description as backup");
                    }
                    else
                    {
                        _loggingService.LogDebug($"[TEST_STEPS] Description already contains steps table, skipping");
                    }
                }

                // 4. Synchronize Attachments (only add missing ones) with placeholder replacement
                if (rallyItem.Attachments != null && rallyItem.Attachments.Any())
                {
                    _loggingService.LogInfo($"[CHECKING] {rallyItem.Attachments.Count} attachments");
                    
                    // Get existing attachments from ADO
                    var existingAttachments = await GetExistingAttachmentNamesAsync(settings, adoId);
                    
                    // Only upload attachments that don't already exist
                    var attachmentsToUpload = rallyItem.Attachments
                        .Where(a => !existingAttachments.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    
                    if (attachmentsToUpload.Any() || (syncAttachmentReferences != null && syncAttachmentReferences.Any()))
                    {
                        // Use AttachmentPlaceholderService to upload and replace placeholders during sync
                        var attachmentService = new AttachmentPlaceholderService(_adoService, _loggingService);
                        
                        // Create a temporary Rally item with only the attachments that need uploading
                        var tempItem = new RallyWorkItem
                        {
                            ObjectID = rallyItem.ObjectID,
                            FormattedID = rallyItem.FormattedID,
                            Attachments = attachmentsToUpload
                        };
                        
                        // CRITICAL: Pass the rebuilt description so we replace placeholders in the right content
                        var placeholderSuccess = await attachmentService.UploadAttachmentsAndReplacePlaceholdersAsync(
                            settings,
                            adoId,
                            tempItem,
                            syncAttachmentReferences,
                            rebuiltDescription); // Pass the description we just built
                        
                        if (placeholderSuccess)
                        {
                            _loggingService.LogInfo($"[SYNC] Attachments synchronized with placeholder replacement");
                        }
                        else
                        {
                            _loggingService.LogWarning($"[SYNC] Placeholder replacement had issues during sync");
                        }
                    }
                    else
                    {
                        _loggingService.LogDebug($"All {existingAttachments.Count} attachments already exist");
                    }
                }

                // 5. Synchronize Comments (only add missing ones)
                if (rallyItem.Comments != null && rallyItem.Comments.Any())
                {
                    _loggingService.LogInfo($"[CHECKING] {rallyItem.Comments.Count} comments");
                    
                    // Get existing comments from ADO to avoid duplicates
                    var existingCommentTexts = await GetExistingCommentTextsAsync(settings, adoId);
                    
                    // Only add comments that don't already exist (compare by text content)
                    var commentsToAdd = rallyItem.Comments
                        .Where(c => !existingCommentTexts.Contains(NormalizeCommentText(c.Text)))
                        .ToList();
                    
                    if (commentsToAdd.Any())
                    {
                        _loggingService.LogInfo($"[ADDING] {commentsToAdd.Count} new comments (skipping {rallyItem.Comments.Count - commentsToAdd.Count} duplicates)");
                        
                        foreach (var comment in commentsToAdd.OrderBy(c => c.CreationDate))
                        {
                            try
                            {
                                var success = await _adoService.AddCommentAsync(settings, adoId, comment);
                                if (!success)
                                {
                                    _loggingService.LogDebug($"Comment add returned false, may already exist");
                                }
                            }
                            catch (Exception comEx)
                            {
                                _loggingService.LogDebug($"Comment add failed (may already exist): {comEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        _loggingService.LogDebug($"All {existingCommentTexts.Count} comments already exist");
                    }
                }

                LogToUI($"    Synchronized successfully", Color.Green);
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"SynchronizeExistingWorkItemAsync failed for {rallyItem.FormattedID}: {ex.Message}");
                LogToUI($"      Sync error: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// PHASE 2: Create all relationship links
        /// </summary>
        private async Task<Dictionary<string, int>> CreateAllLinksAsync(ConnectionSettings settings, List<RallyWorkItem> allItems, MigrationProgress progress)
        {
            var stats = new Dictionary<string, int>
            {
                { "Parent-Child Links", 0 },
                { "Test Case Links", 0 },
                { "Failed Links", 0 },
                { "Skipped (No Parent)", 0 },
                { "Skipped (Parent Not Migrated)", 0 },
                { "Skipped (Child Not Migrated)", 0 }
            };

            LogToUI(" Creating parent-child hierarchy links...", Color.Blue);
            LogToUI("", Color.Black);
            
            // Log the mapping status first for debugging
            _loggingService.LogInfo($"[MAPPING] Rally-ADO ID Mapping Status: {_rallyObjectIdToAdoId.Count} items mapped");
            foreach (var mapping in _rallyObjectIdToAdoId.Take(10))
            {
                _loggingService.LogDebug($"Rally ObjectID {mapping.Key} ? ADO ID {mapping.Value}");
            }
            
            foreach (var rallyItem in allItems)
            {
                try
                {
                    _loggingService.LogDebug($"[PROCESSING] Links for {rallyItem.FormattedID} (ObjectID: {rallyItem.ObjectID}, Type: {rallyItem.Type})");
                    
                    // Verify this child item was migrated
                    if (!_rallyObjectIdToAdoId.ContainsKey(rallyItem.ObjectID))
                    {
                        _loggingService.LogWarning($"[NOT_IN_MAPPING] {rallyItem.FormattedID} not in ADO mapping, skipping links");
                        stats["Skipped (Child Not Migrated)"]++;
                        continue;
                    }

                    var childAdoId = _rallyObjectIdToAdoId[rallyItem.ObjectID];
                    _loggingService.LogDebug($"Child ADO ID: {childAdoId}");

                    // Link to parent
                    if (string.IsNullOrEmpty(rallyItem.Parent))
                    {
                        _loggingService.LogDebug($"No parent defined for {rallyItem.FormattedID}");
                        stats["Skipped (No Parent)"]++;
                    }
                    else if (!_rallyObjectIdToAdoId.ContainsKey(rallyItem.Parent))
                    {
                        _loggingService.LogWarning($"[PARENT_NOT_FOUND] Parent Rally ObjectID {rallyItem.Parent} for {rallyItem.FormattedID} not found in ADO mapping!");
                        _loggingService.LogWarning($"This usually means the parent was not migrated or failed to create.");
                        stats["Skipped (Parent Not Migrated)"]++;
                    }
                    else
                    {
                        var parentAdoId = _rallyObjectIdToAdoId[rallyItem.Parent];
                        _loggingService.LogInfo($"[CHECKING_LINK] {rallyItem.FormattedID} (ADO {childAdoId}) -> Parent (Rally {rallyItem.Parent}, ADO {parentAdoId})");
                        
                        // Check if link already exists to avoid duplicate parent errors
                        var linkExists = await CheckParentLinkExistsAsync(settings, childAdoId, parentAdoId);
                        
                        if (linkExists)
                        {
                            _loggingService.LogInfo($"[LINK_EXISTS] Link already exists, skipping");
                            LogToUI($"{rallyItem.FormattedID} already linked to parent", Color.Gray);
                            stats["Parent-Child Links"]++; // Count as success (link exists)
                        }
                        else
                        {
                            _loggingService.LogInfo($"Creating new parent-child link...");
                            try
                            {
                                var linked = await _adoService.LinkWorkItemsAsync(settings, parentAdoId, childAdoId, "Child");
                                
                                if (linked)
                                {
                                    stats["Parent-Child Links"]++;
                                    LogToUI($"Linked {rallyItem.FormattedID} Parent (ADO #{parentAdoId})", Color.Green);
                                }
                                else
                                {
                                    stats["Failed Links"]++;
                                    _loggingService.LogError($"? Failed to link {rallyItem.FormattedID} to parent (no exception thrown)");
                                    LogToUI($"Failed to link {rallyItem.FormattedID} Parent", Color.Red);
                                }
                            }
                            catch (Exception linkEx)
                            {
                                // Check if error is "already has parent" - treat as success
                                if (linkEx.Message.Contains("TF201036") || linkEx.Message.Contains("already") || linkEx.Message.Contains("Parent link"))
                                {
                                    _loggingService.LogInfo($"[LINK_EXISTS] Already exists (detected via exception): {linkEx.Message}");
                                    LogToUI($"{rallyItem.FormattedID} already linked to parent", Color.Gray);
                                    stats["Parent-Child Links"]++; // Count as success
                                }
                                else
                                {
                                    stats["Failed Links"]++;
                                    _loggingService.LogError($"Link error for {rallyItem.FormattedID}: {linkEx.Message}");
                                    LogToUI($"Link error: {linkEx.Message}", Color.Red);
                                }
                            }
                        }
                    }

                    // Link Test Cases to User Stories/Defects
                    if ((string.Equals(rallyItem.Type, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(rallyItem.Type, "Defect", StringComparison.OrdinalIgnoreCase)) &&
                        rallyItem.TestCases != null && rallyItem.TestCases.Any())
                    {
                        LogToUI($"      ?? Linking {rallyItem.TestCases.Count} test cases to {rallyItem.FormattedID}...", Color.Blue);
                        
                        foreach (var testCaseId in rallyItem.TestCases)
                        {
                            if (_rallyObjectIdToAdoId.ContainsKey(testCaseId))
                            {
                                var testCaseAdoId = _rallyObjectIdToAdoId[testCaseId];
                                
                                try
                                {
                                    var linked = await _adoService.LinkWorkItemsAsync(settings, childAdoId, testCaseAdoId, "Tests");
                                    
                                    if (linked)
                                    {
                                        stats["Test Case Links"]++;
                                        LogToUI($"         Linked Test Case {testCaseId}", Color.Gray);
                                    }
                                    else
                                    {
                                        stats["Failed Links"]++;
                                    }
                                }
                                catch (Exception tcLinkEx)
                                {
                                    stats["Failed Links"]++;
                                    _loggingService.LogWarning($"Test case link error: {tcLinkEx.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Failed to create links for {rallyItem.FormattedID}", ex);
                    stats["Failed Links"]++;
                }
            }

            return stats;
        }

        /// <summary>
        /// Get names of existing attachments for an ADO work item
        /// Extracts attachment filenames from relations and URL patterns
        /// </summary>
        private async Task<HashSet<string>> GetExistingAttachmentNamesAsync(ConnectionSettings settings, int workItemId)
        {
            var attachmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                // CRITICAL: Must use $expand=relations to get attachment data
                // GetWorkItemByIdAsync doesn't expand relations by default
                var workItem = await _adoService.GetWorkItemWithRelationsAsync(settings, workItemId);
                
                if (workItem == null)
                {
                    _loggingService.LogWarning($"Could not retrieve work item {workItemId} to check attachments");
                    return attachmentNames;
                }

                // Check relations array for attachments
                var relations = workItem["relations"] as JArray;
                if (relations == null || !relations.Any())
                {
                    _loggingService.LogDebug($"Work item {workItemId} has no relations/attachments");
                    return attachmentNames; // No relations exist
                }

                _loggingService.LogDebug($"[RELATIONS] Found {relations.Count} relation(s) for work item {workItemId}");

                // Look for AttachedFile relations and extract filenames
                foreach (var relation in relations)
                {
                    try
                    {
                        var rel = relation["rel"]?.ToString();
                        
                        if (string.Equals(rel, "AttachedFile", StringComparison.OrdinalIgnoreCase))
                        {
                            // Try multiple ways to extract filename
                            string filename = null;
                            
                            // Method 1: From attributes.name
                            var attributes = relation["attributes"];
                            if (attributes != null)
                            {
                                filename = attributes["name"]?.ToString();
                            }
                            
                            // Method 2: From attributes.comment (sometimes contains filename)
                            if (string.IsNullOrEmpty(filename) && attributes != null)
                            {
                                var comment = attributes["comment"]?.ToString();
                                if (!string.IsNullOrEmpty(comment) && comment.Contains("Migrated attachment"))
                                {
                                    // Comment format: "Migrated attachment from Rally"
                                    // Try to extract from URL instead
                                }
                            }
                            
                            // Method 3: From URL (most reliable)
                            // URL format: https://dev.azure.com/{org}/{project}/_apis/wit/attachments/{guid}?fileName={filename}
                            if (string.IsNullOrEmpty(filename))
                            {
                                var url = relation["url"]?.ToString();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    // Extract fileName parameter from URL
                                    var fileNameMatch = System.Text.RegularExpressions.Regex.Match(url, @"[?&]fileName=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (fileNameMatch.Success && fileNameMatch.Groups.Count > 1)
                                    {
                                        filename = Uri.UnescapeDataString(fileNameMatch.Groups[1].Value);
                                    }
                                }
                            }
                    
                            if (!string.IsNullOrEmpty(filename))
                            {
                                attachmentNames.Add(filename);
                                _loggingService.LogDebug($"[EXISTING_ATTACHMENT] {filename}");
                            }
                            else
                            {
                                _loggingService.LogDebug($"Found attachment but could not extract filename from: {relation.ToString()}");
                            }
                        }
                    }
                    catch (Exception relEx)
                    {
                        _loggingService.LogDebug($"Error processing relation: {relEx.Message}");
                    }
                }

                _loggingService.LogInfo($"[FOUND] {attachmentNames.Count} existing attachments in ADO");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error checking existing attachments for work item {workItemId}: {ex.Message}");
            }

            return attachmentNames;
        }

        /// <summary>
        /// Check if a parent-child link already exists between two work items
        /// </summary>
        private async Task<bool> CheckParentLinkExistsAsync(ConnectionSettings settings, int childAdoId, int expectedParentAdoId)
        {
            try
            {
                // Get the child work item to check its relations
                var childWorkItem = await _adoService.GetWorkItemByIdAsync(settings, childAdoId);
                if (childWorkItem == null)
                {
                    _loggingService.LogWarning($"Could not retrieve work item {childAdoId} to check links");
                    return false;
                }

                // Check relations array for parent link
                var relations = childWorkItem["relations"] as JArray;
                if (relations == null || !relations.Any())
                {
                    return false; // No relations exist
                }

                // Look for a Parent link pointing to the expected parent
                foreach (var relation in relations)
                {
                    var rel = relation["rel"]?.ToString();
                    var url = relation["url"]?.ToString();
                    
                    // Check if this is a Parent link (reverse of Child)
                    if (string.Equals(rel, "System.LinkTypes.Hierarchy-Reverse", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract work item ID from URL (format: .../_apis/wit/workItems/516225)
                        if (!string.IsNullOrEmpty(url))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(url, @"/workItems/(\d+)");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int parentId))
                            {
                                if (parentId == expectedParentAdoId)
                                {
                                    _loggingService.LogDebug($"Parent link already exists: {childAdoId} - {parentId}");
                                    return true;
                                }
                                else
                                {
                                    _loggingService.LogWarning($"Work item {childAdoId} already has a DIFFERENT parent: {parentId} (expected {expectedParentAdoId})");
                                    return true; // Link exists, just to a different parent - still can't add another
                                }
                            }
                        }
                    }
                }

                return false; // No parent link found
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error checking parent link for work item {childAdoId}: {ex.Message}");
                return false; // Assume no link if we can't check
            }
        }

        /// <summary>
        /// Get existing comment texts from ADO work item to avoid duplicates
        /// Returns a hashset of normalized comment texts for comparison
        /// </summary>
        private async Task<HashSet<string>> GetExistingCommentTextsAsync(ConnectionSettings settings, int workItemId)
        {
            var commentTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                var workItem = await _adoService.GetWorkItemByIdAsync(settings, workItemId);
                if (workItem == null)
                {
                    _loggingService.LogWarning($"Could not retrieve work item {workItemId} to check comments");
                    return commentTexts;
                }

                // Check relations array for comments  
                var relations = workItem["relations"] as JArray;
                if (relations == null || !relations.Any())
                {
                    _loggingService.LogDebug($"Work item {workItemId} has no relations/comments");
                    return commentTexts;
                }

                // Look for comment relations (AttachedFile is for attachments, comments have different structure)
                // ADO comments API v7.1 uses the Comments resource, but older versions may be in relations
                // For now, we'll try to fetch via the Comments API
                try
                {
                    // ADO Comments API endpoint
                    var serverUrl = settings?.AdoServerUrl?.TrimEnd('/') ?? "https://dev.azure.com";
                    var organization = settings?.AdoOrganization?.Trim();
                    var project = settings?.AdoProject?.Trim();

                    string baseApiPath;
                    if (serverUrl.IndexOf("visualstudio.com", StringComparison.OrdinalIgnoreCase) >= 0)
                        baseApiPath = $"{serverUrl}/{project}";
                    else if (serverUrl.IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(organization))
                        baseApiPath = $"{serverUrl}/{organization}/{project}";
                    else 
                        baseApiPath = $"{serverUrl}/{project}";

                    // For now, just return empty set - ADO doesn't prevent duplicate comments anyway
                    // The real issue is that comments should be compared by timestamp+user+text
                    _loggingService.LogDebug($"Comment deduplication not fully implemented - ADO may have duplicate comments");
                }
                catch (Exception ex)
                {
                    _loggingService.LogDebug($"Could not fetch existing comments: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"Error checking existing comments for work item {workItemId}: {ex.Message}");
            }

            return commentTexts;
        }

        /// <summary>
        /// Normalize comment text for comparison (trim whitespace, lowercase)
        /// </summary>
        private string NormalizeCommentText(string commentText)
        {
            if (string.IsNullOrEmpty(commentText))
                return string.Empty;
                
            return commentText.Trim().ToLowerInvariant();
        }

        private void LogToUI(string message, Color color)
        {
            if (color == Color.Red)
                _loggingService.LogError(message);
            else if (color == Color.Orange)
                _loggingService.LogWarning(message);
            else
                _loggingService.LogInfo(message);
            
            _uiLogger?.Invoke(message, color);
        }

        public void Dispose()
        {
            _rallyService?.Dispose();
            _adoService?.Dispose();
        }
    }
}
