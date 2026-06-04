# Smoke test: confirm Claude CLI sees a chart image and reports its content correctly.
# Run from anywhere: pwsh tools\ClaudeImageSmoke.ps1

$ErrorActionPreference = 'Stop'

$img = Join-Path $env:APPDATA 'EnglishStudio\IeltsContent\Writing\acad-w-test05\Coffee_and_tea_in_Australia.png'

if (-not (Test-Path $img)) {
    Write-Host "Image not found: $img" -ForegroundColor Red
    Write-Host "Run the app at least once so WritingSeedService copies seed images into %AppData%."
    exit 1
}

$dir = Split-Path $img
$prompt = "@$img`nWhat are the five cities shown in this bar chart? List them comma-separated on one line. Nothing else."

Write-Host "Asking Claude to read $img ..." -ForegroundColor Cyan
$json = $prompt | claude -p --output-format json --add-dir $dir --allowedTools Read

$result = ($json | ConvertFrom-Json).result
Write-Host "Claude said: $result" -ForegroundColor Green
Write-Host ""
Write-Host "Expected to contain: Sydney, Melbourne, Brisbane, Adelaide, Hobart" -ForegroundColor Yellow

$expected = @('Sydney','Melbourne','Brisbane','Adelaide','Hobart')
$missing = $expected | Where-Object { $result -notmatch $_ }
if ($missing.Count -eq 0) {
    Write-Host "OK - all five cities present." -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAIL - missing: $($missing -join ', ')" -ForegroundColor Red
    exit 2
}
