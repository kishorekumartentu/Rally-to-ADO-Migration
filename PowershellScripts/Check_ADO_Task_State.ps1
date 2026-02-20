# Check ADO Task State
# This script checks what state a migrated Task actually has in ADO

param(
    [Parameter(Mandatory=$true)]
    [string]$AdoPat,
    
    [Parameter(Mandatory=$true)]
    [int]$WorkItemId,  # ADO work item ID
    
    [Parameter(Mandatory=$false)]
    [string]$Organization = "emisgroup",
    
    [Parameter(Mandatory=$false)]
    [string]$Project = "Acute Meds Management"
)

$ErrorActionPreference = "Stop"

# ADO API URL
$adoUrl = "https://$Organization.visualstudio.com/$Project/_apis/wit/workitems/$WorkItemId`?api-version=7.1"

# Create auth header
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$AdoPat"))
$headers = @{
    Authorization = "Basic $base64AuthInfo"
}

Write-Host ""
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "ADO Work Item State Check" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Work Item ID: $WorkItemId" -ForegroundColor Yellow
Write-Host "Organization: $Organization" -ForegroundColor Gray
Write-Host "Project: $Project" -ForegroundColor Gray
Write-Host ""

try {
    Write-Host "Fetching work item from ADO..." -ForegroundColor Gray
    $response = Invoke-RestMethod -Uri $adoUrl -Method Get -Headers $headers -ContentType "application/json"
    
    $fields = $response.fields
    
    Write-Host "? Work Item Found" -ForegroundColor Green
    Write-Host ""
    Write-Host "BASIC INFO:" -ForegroundColor Yellow
    Write-Host "   ID: $($response.id)" -ForegroundColor White
    Write-Host "   Type: $($fields.'System.WorkItemType')" -ForegroundColor White
    Write-Host "   Title: $($fields.'System.Title')" -ForegroundColor White
    Write-Host ""
    Write-Host "STATE INFORMATION:" -ForegroundColor Yellow
    Write-Host "   State: '$($fields.'System.State')'" -ForegroundColor $(if ($fields.'System.State' -eq 'Closed') { 'Green' } elseif ($fields.'System.State' -eq 'Active') { 'Yellow' } else { 'White' })
    Write-Host "   Reason: '$($fields.'System.Reason')'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "RALLY TRACEABILITY:" -ForegroundColor Yellow
    
    # Check for Rally tags
    $tags = $fields.'System.Tags'
    if ($tags) {
        $tagArray = $tags -split ';' | ForEach-Object { $_.Trim() }
        $rallyTags = $tagArray | Where-Object { $_ -like "Rally-*" -or $_ -like "RallyObjectID-*" }
        
        if ($rallyTags) {
            Write-Host "   Rally Tags Found:" -ForegroundColor Green
            foreach ($tag in $rallyTags) {
                Write-Host "      - $tag" -ForegroundColor White
            }
        }
        else {
            Write-Host "   No Rally tags found" -ForegroundColor Red
        }
    }
    else {
        Write-Host "   No tags on work item" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "TIME TRACKING (if Task):" -ForegroundColor Yellow
    Write-Host "   Original Estimate: $($fields.'Microsoft.VSTS.Scheduling.OriginalEstimate')" -ForegroundColor White
    Write-Host "   Remaining Work: $($fields.'Microsoft.VSTS.Scheduling.RemainingWork')" -ForegroundColor White
    Write-Host "   Completed Work: $($fields.'Microsoft.VSTS.Scheduling.CompletedWork')" -ForegroundColor White
    Write-Host ""
    
    Write-Host "AUDIT INFO:" -ForegroundColor Yellow
    Write-Host "   Created Date: $($fields.'System.CreatedDate')" -ForegroundColor Gray
    Write-Host "   Created By: $($fields.'System.CreatedBy'.'displayName')" -ForegroundColor Gray
    Write-Host "   Changed Date: $($fields.'System.ChangedDate')" -ForegroundColor Gray
    Write-Host "   Changed By: $($fields.'System.ChangedBy'.'displayName')" -ForegroundColor Gray
    Write-Host ""
    
    # Check if state is wrong
    if ($fields.'System.WorkItemType' -eq 'Task' -and $fields.'System.State' -eq 'Active') {
        $remainingWork = $fields.'Microsoft.VSTS.Scheduling.RemainingWork'
        
        if ($remainingWork -eq 0) {
            Write-Host "??  POTENTIAL ISSUE DETECTED:" -ForegroundColor Red
            Write-Host "   - Work Item Type: Task" -ForegroundColor Red
            Write-Host "   - State: Active" -ForegroundColor Red
            Write-Host "   - Remaining Work: 0" -ForegroundColor Red
            Write-Host ""
            Write-Host "   This Task shows Active but has no remaining work!" -ForegroundColor Red
            Write-Host "   Expected: State should be 'Closed' if task is completed" -ForegroundColor Yellow
            Write-Host ""
        }
    }
    
} catch {
    Write-Host "? Error fetching work item: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Check Complete" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
