# PowerShell script to query ADO for Feature field allowed values
param(
    [string]$Organization = "emisgroup",
    [string]$Project = "Acute Meds Management",
    [string]$PAT = ""
)

if ([string]::IsNullOrEmpty($PAT)) {
    Write-Host "Please provide PAT token" -ForegroundColor Red
    exit 1
}

$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$PAT"))
$headers = @{
    Authorization = "Basic $base64AuthInfo"
    "Content-Type" = "application/json"
}

# Get Feature work item type definition
$url = "https://dev.azure.com/$Organization/$Project/_apis/wit/workitemtypes/Feature?api-version=7.0"

try {
    Write-Host "Querying Feature work item type..." -ForegroundColor Cyan
    $response = Invoke-RestMethod -Uri $url -Method Get -Headers $headers
    
    Write-Host "`nFeature Fields with Allowed Values:" -ForegroundColor Green
    Write-Host "====================================`n" -ForegroundColor Green
    
    foreach ($field in $response.fields) {
        if ($field.allowedValues -and $field.allowedValues.Count -gt 0) {
            Write-Host "Field: $($field.referenceName)" -ForegroundColor Yellow
            Write-Host "Name: $($field.name)" -ForegroundColor White
            Write-Host "Allowed Values:" -ForegroundColor White
            foreach ($value in $field.allowedValues) {
                Write-Host "  - $value" -ForegroundColor Gray
            }
            Write-Host ""
        }
    }
    
    # Specifically check for Category and ValueStream
    Write-Host "`n=== CUSTOM FIELDS ===" -ForegroundColor Cyan
    $categoryField = $response.fields | Where-Object { $_.referenceName -like "*Category*" }
    $valueStreamField = $response.fields | Where-Object { $_.referenceName -like "*ValueStream*" -or $_.referenceName -like "*Value*Stream*" }
    
    if ($categoryField) {
        Write-Host "`nCategory Field:" -ForegroundColor Yellow
        Write-Host "Reference: $($categoryField.referenceName)"
        Write-Host "Name: $($categoryField.name)"
        Write-Host "Type: $($categoryField.type)"
        Write-Host "Required: $($categoryField.alwaysRequired)"
        if ($categoryField.allowedValues) {
            Write-Host "Allowed Values:" -ForegroundColor Green
            foreach ($val in $categoryField.allowedValues) {
                Write-Host "  - '$val'" -ForegroundColor White
            }
        }
    }
    
    if ($valueStreamField) {
        Write-Host "`nValue Stream Field:" -ForegroundColor Yellow
        Write-Host "Reference: $($valueStreamField.referenceName)"
        Write-Host "Name: $($valueStreamField.name)"
        Write-Host "Type: $($valueStreamField.type)"
        Write-Host "Required: $($valueStreamField.alwaysRequired)"
        if ($valueStreamField.allowedValues) {
            Write-Host "Allowed Values:" -ForegroundColor Green
            foreach ($val in $valueStreamField.allowedValues) {
                Write-Host "  - '$val'" -ForegroundColor White
            }
        }
    }
    
} catch {
    Write-Host "Error querying ADO: $_" -ForegroundColor Red
}
