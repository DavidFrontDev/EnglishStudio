# Конвертирует winterdl/oxford-5000 JSON в наш seed-формат.
# Запуск: pwsh -File tools\convert-oxford-seed.ps1

$srcPath = Join-Path $PSScriptRoot '..\.tmp_oxford_5000.json' | Resolve-Path
$dstPath = Join-Path $PSScriptRoot '..\src\EnglishStudio.Modules.Dictionary\Seed\oxford_5000.json'

$posMap = @{
    'noun'               = 'n'
    'verb'               = 'v'
    'adjective'          = 'adj'
    'adverb'             = 'adv'
    'pronoun'            = 'pron'
    'preposition'        = 'prep'
    'determiner'         = 'det'
    'number'             = 'num'
    'conjunction'        = 'conj'
    'exclamation'        = 'exclam'
    'modal verb'         = 'modal'
    'ordinal number'     = 'ordnum'
    'auxiliary verb'     = 'aux'
    'indefinite article' = 'art_indef'
    'definite article'   = 'art_def'
    'infinitive marker'  = 'inf'
    'linking verb'       = 'linkv'
}

$validCefr = @('A1', 'A2', 'B1', 'B2', 'C1', 'C2')

Write-Host "Reading: $srcPath"
$src = Get-Content $srcPath -Raw | ConvertFrom-Json

$words = New-Object System.Collections.Generic.List[object]
$skipped = 0
$props = $src | Get-Member -MemberType NoteProperty

foreach ($p in $props) {
    $e = $src.($p.Name)

    $word = $e.word
    $type = $e.type
    $cefr = if ($e.cefr) { $e.cefr.ToUpper() } else { '' }

    if ([string]::IsNullOrWhiteSpace($word) -or [string]::IsNullOrWhiteSpace($type)) {
        $skipped++
        continue
    }

    $pos = if ($posMap.ContainsKey($type)) { $posMap[$type] } else { 'other' }
    if (-not ($validCefr -contains $cefr)) { $cefr = '' }

    $entry = [ordered]@{
        headword     = $word
        pos          = $pos
        posFull      = $type
        cefr         = $cefr
        ipaUk        = $e.phon_br
        ipaUs        = $e.phon_n_am
        definitionEn = $e.definition
        exampleEn    = $e.example
        audioUk      = $e.uk
        audioUs      = $e.us
    }
    $words.Add($entry)
}

$out = [ordered]@{
    schemaVersion   = 1
    sourceName      = 'Oxford 3000/5000 (CEFR-tagged)'
    sourceRepo      = 'https://github.com/winterdl/oxford-5000-vocabulary-audio-definition'
    audioBaseUrl    = 'https://raw.githubusercontent.com/winterdl/oxford-5000-vocabulary-audio-definition/main/audio'
    generatedAt     = (Get-Date -Format 'yyyy-MM-dd')
    language        = 'en'
    totalEntries    = $words.Count
    skippedEntries  = $skipped
    words           = $words.ToArray()
}

Write-Host "Writing: $dstPath  ($($words.Count) entries, $skipped skipped)"
$out | ConvertTo-Json -Depth 6 -Compress | Set-Content $dstPath -Encoding utf8

$size = (Get-Item $dstPath).Length
Write-Host "Done. File size: $size bytes ($([Math]::Round($size / 1KB, 1)) KB)"
