# ADO Task Workflow Validation Script
# This script checks if ADO allows "New" -> "Closed" state transition for Tasks

param(
    [Parameter(Mandatory=$true)]
    [string]$AdoPat,
    
    [Parameter(Mandatory=$true)]
    [string]$AdoOrganization,
    
    [Parameter(Mandatory=$true)]
    [string]$AdoProject,
    
    [Parameter(Mandatory=$false)]
    [int]$TestTaskId  # Optional: Test with an existing Task
)

$ErrorActionPreference = "Continue"

# ADO API base URL
$adoBaseUrl = "https://dev.azure.com/$AdoOrganization"

# Create authentication header
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$AdoPat"))
$authHeader = @{
    "Authorization" = "Basic $base64AuthInfo"
    "Content-Type" = "application/json-patch+json"
}

Write-Host ""
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "ADO Task State Workflow Validation" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Organization: $AdoOrganization" -ForegroundColor Yellow
Write-Host "Project: $AdoProject" -ForegroundColor Yellow
Write-Host ""

# ============================================================
# TEST 1: Get Work Item Type Definition for Task
# ============================================================
Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "TEST 1: Getting Task Work Item Type Definition" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

$witUrl = "$adoBaseUrl/$AdoProject/_apis/wit/workitemtypes/Task?api-version=7.0"
Write-Host "URL: $witUrl" -ForegroundColor Gray
Write-Host ""

try {
    $witResponse = Invoke-RestMethod -Uri $witUrl -Method Get -Headers $authHeader
    
    Write-Host "? Task work item type found" -ForegroundColor Green
    Write-Host ""
    
    # Get states
    Write-Host "Available States for Task:" -ForegroundColor Yellow
    $states = $witResponse.states
    foreach ($state in $states) {
        $color = switch ($state.name) {
            "New" { "Green" }
            "Active" { "Cyan" }
            "Resolved" { "Yellow" }
            "Closed" { "Magenta" }
            "Removed" { "Gray" }
            default { "White" }
        }
        Write-Host "   - $($state.name) $(if ($state.category) { "(Category: $($state.category))" })" -ForegroundColor $color
    }
    Write-Host ""
    
    # Get state transitions
    Write-Host "State Transitions:" -ForegroundColor Yellow
    if ($witResponse.transitions) {
        foreach ($fromState in $witResponse.transitions.PSObject.Properties) {
            $stateName = $fromState.Name
            $toStates = $fromState.Value
            
            Write-Host "   From '$stateName' you can go to:" -ForegroundColor White
            foreach ($toState in $toStates) {
                $color = "Gray"
                if ($stateName -eq "New" -and $toState -eq "Closed") {
                    $color = "Red"
                    Write-Host "      -> $toState ?? THIS IS THE KEY TRANSITION!" -ForegroundColor $color
                } else {
                    Write-Host "      -> $toState" -ForegroundColor $color
                }
            }
        }
    } else {
        Write-Host "   ?? No explicit transition rules found (might allow all transitions)" -ForegroundColor Yellow
    }
    Write-Host ""
    
    # Check if New -> Closed is allowed
    $newToClosedAllowed = $false
    if ($witResponse.transitions -and $witResponse.transitions.New) {
        $newToClosedAllowed = $witResponse.transitions.New -contains "Closed"
    }
    
    Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "CRITICAL CHECK: Can Task go from 'New' -> 'Closed'?" -ForegroundColor Cyan
    Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
    if ($newToClosedAllowed) {
        Write-Host "? YES - 'New' -> 'Closed' transition is ALLOWED" -ForegroundColor Green
        Write-Host "   The migration tool should work correctly" -ForegroundColor Green
    } else {
        Write-Host "? NO - 'New' -> 'Closed' transition is NOT ALLOWED" -ForegroundColor Red
        Write-Host "   This explains why Tasks are staying as 'Active'!" -ForegroundColor Red
        Write-Host ""
        Write-Host "RECOMMENDED SOLUTION:" -ForegroundColor Yellow
        Write-Host "   The migration tool needs to use intermediate state:" -ForegroundColor Yellow
        Write-Host "   1. Create Task with State='New'" -ForegroundColor White
        Write-Host "   2. Update State to 'Active'" -ForegroundColor White
        Write-Host "   3. Update State to 'Closed'" -ForegroundColor White
        Write-Host ""
        Write-Host "   OR check if 'Bypass rules when pushing' permission helps" -ForegroundColor Yellow
    }
    Write-Host ""
    
} catch {
    Write-Host "? Error getting work item type: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
}

