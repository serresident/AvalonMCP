$ErrorActionPreference = 'Stop'

function Invoke-McpTool($toolName, $argsObj) {
    $req = @{
        jsonrpc = "2.0"
        id = 1
        method = "tools/call"
        params = @{
            name = $toolName
            arguments = $argsObj
        }
    } | ConvertTo-Json -Compress -Depth 10

    $resp = $req | dotnet (Join-Path $PSScriptRoot 'bin\Debug\net8.0\AvalonMCP.dll')
    # Write-Host "RAW: $resp"
    $parsed = $resp | ConvertFrom-Json
    if ($parsed.error) { throw "JSON-RPC Error: $($parsed.error.message)" }
    return $parsed
}

Write-Host "1. Testing clean layout with lint_ui..."
$cleanXaml = '<Border xmlns="https://github.com/avaloniaui" Width="200" Height="100" Background="Blue"><TextBlock Text="OK" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>'
$cleanResp = Invoke-McpTool "lint_ui" @{ xaml = $cleanXaml; width = 400; height = 300 }
$cleanReport = $cleanResp.result.content[0].text | ConvertFrom-Json
if (-not $cleanReport.Passed -or $cleanReport.ErrorCount -ne 0) {
    throw "Expected clean layout to pass, but got: $($cleanResp | ConvertTo-Json -Depth 5)"
}
Write-Host "  -> Clean layout PASSED."

Write-Host "2. Testing ZERO_BOUNDS detection..."
$zeroXaml = '<StackPanel xmlns="https://github.com/avaloniaui"><Button Name="CollapsedBtn" Content="Save" Width="0" Height="0"/></StackPanel>'
$zeroResp = Invoke-McpTool "lint_ui" @{ xaml = $zeroXaml; width = 400; height = 300 }
Write-Host "ZERO TEXT: $($zeroResp.result.content[0].text)"
$zeroReport = $zeroResp.result.content[0].text | ConvertFrom-Json
if ($zeroReport.Passed -or $zeroReport.ErrorCount -eq 0 -or $zeroReport.Diagnostics[0].Code -ne "ZERO_BOUNDS") {
    throw "Expected ZERO_BOUNDS error, but got: $($zeroResp | ConvertTo-Json -Depth 5)"
}
Write-Host "  -> ZERO_BOUNDS correctly detected: $($zeroReport.Diagnostics[0].Message)"

Write-Host "3. Testing TEXT_CLIPPED detection..."
$clipXaml = '<Border xmlns="https://github.com/avaloniaui" Width="40" Height="20"><TextBlock Name="ClippedText" Text="This text is way too long to fit in 40 pixels"/></Border>'
$clipResp = Invoke-McpTool "lint_ui" @{ xaml = $clipXaml; width = 400; height = 300 }
$clipReport = $clipResp.result.content[0].text | ConvertFrom-Json
$hasClip = $clipReport.Diagnostics | Where-Object { $_.Code -eq "TEXT_CLIPPED" }
if (-not $hasClip) {
    throw "Expected TEXT_CLIPPED warning, but got: $($clipResp | ConvertTo-Json -Depth 5)"
}
Write-Host "  -> TEXT_CLIPPED correctly detected: $($hasClip.Message)"

Write-Host "4. Testing UNINTENDED_OVERLAP in Grid..."
$overlapXaml = '<Grid xmlns="https://github.com/avaloniaui" Width="200" Height="100"><Button Name="Btn1" Content="First"/><Button Name="Btn2" Content="Second"/></Grid>'
$overlapResp = Invoke-McpTool "lint_ui" @{ xaml = $overlapXaml; width = 400; height = 300 }
$overlapReport = $overlapResp.result.content[0].text | ConvertFrom-Json
$hasOverlap = $overlapReport.Diagnostics | Where-Object { $_.Code -eq "UNINTENDED_OVERLAP" }
if (-not $hasOverlap) {
    throw "Expected UNINTENDED_OVERLAP error, but got: $($overlapResp | ConvertTo-Json -Depth 5)"
}
Write-Host "  -> UNINTENDED_OVERLAP correctly detected: $($hasOverlap.Message)"

Write-Host "5. Testing inspect_ui with Debug Overlay..."
$inspectResp = Invoke-McpTool "inspect_ui" @{ xaml = $overlapXaml; width = 400; height = 300; annotateErrors = $true }
if ($inspectResp.result.content.Count -ne 2) {
    throw "Expected 2 content items (text + image), got $($inspectResp.result.content.Count)"
}
$diagText = $inspectResp.result.content[0].text | ConvertFrom-Json
if (-not $diagText.diagnostics) {
    throw "Expected diagnostics inside inspect_ui text payload"
}
$pngBytes = [Convert]::FromBase64String($inspectResp.result.content[1].data)
if ([BitConverter]::ToString($pngBytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') {
    throw "Invalid PNG signature in inspect_ui"
}
Write-Host "  -> inspect_ui generated valid annotated PNG ($($pngBytes.Length) bytes) and diagnostics."

Write-Host "`nALL LINTER AND OVERLAY TESTS PASSED SUCCESSFULLY! 🎯"
