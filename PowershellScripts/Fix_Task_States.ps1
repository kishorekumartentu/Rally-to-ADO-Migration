# Fix Task States - Set Completed Tasks to Closed
# This script finds Tasks with RemainingWork=0 and State=Active, then sets them to Closed

param(
    [Parameter(Mandatory=$true)]
    [string]$AdoPat,
    
    [Parameter(Mandatory=$false)]
    [string]$Organization = "emisgroup",
    
    [Parameter(Mandatory=$false)]
    [string]$Project = "Acute Meds Management",
    
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

# Create auth header
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$AdoPat"))
$headers = @{
    Authorization = "Basic $base64AuthInfo"
    "Content-Type" = "application/json-patch+json"
}

Write-Host ""
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Fix Task States - Completed Tasks to Closed" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Organization: $Organization" -ForegroundColor Gray
Write-Host "Project: $Project" -ForegroundColor Gray
Write-Host "Mode: $(if ($DryRun) { 'DRY RUN (no changes)' } else { 'LIVE (will update)' })" -ForegroundColor $(if ($DryRun) { 'Yellow' } else { 'Red' })
Write-Host ""

# Query for Tasks with RemainingWork=0 and State=Active
$wiqlQuery = @{
    query = "SELECT [System.Id], [System.Title], [System.State], [Microsoft.VSTS.Scheduling.RemainingWork] 
             FROM WorkItems 
             WHERE [System.TeamProject] = '$Project' 
             AND [System.WorkItemType] = 'Task' 
             AND [System.State] = 'Active' 
             AND [Microsoft.VSTS.Scheduling.RemainingWork] = 0
             AND [System.Tags] CONTAINS 'Rally-'
             ORDER BY [System.Id]"
} | ConvertTo-Json

$wiqlUrl = "https://$Organization.visualstudio.com/$Project/_apis/wit/wiql?api-version=7.1"

Write-Host "Querying for Tasks with RemainingWork=0 and State=Active..." -ForegroundColor Gray

try {
    $queryResult = Invoke-RestMethod -Uri $wiqlUrl -Method Post -Headers $headers -Body $wiqlQuery -ContentType "application/json"
    
    $workItems = $queryResult.workItems
    
    if (-not $workItems -or $workItems.Count -eq 0) {
        Write-Host "? No Tasks found that need state correction" -ForegroundColor Green
        exit 0
    }
    
    Write-Host "Found $($workItems.Count) Task(s) that need state correction" -ForegroundColor Yellow
    Write-Host ""
    
    $updatedCount = 0
    $failedCount = 0
    
    foreach ($wi in $workItems) {
        $workItemId = $wi.id
        
        # Get full work item details
        $getUrl = "https://$Organization.visualstudio.com/$Project/_apis/wit/workitems/$workItemId`?api-version=7.1"
        $workItem = Invoke-RestMethod -Uri $getUrl -Method Get -Headers $headers
        
        $title = $workItem.fields.'System.Title'
        $tags = $workItem.fields.'System.Tags'
        
        # Extract Rally ID from tags
        $rallyId = ""
        if ($tags) {
            $tagMatch = [regex]::Match($tags, 'Rally-(TA\d+)')
            if ($tagMatch.Success) {
                $rallyId = $tagMatch.Groups[1].Value
            }
        }
        
        Write-Host "[$workItemId] $rallyId - $title" -ForegroundColor White
        Write-Host "   Current State: Active" -ForegroundColor Yellow
        Write-Host "   Remaining Work: 0" -ForegroundColor Cyan
        
        if ($DryRun) {
            Write-Host "   [DRY RUN] Would update to: State=Closed" -ForegroundColor Gray
        }
        else {
            # Update state to Closed
            $patchUrl = "https://$Organization.visualstudio.com/$Project/_apis/wit/workitems/$workItemId`?api-version=7.1"
            
            $patchDocument = @(
                @{
                    op = "add"
                    path = "/fields/System.State"
                    value = "Closed"
                },
                @{
                    op = "add"
                    path = "/fields/System.Reason"
                    value = "Completed"
                }
            ) | ConvertTo-Json
            
            try {
                $patchResult = Invoke-RestMethod -Uri $patchUrl -Method Patch -Headers $headers -Body $patchDocument
                
                $newState = $patchResult.fields.'System.State'
                Write-Host "   ? Updated to: State=$newState" -ForegroundColor Green
                $updatedCount++
            }
            catch {
                Write-Host "   ? Failed to update: $($_.Exception.Message)" -ForegroundColor Red
                $failedCount++
            }
        }
        
        Write-Host ""
    }
    
    Write-Host "=============================================================" -ForegroundColor Cyan
    Write-Host "Summary" -ForegroundColor Cyan
    Write-Host "=============================================================" -ForegroundColor Cyan
    
    if ($DryRun) {
        Write-Host "DRY RUN: Found $($workItems.Count) Task(s) that would be updated" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Run without -DryRun to actually update the Tasks" -ForegroundColor Gray
    }
    else {
        Write-Host "Total Tasks Found: $($workItems.Count)" -ForegroundColor White
        Write-Host "Successfully Updated: $updatedCount" -ForegroundColor Green
        Write-Host "Failed: $failedCount" -ForegroundColor $(if ($failedCount -gt 0) { 'Red' } else { 'White' })
    }
    
} catch {
    Write-Host "? Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=============================================================" -ForegroundColor Cyan
Write-Host "Complete" -ForegroundColor Cyan
Write-Host "=============================================================" -ForegroundColor Cyan
