using System;
using System.Net.Http;
using System.Threading.Tasks;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Direct Read API support for RallyApiService
    /// STALE CACHE DETECTION DISABLED PER USER REQUEST
    /// The State field is the reliable source of truth for Task state in this Rally instance
    /// </summary>
    public partial class RallyApiService
    {
        /* STALE CACHE DETECTION - COMMENTED OUT PER USER REQUEST
         * 
         * User confirmed this is not a stale cache issue.
         * The Rally API returns the correct state in the "State" field.
         * Previous logic was incorrectly prioritizing TaskStatus field over State field.
         * 
         * Original implementation attempted to detect stale Rally API cache by comparing
         * ToDo field with State field, but this was unnecessary complexity.
         * 
        /// <summary>
        /// Detect if a Task's state might be stale based on time tracking data and state
        /// Rally API query cache can be stale, but direct Read API is often fresher
        /// PUBLIC so TwoPhaseHierarchicalMigrationService can use it
        /// </summary>
        public bool IsPotentiallyStaleTaskState(RallyWorkItem task)
        {
            if (task == null || string.IsNullOrEmpty(task.State))
                return false;

            // Only check Tasks
            if (!string.Equals(task.Type, "Task", StringComparison.OrdinalIgnoreCase))
                return false;

            var state = task.State.Trim();
            
            _loggingService.LogDebug($"[STALE_CHECK] Checking Task {task.FormattedID} for potentially stale state...");
            _loggingService.LogDebug($"   State: '{state}'");
            _loggingService.LogDebug($"   Estimate: {task.Estimate?.ToString() ?? "null"}");
            _loggingService.LogDebug($"   ToDo: {task.ToDo?.ToString() ?? "null"}");
            _loggingService.LogDebug($"   Actuals: {task.Actuals?.ToString() ?? "null"}");

            // Indicator 1: Task shows "In-Progress" but has no remaining work (ToDo = 0)
            // This often means the task was completed but Rally cache hasn't updated
            if (string.Equals(state, "In-Progress", StringComparison.OrdinalIgnoreCase))
            {
                if (task.ToDo.HasValue && task.ToDo.Value == 0)
                {
                    _loggingService.LogInfo($"[STALE_INDICATOR_1] Task {task.FormattedID}: State='In-Progress' but ToDo=0 (no remaining work)");
                    _loggingService.LogInfo($"   This often indicates the task is actually Completed but Rally cache is stale");
                    return true;
                }

                // Indicator 2: Task has actuals but no ToDo and still shows In-Progress
                if (task.Actuals.HasValue && task.Actuals.Value > 0 && 
                    task.ToDo.HasValue && task.ToDo.Value == 0)
                {
                    _loggingService.LogInfo($"[STALE_INDICATOR_2] Task {task.FormattedID}: Has Actuals ({task.Actuals}) but ToDo=0, still In-Progress");
                    _loggingService.LogInfo($"   This suggests task was marked complete but cache hasn't refreshed");
                    return true;
                }
            }

            // Indicator 3: Task shows "Defined" but has actuals (work completed)
            // This is rare but can happen if someone logs time before updating state
            if (string.Equals(state, "Defined", StringComparison.OrdinalIgnoreCase))
            {
                if (task.Actuals.HasValue && task.Actuals.Value > 0)
                {
                    _loggingService.LogInfo($"[STALE_INDICATOR_3] Task {task.FormattedID}: State='Defined' but has Actuals ({task.Actuals})");
                    _loggingService.LogInfo($"   Task has logged work but state wasn't updated - may be stale");
                    return true;
                }
            }

            _loggingService.LogDebug($"[STALE_CHECK] No stale indicators found for {task.FormattedID}");
            return false;
        }
        */
    }
}

