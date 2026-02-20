# Rally Task State Query Comparison Test
# This script tests BOTH FormattedID and ObjectID queries to find the discrepancy
# The migration tool uses ObjectID queries for dependencies, which may return stale data

param(
    [Parameter(Mandatory=$true)]
    [string]$RallyApiKey,
    
    [Parameter(Mandatory=$true)]
    [string]$TaskFormattedID,  # e.g., "TA29099015"
    
    [Parameter(Mandatory=$false)]
    [string]$WorkspaceId = "14457696030",
    
    [Parameter(Mandatory=$false)]
    [string]$ProjectId = "825562720841"
)

$ErrorActionPreference = "Continue"

# Rally API base URL
$rallyBaseUrl = "https://rally1.rallydev.com/slm/webservice/v2.0"

# Create authentication header
$authHeader = @{
    "ZSESSIONID" = $RallyApiKey
}

Write-Host ""
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Rally Task State API Comparison Test" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Task: $TaskFormattedID" -ForegroundColor Yellow
Write-Host "Workspace: $WorkspaceId" -ForegroundColor Gray
Write-Host "Project: $ProjectId" -ForegroundColor Gray
Write-Host ""

# ============================================================
# TEST 1: Query API (what the migration tool uses normally)
# ============================================================
Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "TEST 1: Query API (normal migration path)" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan

