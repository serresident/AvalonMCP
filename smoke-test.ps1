$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$responses = Get-Content -Raw (Join-Path $PSScriptRoot 'smoke-input.jsonl') | dotnet (Join-Path $PSScriptRoot 'bin\Debug\net8.0\AvalonMCP.dll')
if ($LASTEXITCODE -ne 0) { throw 'Server failed' }
$parsed = @($responses | ForEach-Object { $_ | ConvertFrom-Json })
if ($parsed.Count -ne 5) { throw 'Missing responses' }
if ($parsed[0].id -is [string]) { throw 'Numeric id converted to string' }
foreach ($reply in $parsed) { if ($reply.error -or $reply.result.isError) { throw ($reply | ConvertTo-Json -Depth 10) } }
$bytes = [Convert]::FromBase64String($parsed[4].result.content[0].data)
if ([BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Invalid PNG signature' }
$stream = [IO.MemoryStream]::new($bytes, $false)
$bitmap = [Drawing.Bitmap]::new($stream)
try {
    if ($bitmap.Width -ne 320 -or $bitmap.Height -ne 200) { throw 'Wrong PNG dimensions' }
    $pixel = $bitmap.GetPixel(160, 100)
    if ($pixel.R -lt 250 -or $pixel.G -gt 5 -or $pixel.B -gt 5) { throw 'Expected red control at viewport center' }
} finally { $bitmap.Dispose(); $stream.Dispose() }
Write-Output 'PASS: 5 responses, numeric id, tree, PNG signature, dimensions and red center pixel.'
