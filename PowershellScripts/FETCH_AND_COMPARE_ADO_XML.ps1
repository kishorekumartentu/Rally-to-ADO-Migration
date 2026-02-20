# PowerShell Script to Fetch and Compare ADO Test Steps XML
# This will help us see EXACTLY what ADO has stored vs. what a manual test case looks like

$pat = ""  # Replace with your PAT
$org = "emisgroup"
$project = "Acute Meds Management"

# Test case created by our tool (blank steps in UI)
$migratedTestCaseId = 516344

# You need to create this manually in ADO with 2-3 steps
$manualTestCaseId = 516328  # REPLACE THIS - create a test case manually and enter its ID

$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$($pat)"))
$headers = @{Authorization=("Basic {0}" -f $base64AuthInfo)}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FETCHING MIGRATED TEST CASE XML" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$url1 = "https://$org.visualstudio.com/$project/_apis/wit/workitems/$migratedTestCaseId`?api-version=7.1"
try {
    $response1 = Invoke-RestMethod -Uri $url1 -Method Get -Headers $headers
    $stepsXml1 = $response1.fields.'Microsoft.VSTS.TCM.Steps'
    
    Write-Host "Test Case ID: $migratedTestCaseId" -ForegroundColor Green
    Write-Host "XML Length: $($stepsXml1.Length) characters" -ForegroundColor Green
    Write-Host ""
    Write-Host "First 1000 chars:" -ForegroundColor Yellow
    Write-Host $stepsXml1.Substring(0, [Math]::Min(1000, $stepsXml1.Length))
    Write-Host ""
    
    # Save to file
    $stepsXml1 | Out-File "Migrated_TC_Steps.xml" -Encoding UTF8
    Write-Host "Saved to: Migrated_TC_Steps.xml" -ForegroundColor Green
    
    # Parse and analyze
    [xml]$xmlDoc1 = $stepsXml1
    $steps1 = $xmlDoc1.SelectNodes("//step")
    Write-Host ""
    Write-Host "Analysis:" -ForegroundColor Cyan
    Write-Host "  Total steps: $($steps1.Count)" -ForegroundColor Cyan
    
    if ($steps1.Count -gt 0) {
        $firstStep = $steps1[0]
        Write-Host "  First step structure:" -ForegroundColor Cyan
        Write-Host "    Step ID: $($firstStep.id)" -ForegroundColor Gray
        Write-Host "    Step Type: $($firstStep.type)" -ForegroundColor Gray
        
        $paramStrings = $firstStep.SelectNodes(".//parameterizedString")
        Write-Host "    Number of parameterizedString elements: $($paramStrings.Count)" -ForegroundColor Gray
        
        if ($paramStrings.Count -gt 0) {
            $firstParam = $paramStrings[0]
            Write-Host "    First parameterizedString:" -ForegroundColor Gray
            Write-Host "      isformatted attribute: $($firstParam.isformatted)" -ForegroundColor Gray
            Write-Host "      Content: $($firstParam.InnerXml.Substring(0, [Math]::Min(100, $firstParam.InnerXml.Length)))" -ForegroundColor Gray
            
            # Check DIV/P structure
            $div = $firstParam.SelectSingleNode(".//DIV")
            if ($div) {
                Write-Host "      DIV found" -ForegroundColor Green
                $p = $div.SelectSingleNode(".//P")
                if ($p) {
                    Write-Host "      P found inside DIV" -ForegroundColor Green
                    Write-Host "      P text content: '$($p.InnerText)'" -ForegroundColor Green
                    Write-Host "      P inner XML: '$($p.InnerXml)'" -ForegroundColor Green
                } else {
                    Write-Host "      NO P tag found inside DIV!" -ForegroundColor Red
                    Write-Host "      DIV content: '$($div.InnerText)'" -ForegroundColor Red
                }
            } else {
                Write-Host "      NO DIV found!" -ForegroundColor Red
            }
        }
    }
    
} catch {
    Write-Host "ERROR fetching migrated test case: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host ""

if ($manualTestCaseId -eq 0) {
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host "MANUAL TEST CASE NOT SPECIFIED" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please:" -ForegroundColor Yellow
    Write-Host "1. Create a test case MANUALLY in ADO" -ForegroundColor Yellow
    Write-Host "2. Add 2-3 steps using the Steps tab" -ForegroundColor Yellow
    Write-Host "3. Save it" -ForegroundColor Yellow
    Write-Host "4. Update `$manualTestCaseId in this script with its ID" -ForegroundColor Yellow
    Write-Host "5. Run this script again" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "This will show us the EXACT format ADO expects!" -ForegroundColor Yellow
    exit
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FETCHING MANUAL TEST CASE XML" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$url2 = "https://$org.visualstudio.com/$project/_apis/wit/workitems/$manualTestCaseId`?api-version=7.1"
try {
    $response2 = Invoke-RestMethod -Uri $url2 -Method Get -Headers $headers
    $stepsXml2 = $response2.fields.'Microsoft.VSTS.TCM.Steps'
    
    Write-Host "Test Case ID: $manualTestCaseId" -ForegroundColor Green
    Write-Host "XML Length: $($stepsXml2.Length) characters" -ForegroundColor Green
    Write-Host ""
    Write-Host "First 1000 chars:" -ForegroundColor Yellow
    Write-Host $stepsXml2.Substring(0, [Math]::Min(1000, $stepsXml2.Length))
    Write-Host ""
    
    # Save to file
    $stepsXml2 | Out-File "Manual_TC_Steps.xml" -Encoding UTF8
    Write-Host "Saved to: Manual_TC_Steps.xml" -ForegroundColor Green
    
    # Parse and analyze
    [xml]$xmlDoc2 = $stepsXml2
    $steps2 = $xmlDoc2.SelectNodes("//step")
    Write-Host ""
    Write-Host "Analysis:" -ForegroundColor Cyan
    Write-Host "  Total steps: $($steps2.Count)" -ForegroundColor Cyan
    
    if ($steps2.Count -gt 0) {
        $firstStep = $steps2[0]
        Write-Host "  First step structure:" -ForegroundColor Cyan
        Write-Host "    Step ID: $($firstStep.id)" -ForegroundColor Gray
        Write-Host "    Step Type: $($firstStep.type)" -ForegroundColor Gray
        
        $paramStrings = $firstStep.SelectNodes(".//parameterizedString")
        Write-Host "    Number of parameterizedString elements: $($paramStrings.Count)" -ForegroundColor Gray
        
        if ($paramStrings.Count -gt 0) {
            $firstParam = $paramStrings[0]
            Write-Host "    First parameterizedString:" -ForegroundColor Gray
            Write-Host "      isformatted attribute: $($firstParam.isformatted)" -ForegroundColor Gray
            Write-Host "      Content: $($firstParam.InnerXml.Substring(0, [Math]::Min(100, $firstParam.InnerXml.Length)))" -ForegroundColor Gray
            
            # Check DIV/P structure
            $div = $firstParam.SelectSingleNode(".//DIV")
            if ($div) {
                Write-Host "      DIV found" -ForegroundColor Green
                $p = $div.SelectSingleNode(".//P")
                if ($p) {
                    Write-Host "      P found inside DIV" -ForegroundColor Green
                    Write-Host "      P text content: '$($p.InnerText)'" -ForegroundColor Green
                    Write-Host "      P inner XML: '$($p.InnerXml)'" -ForegroundColor Green
                } else {
                    Write-Host "      NO P tag found inside DIV!" -ForegroundColor Red
                    Write-Host "      DIV content: '$($div.InnerText)'" -ForegroundColor Red
                }
            } else {
                Write-Host "      NO DIV found!" -ForegroundColor Red
            }
        }
    }
    
} catch {
    Write-Host "ERROR fetching manual test case: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "COMPARISON" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "Compare the two XML files:" -ForegroundColor Magenta
Write-Host "  Migrated_TC_Steps.xml" -ForegroundColor Yellow
Write-Host "  Manual_TC_Steps.xml" -ForegroundColor Yellow
Write-Host ""
Write-Host "Look for differences in:" -ForegroundColor Magenta
Write-Host "  - Tag casing (DIV vs div)" -ForegroundColor Gray
Write-Host "  - Attribute values" -ForegroundColor Gray
Write-Host "  - Text encoding" -ForegroundColor Gray
Write-Host "  - Whitespace" -ForegroundColor Gray
Write-Host "  - Any extra attributes" -ForegroundColor Gray
Write-Host ""
Write-Host "The manual test case XML is the GOLD STANDARD" -ForegroundColor Green
Write-Host "Our generated XML must match it EXACTLY!" -ForegroundColor Green
