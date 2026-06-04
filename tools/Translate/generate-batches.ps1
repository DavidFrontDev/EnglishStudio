# Генерирует input-батчи для перевода из seed JSON.
# Запуск: powershell -ExecutionPolicy Bypass -File generate-batches.ps1 -Cefr A1 -ChunkSize 150

param(
    [Parameter(Mandatory=$true)][ValidateSet('A1','A2','B1','B2','C1','C2')][string]$Cefr,
    [int]$ChunkSize = 150
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$seedPath = Join-Path $root '..\..\src\EnglishStudio.Modules.Dictionary\Seed\oxford_5000.json' | Resolve-Path
$inputDir = Join-Path $root "input\$Cefr"

if (Test-Path $inputDir) { Remove-Item "$inputDir\*" -Force }
New-Item -ItemType Directory -Path $inputDir -Force | Out-Null

$seed = Get-Content $seedPath -Raw | ConvertFrom-Json
$words = @($seed.words | Where-Object { $_.cefr -eq $Cefr })
Write-Host "Loaded $($words.Count) $Cefr words from seed"

$batches = [Math]::Ceiling($words.Count / $ChunkSize)
Write-Host "Will produce $batches batches of up to $ChunkSize words each"

for ($i = 0; $i -lt $batches; $i++) {
    $batchNum = '{0:D3}' -f ($i + 1)
    $batchId = "${Cefr}_${batchNum}"
    $start = $i * $ChunkSize
    $end = [Math]::Min($start + $ChunkSize, $words.Count) - 1
    $slice = $words[$start..$end]

    $batchWords = $slice | ForEach-Object {
        [ordered]@{
            headword     = $_.headword
            pos          = $_.pos
            posFull      = $_.posFull
            definitionEn = $_.definitionEn
            exampleEn    = $_.exampleEn
        }
    }

    $batch = [ordered]@{
        batchId = $batchId
        cefr    = $Cefr
        count   = $batchWords.Count
        words   = @($batchWords)
    }

    $outPath = Join-Path $inputDir "batch_${batchId}.json"
    $batch | ConvertTo-Json -Depth 5 -Compress | Set-Content $outPath -Encoding utf8
    Write-Host "  → $outPath ($($batchWords.Count) words)"
}

Write-Host ""
Write-Host "Done. Input batches in: $inputDir"