$queryUrl = "$rallyBaseUrl/task" +
    "?workspace=/workspace/$WorkspaceId" +
    "&project=/project/$ProjectId" +
    "&query=(FormattedID = `"$TaskFormattedID`")" +
    "&fetch=ObjectID,FormattedID,Name,State,ScheduleState,TaskStatus,WorkflowState,Status,ActualState,Estimate,ToDo,Actuals"

Write-Host "Query URL: $queryUrl" -ForegroundColor Gray
Write-Host ""

try {
    $queryResponse = Invoke-RestMethod -Uri $queryUrl -Method Get -Headers $authHeader -ContentType "application/json"
    
    if ($queryResponse.QueryResult.TotalResultCount -eq 0) {
        Write-Host "? Task not found via Query API!" -ForegroundColor Red
        exit 1
    }
    
    $taskFromQuery = $queryResponse.QueryResult.Results[0]
    
    Write-Host "? Query API Response:" -ForegroundColor Green
    Write-Host "   ObjectID: $($taskFromQuery.ObjectID)" -ForegroundColor White
    Write-Host "   FormattedID: $($taskFromQuery.FormattedID)" -ForegroundColor White
    Write-Host "   Name: $($taskFromQuery.Name)" -ForegroundColor White
    Write-Host ""
    Write-Host "   STATE FIELDS:" -ForegroundColor Yellow
    Write-Host "   - State: '$($taskFromQuery.State)'" -ForegroundColor $(if ($taskFromQuery.State -eq 'Completed') { 'Green' } else { 'Yellow' })
    Write-Host "   - ScheduleState: '$($taskFromQuery.ScheduleState)'" -ForegroundColor Gray
    Write-Host "   - TaskStatus: '$($taskFromQuery.TaskStatus)'" -ForegroundColor $(if ($taskFromQuery.TaskStatus -eq 'COMPLETED') { 'Green' } else { 'Yellow' })
    Write-Host "   - WorkflowState: '$($taskFromQuery.WorkflowState)'" -ForegroundColor Gray
    Write-Host "   - Status: '$($taskFromQuery.Status)'" -ForegroundColor Gray
    Write-Host "   - ActualState: '$($taskFromQuery.ActualState)'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "   TIME TRACKING:" -ForegroundColor Yellow
    Write-Host "   - Estimate: $($taskFromQuery.Estimate)" -ForegroundColor White
    Write-Host "   - ToDo: $($taskFromQuery.ToDo)" -ForegroundColor $(if ($taskFromQuery.ToDo -eq 0) { 'Cyan' } else { 'White' })
    Write-Host "   - Actuals: $($taskFromQuery.Actuals)" -ForegroundColor White
    Write-Host ""
    
    # Staleness detection
    if ($taskFromQuery.ToDo -eq 0 -and ($taskFromQuery.TaskStatus -eq 'IN_PROGRESS' -or $taskFromQuery.State -eq 'In-Progress')) {
        Write-Host "??  STALE STATE DETECTED!" -ForegroundColor Red
        Write-Host "   ToDo=0 but State is not 'Completed' - this indicates stale cache" -ForegroundColor Red
        Write-Host ""
    }
    
    # Save ObjectID for direct API test
    $objectId = $taskFromQuery.ObjectID
    
} catch {
    Write-Host "? Query API Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.Response
    exit 1
}

Write-Host ""
Write-Host "Waiting 2 seconds..." -ForegroundColor Gray
Start-Sleep -Seconds 2
Write-Host ""

# ============================================================
# TEST 2: Direct Read API (bypass cache)
# ============================================================
Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "TEST 2: Direct Read API (cache bypass)" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan

if (-not $objectId) {
    Write-Host "? Cannot run Direct Read test - no ObjectID from Query API" -ForegroundColor Red
    exit 1
}

$directUrl = "$rallyBaseUrl/task/$objectId" +
    "?fetch=ObjectID,FormattedID,Name,State,ScheduleState,TaskStatus,WorkflowState,Status,ActualState,Estimate,ToDo,Actuals"

Write-Host "Direct Read URL: $directUrl" -ForegroundColor Gray
Write-Host ""

try {
    $directResponse = Invoke-RestMethod -Uri $directUrl -Method Get -Headers $authHeader -ContentType "application/json"
    
    $taskFromDirect = $directResponse.Task
    
    Write-Host "? Direct Read API Response:" -ForegroundColor Green
    Write-Host "   ObjectID: $($taskFromDirect.ObjectID)" -ForegroundColor White
    Write-Host "   FormattedID: $($taskFromDirect.FormattedID)" -ForegroundColor White
    Write-Host "   Name: $($taskFromDirect.Name)" -ForegroundColor White
    Write-Host ""
    Write-Host "   STATE FIELDS:" -ForegroundColor Yellow
    Write-Host "   - State: '$($taskFromDirect.State)'" -ForegroundColor $(if ($taskFromDirect.State -eq 'Completed') { 'Green' } else { 'Yellow' })
    Write-Host "   - ScheduleState: '$($taskFromDirect.ScheduleState)'" -ForegroundColor Gray
    Write-Host "   - TaskStatus: '$($taskFromDirect.TaskStatus)'" -ForegroundColor $(if ($taskFromDirect.TaskStatus -eq 'COMPLETED') { 'Green' } else { 'Yellow' })
    Write-Host "   - WorkflowState: '$($taskFromDirect.WorkflowState)'" -ForegroundColor Gray
    Write-Host "   - Status: '$($taskFromDirect.Status)'" -ForegroundColor Gray
    Write-Host "   - ActualState: '$($taskFromDirect.ActualState)'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "   TIME TRACKING:" -ForegroundColor Yellow
    Write-Host "   - Estimate: $($taskFromDirect.Estimate)" -ForegroundColor White
    Write-Host "   - ToDo: $($taskFromDirect.ToDo)" -ForegroundColor $(if ($taskFromDirect.ToDo -eq 0) { 'Cyan' } else { 'White' })
    Write-Host "   - Actuals: $($taskFromDirect.Actuals)" -ForegroundColor White
    Write-Host ""
    
} catch {
    Write-Host "? Direct Read API Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================================
# COMPARISON & ANALYSIS
# ============================================================
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "COMPARISON & ANALYSIS" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host ""

# Compare State
Write-Host "State Field Comparison:" -ForegroundColor Yellow
Write-Host "   Query API:  '$($taskFromQuery.State)'" -ForegroundColor White
Write-Host "   Direct API: '$($taskFromDirect.State)'" -ForegroundColor White
if ($taskFromQuery.State -ne $taskFromDirect.State) {
    Write-Host "   ? DIFFERENT! APIs returned different State values" -ForegroundColor Red
} else {
    Write-Host "   ? Same" -ForegroundColor Green
}
Write-Host ""

# Compare TaskStatus
Write-Host "TaskStatus Field Comparison:" -ForegroundColor Yellow
Write-Host "   Query API:  '$($taskFromQuery.TaskStatus)'" -ForegroundColor White
Write-Host "   Direct API: '$($taskFromDirect.TaskStatus)'" -ForegroundColor White
if ($taskFromQuery.TaskStatus -ne $taskFromDirect.TaskStatus) {
    Write-Host "   ? DIFFERENT! APIs returned different TaskStatus values" -ForegroundColor Red
} else {
    Write-Host "   ? Same" -ForegroundColor Green
}
Write-Host ""

# Compare ScheduleState
Write-Host "ScheduleState Field Comparison:" -ForegroundColor Yellow
Write-Host "   Query API:  '$($taskFromQuery.ScheduleState)'" -ForegroundColor White
Write-Host "   Direct API: '$($taskFromDirect.ScheduleState)'" -ForegroundColor White
if ($taskFromQuery.ScheduleState -ne $taskFromDirect.ScheduleState) {
    Write-Host "   ? DIFFERENT! APIs returned different ScheduleState values" -ForegroundColor Red
} else {
    Write-Host "   ? Same" -ForegroundColor Green
}
Write-Host ""

# ============================================================
# WHAT MIGRATION TOOL WILL USE
# ============================================================
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "WHAT MIGRATION TOOL WILL USE" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host ""

# Simulate the NEW FIXED parsing logic from RallyApiService.Parsing.cs
$rawState = $taskFromQuery.State
$scheduleState = $taskFromQuery.ScheduleState
$taskStatus = $taskFromQuery.TaskStatus

Write-Host "Simulating FIXED RallyApiService.Parsing.cs logic:" -ForegroundColor Yellow
Write-Host "   Raw State field: '$rawState'" -ForegroundColor Gray
Write-Host "   ScheduleState field: '$scheduleState'" -ForegroundColor Gray
Write-Host "   TaskStatus field: '$taskStatus'" -ForegroundColor Gray
Write-Host ""

$finalState = $rawState

# FIXED LOGIC: For Tasks, prioritize State field (source of truth)
if ($rawState) {
    # Use State field as primary source (contains correct "Completed" value)
    Write-Host "? Tool will use State field: '$rawState' (PRIMARY for Tasks)" -ForegroundColor Green
    $finalState = $rawState
}
# Fallback to TaskStatus if State is empty
elseif ($taskStatus) {
    # Normalize: IN_PROGRESS -> In-Progress, COMPLETED -> Completed
    $normalizedTaskStatus = $taskStatus.Replace("_", "-")
    
    # Split by dash and capitalize each word
    $words = $normalizedTaskStatus.Split('-')
    $capitalizedWords = $words | ForEach-Object {
        $word = $_.ToLower()
        if ($word.Length -gt 0) {
            $word.Substring(0,1).ToUpper() + $word.Substring(1)
        }
    }
    $normalizedTaskStatus = $capitalizedWords -join '-'
    
    Write-Host "? Tool will use TaskStatus (State was empty): '$taskStatus' (normalized to '$normalizedTaskStatus')" -ForegroundColor Yellow
    $finalState = $normalizedTaskStatus
}
# Last resort fallback to ScheduleState
elseif ($scheduleState) {
    Write-Host "? Tool will use ScheduleState (fallback): '$scheduleState'" -ForegroundColor Yellow
    $finalState = $scheduleState
}
else {
    Write-Host "?? No state field found - using default" -ForegroundColor Red
    $finalState = "Active" # Default fallback
}

Write-Host ""
Write-Host "Final Rally State for migration: '$finalState'" -ForegroundColor Cyan
Write-Host ""

# Map to ADO state
$adoStateMapping = @{
    "Defined" = "New"
    "In-Progress" = "Active"
    "Completed" = "Closed"
}

$adoState = $adoStateMapping[$finalState]
if (-not $adoState) {
    $adoState = "Active" # Default fallback
}

Write-Host "ADO State Mapping (for Tasks):" -ForegroundColor Yellow
Write-Host "   Rally: '$finalState' ? ADO: '$adoState'" -ForegroundColor $(if ($adoState -eq 'Closed') { 'Green' } else { 'Yellow' })
Write-Host ""

# ============================================================
# RECOMMENDATIONS
# ============================================================
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "RECOMMENDATIONS" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host ""

if ($adoState -ne "Closed" -and $taskFromQuery.ToDo -eq 0) {
    Write-Host "??  ISSUE DETECTED:" -ForegroundColor Red
    Write-Host "   - Task has ToDo=0 (no remaining work)" -ForegroundColor Red
    Write-Host "   - But Rally API returns state as '$finalState' (not 'Completed')" -ForegroundColor Red
    Write-Host "   - This will migrate to ADO as '$adoState' instead of 'Closed'" -ForegroundColor Red
    Write-Host ""
    Write-Host "   POSSIBLE CAUSES:" -ForegroundColor Yellow
    Write-Host "   1. Rally API cache is stale (most likely)" -ForegroundColor White
    Write-Host "   2. Task workflow in Rally allows In-Progress with ToDo=0" -ForegroundColor White
    Write-Host "   3. Task was updated very recently (< 5 minutes ago)" -ForegroundColor White
    Write-Host ""
    Write-Host "   SOLUTIONS:" -ForegroundColor Yellow
    Write-Host "   1. Edit the task in Rally UI (add/remove a space), Save, wait 60s, re-test" -ForegroundColor White
    Write-Host "   2. Wait 15 minutes for Rally cache to refresh naturally" -ForegroundColor White
    Write-Host "   3. Use the automatic retry logic (already implemented in tool)" -ForegroundColor White
    Write-Host ""
}
else {
    Write-Host "? State appears correct!" -ForegroundColor Green
    Write-Host "   Task will migrate to ADO with State='$adoState'" -ForegroundColor Green
    Write-Host ""
}

# Check if Direct API would fix it
if ($taskFromQuery.TaskStatus -ne $taskFromDirect.TaskStatus) {
    Write-Host "?? Direct Read API FIX AVAILABLE:" -ForegroundColor Cyan
    Write-Host "   Direct API returns: '$($taskFromDirect.TaskStatus)'" -ForegroundColor Cyan
    Write-Host "   This would map to ADO State: '$($adoStateMapping[$taskFromDirect.TaskStatus.Replace('_','-')])'" -ForegroundColor Cyan
    Write-Host "   ? The automatic retry logic in the tool should handle this!" -ForegroundColor Green
    Write-Host ""
}

Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