# ============================================================
# TEST 2: Try to create a test Task and set it to Closed
# ============================================================
if ($TestTaskId -and $TestTaskId -gt 0) {
    Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "TEST 2: Testing State Transition on Existing Task" -ForegroundColor Cyan
    Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "Testing with Task ID: $TestTaskId" -ForegroundColor Yellow
    Write-Host ""
    
    # Get current state
    $getUrl = "$adoBaseUrl/$AdoProject/_apis/wit/workitems/${TestTaskId}?api-version=7.0"
    try {
        $currentTask = Invoke-RestMethod -Uri $getUrl -Method Get -Headers $authHeader
        $currentState = $currentTask.fields.'System.State'
        Write-Host "Current State: '$currentState'" -ForegroundColor White
        Write-Host ""
        
        # Try to update to Closed with bypass rules
        Write-Host "Attempting to update State to 'Closed' WITH bypass rules..." -ForegroundColor Yellow
        
        $patchUrl = "$adoBaseUrl/$AdoProject/_apis/wit/workitems/${TestTaskId}?bypassRules=true&api-version=7.0"
        $patchBody = @(
            @{
                op = "add"
                path = "/fields/System.State"
                value = "Closed"
            }
        ) | ConvertTo-Json -Depth 10
        
        try {
            $patchResponse = Invoke-RestMethod -Uri $patchUrl -Method Patch -Headers $authHeader -Body $patchBody
            $newState = $patchResponse.fields.'System.State'
            
            Write-Host "? Success! State updated to: '$newState'" -ForegroundColor Green
            
            if ($newState -eq "Closed") {
                Write-Host "   ? State is now 'Closed' as expected!" -ForegroundColor Green
            } else {
                Write-Host "   ?? State changed but not to 'Closed'! It's: '$newState'" -ForegroundColor Yellow
                Write-Host "   This suggests ADO workflow rules overrode the transition" -ForegroundColor Yellow
            }
            Write-Host ""
            
            # Restore original state
            Write-Host "Restoring original state '$currentState'..." -ForegroundColor Gray
            $restoreBody = @(
                @{
                    op = "add"
                    path = "/fields/System.State"
                    value = $currentState
                }
            ) | ConvertTo-Json -Depth 10
            $restoreResponse = Invoke-RestMethod -Uri $patchUrl -Method Patch -Headers $authHeader -Body $restoreBody
            Write-Host "? Restored to: '$($restoreResponse.fields.'System.State')'" -ForegroundColor Gray
            Write-Host ""
            
        } catch {
            Write-Host "? Failed to update state with bypass rules" -ForegroundColor Red
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
            
            # Check if it's a workflow validation error
            if ($_.Exception.Message -match "TF401320" -or $_.Exception.Message -match "transition") {
                Write-Host ""
                Write-Host "?? WORKFLOW VALIDATION ERROR DETECTED" -ForegroundColor Red
                Write-Host "   ADO process template does not allow 'New' -> 'Closed' transition" -ForegroundColor Red
                Write-Host "   Even with bypass rules enabled!" -ForegroundColor Red
                Write-Host ""
                Write-Host "SOLUTION:" -ForegroundColor Yellow
                Write-Host "   Modify migration tool to use intermediate state transitions:" -ForegroundColor Yellow
                Write-Host "   'New' -> 'Active' -> 'Closed'" -ForegroundColor Yellow
            }
            
            # Check if it's a permission error
            if ($_.Exception.Message -match "TF401335" -or $_.Exception.Message -match "permission") {
                Write-Host ""
                Write-Host "?? PERMISSION ERROR DETECTED" -ForegroundColor Red
                Write-Host "   User does not have 'Bypass rules when pushing' permission" -ForegroundColor Red
                Write-Host ""
                Write-Host "SOLUTION:" -ForegroundColor Yellow
                Write-Host "   Grant 'Bypass rules when pushing' permission to the PAT user" -ForegroundColor Yellow
                Write-Host "   In ADO: Project Settings -> Permissions -> User -> Edit" -ForegroundColor Yellow
            }
            Write-Host ""
        }
        
    } catch {
        Write-Host "? Error accessing Task $TestTaskId : $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "TEST 2: Skipped (no TestTaskId provided)" -ForegroundColor Gray
    Write-Host "-------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "To test with an existing Task, provide -TestTaskId parameter" -ForegroundColor Gray
    Write-Host ""
}

# ============================================================
# SUMMARY & RECOMMENDATIONS
# ============================================================
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "SUMMARY & RECOMMENDATIONS" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "POSSIBLE ISSUES:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. ADO Process Template Restrictions" -ForegroundColor White
Write-Host "   - ADO may not allow 'New' -> 'Closed' state transition" -ForegroundColor Gray
Write-Host "   - Check the workflow rules in your process template" -ForegroundColor Gray
Write-Host "   - Solution: Use intermediate state ('New' -> 'Active' -> 'Closed')" -ForegroundColor Gray
Write-Host ""

Write-Host "2. Missing 'Bypass rules when pushing' Permission" -ForegroundColor White
Write-Host "   - Migration PAT user may lack permission to bypass workflow rules" -ForegroundColor Gray
Write-Host "   - Solution: Grant permission in Project Settings -> Permissions" -ForegroundColor Gray
Write-Host ""

Write-Host "3. Required Fields for 'Closed' State" -ForegroundColor White
Write-Host "   - ADO may require additional fields when transitioning to 'Closed'" -ForegroundColor Gray
Write-Host "   - Common required fields: 'Closed Date', 'Closed By', 'Reason'" -ForegroundColor Gray
Write-Host "   - Solution: Set these fields along with State in the same PATCH request" -ForegroundColor Gray
Write-Host ""

Write-Host "4. Rally API Returning Stale State" -ForegroundColor White
Write-Host "   - Rally Query API may return stale TaskStatus from cache" -ForegroundColor Gray
Write-Host "   - Solution: Use Rally Direct Read API to get fresh data" -ForegroundColor Gray
Write-Host "   - The migration tool has automatic retry logic for this" -ForegroundColor Gray
Write-Host ""

Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Run this script WITH -TestTaskId to test actual state transition" -ForegroundColor Yellow
Write-Host "   Example:" -ForegroundColor Gray
Write-Host "   .\Check_ADO_Task_Workflow.ps1 -AdoPat 'your-pat' -AdoOrganization 'your-org' -AdoProject 'your-project' -TestTaskId 12345" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Check migration tool logs for these indicators:" -ForegroundColor Yellow
Write-Host "   - '[MISMATCH] State mismatch!' = ADO rejected state change" -ForegroundColor Gray
Write-Host "   - '[WARNING] Post-creation patch failed' = Permission issue" -ForegroundColor Gray
Write-Host "   - '[CACHE_CONFLICT]' = Rally API returning stale data" -ForegroundColor Gray
Write-Host ""
Write-Host "3. If ADO doesn't allow 'New' -> 'Closed', update migration code to:" -ForegroundColor Yellow
Write-Host "   - Use intermediate state transition" -ForegroundColor Gray
Write-Host "   - OR grant bypass permission to PAT user" -ForegroundColor Gray
Write-Host ""

Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
