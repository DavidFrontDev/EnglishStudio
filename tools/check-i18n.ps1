<#
  Localization guard for EnglishStudio (Stage 6 of the RU/EN i18n work).

  Fails (exit 1) when:
    1. Strings.resx and Strings.en.resx have different key sets, or duplicate keys.
    2. A {loc:Tr KEY} / Loc.Tr("KEY") / Loc.Format("KEY") / localizer["KEY"] reference has no
       matching key in the resx (would render the raw key at runtime).
    3. A user-facing Cyrillic literal is left hardcoded in XAML (outside comments / d: design attrs).
    4. A user-facing Cyrillic string literal is left hardcoded in App C# (outside comments, log
       calls, exceptions and the bilingual `nameRu:` module data).

  Run locally or in CI:  pwsh tools/check-i18n.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$src  = Join-Path $root 'src'
$loc  = Join-Path $src 'EnglishStudio.App\Localization'
$ruPath = Join-Path $loc 'Strings.resx'
$enPath = Join-Path $loc 'Strings.en.resx'
$cyr = '[Ѐ-ӿ]'
$errors = 0
function Fail($msg) { Write-Host "  FAIL: $msg" -ForegroundColor Red; $script:errors++ }
function Ok($msg)   { Write-Host "  OK:   $msg" -ForegroundColor Green }

# ── 1. resx parity + duplicates ───────────────────────────────────────────────
function Get-Keys($path) { (Select-String -Path $path -Pattern '<data name="([^"]+)"').Matches | ForEach-Object { $_.Groups[1].Value } }
$ru = @(Get-Keys $ruPath); $en = @(Get-Keys $enPath)
$ruSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$ru)
$dupRu = ($ru | Group-Object | Where-Object Count -gt 1).Name
$dupEn = ($en | Group-Object | Where-Object Count -gt 1).Name
Write-Host "[1] resx parity ($($ru.Count) RU / $($en.Count) EN)"
if ($dupRu) { Fail "duplicate RU keys: $($dupRu -join ', ')" }
if ($dupEn) { Fail "duplicate EN keys: $($dupEn -join ', ')" }
$onlyRu = $ru | Where-Object { $en -notcontains $_ }
$onlyEn = $en | Where-Object { $ru -notcontains $_ }
if ($onlyRu) { Fail "keys only in RU: $($onlyRu -join ', ')" }
if ($onlyEn) { Fail "keys only in EN: $($onlyEn -join ', ')" }
if (-not ($dupRu -or $dupEn -or $onlyRu -or $onlyEn)) { Ok "key sets identical, no duplicates" }

# ── 2. referenced keys must exist (skip comment lines to avoid doc-example noise) ──
Write-Host "[2] referenced keys exist"
$refPatterns = @(
  '\{loc:Tr\s+(?:Key=)?([A-Za-z0-9_]+)',
  'Loc\.(?:Tr|Format)\(\s*"([A-Za-z0-9_]+)"',
  '(?:_localizer|localizer|Instance)\["([A-Za-z0-9_]+)"\]'
)
$missing = @{}
$files = Get-ChildItem -Path $src -Recurse -Include *.xaml,*.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
foreach ($f in $files) {
  $isCs = $f.Extension -eq '.cs'
  foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
    $t = $line.TrimStart()
    if ($isCs -and ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*'))) { continue }
    foreach ($p in $refPatterns) {
      foreach ($m in [regex]::Matches($line, $p)) {
        $k = $m.Groups[1].Value
        if (-not $ruSet.Contains($k) -and -not $missing.ContainsKey($k)) {
          $missing[$k] = $f.FullName.Substring($src.Length + 1) + ':' + $line.Trim()
        }
      }
    }
  }
}
if ($missing.Count -gt 0) { $missing.GetEnumerator() | ForEach-Object { Fail "missing key '$($_.Key)'  <- $($_.Value)" } }
else { Ok "every referenced key is defined" }

# ── 3. no hardcoded Cyrillic in XAML UI literals (strip comments, allow d: design attrs) ──
Write-Host "[3] no Cyrillic in XAML UI literals"
$xamlHits = 0
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include *.xaml | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
  $raw = [System.IO.File]::ReadAllText($f.FullName)
  $noComments = [regex]::Replace($raw, '(?s)<!--.*?-->', '')
  $n = 0
  foreach ($line in ($noComments -split "`n")) {
    $n++
    if ($line -match $cyr) {
      if ($line -match "d:[A-Za-z]+=`"[^`"]*$cyr") { continue }   # design-time attribute — ignore
      Fail "$($f.Name):$n  $($line.Trim())"; $xamlHits++
    }
  }
}
if ($xamlHits -eq 0) { Ok "no user-facing Cyrillic in XAML" }

# ── 4. no hardcoded Cyrillic in App C# string literals (outside comments/logs/exceptions) ──
Write-Host "[4] no Cyrillic in App C# string literals"
$csHits = 0
$app = Join-Path $src 'EnglishStudio.App'
foreach ($f in (Get-ChildItem -Path $app -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
  $n = 0
  foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
    $n++
    $t = $line.TrimStart()
    if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
    if ($line -match '(_log|_logger|logger)\.Log' -or $line -match '\bthrow new\b' -or $line -match 'nameRu:') { continue }
    # Inspect only COMPLETE string literals (so Cyrillic in a trailing // comment is ignored).
    $hit = $false
    foreach ($lm in [regex]::Matches($line, '"(?:[^"\\]|\\.)*"')) { if ($lm.Value -match $cyr) { $hit = $true; break } }
    if ($hit) { Fail "$($f.Name):$n  $($line.Trim())"; $csHits++ }
  }
}
if ($csHits -eq 0) { Ok "no user-facing Cyrillic in App C# literals" }

Write-Host ""
if ($errors -gt 0) { Write-Host "i18n check FAILED with $errors problem(s)." -ForegroundColor Red; exit 1 }
Write-Host "i18n check PASSED." -ForegroundColor Green; exit 0