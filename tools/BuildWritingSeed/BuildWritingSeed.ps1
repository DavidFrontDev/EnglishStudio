# Builds the new portion of writing_tests.json from raw Telegram source folders.
# Input: Ielts {15,16,17,18,20}/Writing/{Test№1..4}/Task1.txt + 02.txt + Answer/Answer.txt
# Output: <repo>/src/EnglishStudio.Modules.Ielts.Writing/Seed/writing_tests.json
# - Keeps the 4 existing Cambridge IELTS 19 tests intact.
# - Appends acad-w-test05..acad-w-test24 (20 tests).
# - Parses Answer.txt into per-test model answers with band + answer text + examiner comment.

$ErrorActionPreference = 'Stop'

$repoRoot = 'C:\Users\tvore\source\repos\EnglishStudio'
$seedJson = Join-Path $repoRoot 'src\EnglishStudio.Modules.Ielts.Writing\Seed\writing_tests.json'
$tgRoot   = 'C:\Users\tvore\Downloads\Telegram Desktop'

# Maps Telegram folder + test number to:
#   - test code (acad-w-testNN)
#   - Title    (book + test)
#   - Attribution
#   - Task1 image filename (relative, embedded under <code>.<file>.png)
#   - Task1 chart type     (LineGraph/BarChart/PieChart/Table/ProcessDiagram/Map/MultipleCharts)
#   - Task1 topic category
#   - Task2 topic category
$M = @(
  @{N=5;  Folder='Ielts 15'; Book=15; T=1; Img='Coffee_and_tea_in_Australia.png';  Chart='BarChart';        Cat1='consumer-habits';   Cat2='society'},
  @{N=6;  Folder='Ielts 15'; Book=15; T=2; Img='Tourists_Caribbean_island.png';    Chart='LineGraph';       Cat1='tourism';           Cat2='media'},
  @{N=7;  Folder='Ielts 15'; Book=15; T=3; Img='Instant_noodles_process.png';      Chart='ProcessDiagram';  Cat1='manufacturing';     Cat2='media'},
  @{N=8;  Folder='Ielts 15'; Book=15; T=4; Img='Anthropology_graduates.png';       Chart='MultipleCharts';  Cat1='education-jobs';    Cat2='education-children'},
  @{N=9;  Folder='Ielts 16'; Book=16; T=1; Img='Electrical_appliances_housework.png'; Chart='MultipleCharts'; Cat1='domestic-life';   Cat2='society-history'},
  @{N=10; Folder='Ielts 16'; Book=16; T=2; Img='Sugar_from_sugarcane.png';         Chart='ProcessDiagram';  Cat1='manufacturing';     Cat2='advertising'},
  @{N=11; Folder='Ielts 16'; Book=16; T=3; Img='Southwest_Airport.png';            Chart='Map';             Cat1='urban-development'; Cat2='health'},
  @{N=12; Folder='Ielts 16'; Book=16; T=4; Img='Plastic_bottle_recycling.png';     Chart='ProcessDiagram';  Cat1='environment';       Cat2='technology'},
  @{N=13; Folder='Ielts 17'; Book=17; T=1; Img='Norbiton_industrial_area.png';     Chart='Map';             Cat1='urban-development'; Cat2='society'},
  @{N=14; Folder='Ielts 17'; Book=17; T=2; Img='Police_budget.png';                Chart='MultipleCharts';  Cat1='government-finance'; Cat2='education-children'},
  @{N=15; Folder='Ielts 17'; Book=17; T=3; Img='Weekly_spending_families.png';     Chart='BarChart';        Cat1='finance';           Cat2='employment-migration'},
  @{N=16; Folder='Ielts 17'; Book=17; T=4; Img='Shop_closures_openings.png';       Chart='LineGraph';       Cat1='economics';         Cat2='health'},
  @{N=17; Folder='Ielts 18'; Book=18; T=1; Img='Urban_population_Asian.png';       Chart='LineGraph';       Cat1='demographics';      Cat2='science'},
  @{N=18; Folder='Ielts 18'; Book=18; T=2; Img='US_households_income.png';         Chart='BarChart';        Cat1='finance';           Cat2='education'},
  @{N=19; Folder='Ielts 18'; Book=18; T=3; Img='Central_Library.png';              Chart='Map';             Cat1='urban-development'; Cat2='urbanisation'},
  @{N=20; Folder='Ielts 18'; Book=18; T=4; Img='Metal_price_changes.png';          Chart='LineGraph';       Cat1='economics';         Cat2='demographics'},
  @{N=21; Folder='Ielts20';  Book=20; T=1; Img='NYC_population.png';               Chart='Table';           Cat1='demographics';      Cat2='resources'},
  @{N=22; Folder='Ielts20';  Book=20; T=2; Img='Beechwood_Farm.png';               Chart='Map';             Cat1='rural-development'; Cat2='education-children'},
  @{N=23; Folder='Ielts20';  Book=20; T=3; Img='Little_Chalfont_Library.png';      Chart='MultipleCharts';  Cat1='society';           Cat2='environment'},
  @{N=24; Folder='Ielts20';  Book=20; T=4; Img='Bamboo_fabric.png';                Chart='ProcessDiagram';  Cat1='manufacturing';     Cat2='society'}
)

function Read-TxtRaw([string]$path) {
  if (-not (Test-Path -LiteralPath $path)) { return $null }
  return (Get-Content -LiteralPath $path -Encoding UTF8 -Raw)
}

# Strip the boilerplate header from Task1.txt / Task2.txt so the stored promptText
# omits the "You should spend about X minutes... Write at least Y words." chrome.
function Trim-Prompt([string]$raw) {
  if (-not $raw) { return '' }
  $t = $raw
  # Strip the IELTS instruction boilerplate (these can repeat in messy source files).
  $t = $t -replace "(?im)^\s*WRITING\s+TASK\s+\d+\s*$", ''
  $t = $t -replace "(?im)^\s*You should spend about\s+\d+\s+minutes on this task[^\r\n]*$", ''
  $t = $t -replace "(?im)^\s*Write at least\s+\d+\s+words\.?\s*$", ''
  $t = $t -replace "(?im)^\s*Write about the following topic:\s*$", ''
  # Strip duplicate "Give reasons for your answer..." lines if the candidate prompt has one
  # both before and after the topic statement (a quirk in the Ielts 20 Test 1 source file).
  $giveReasons = "(?im)^\s*Give reasons for your answer and include any relevant examples from your own knowledge or experience\.\s*$"
  $occurrences = [regex]::Matches($t, $giveReasons)
  if ($occurrences.Count -gt 1) {
    # Remove all but the last occurrence so the closing line stays.
    for ($i = $occurrences.Count - 2; $i -ge 0; $i--) {
      $o = $occurrences[$i]
      $t = $t.Remove($o.Index, $o.Length)
    }
  }
  # Collapse 3+ blank lines down to a single blank line.
  $t = [regex]::Replace($t, "(\r?\n){3,}", "`r`n`r`n")
  return $t.Trim()
}

# Parses an Answer.txt that uses the "TEST N, WRITING TASK M" / "Band X.Y score" / "Here is the examiner's comment:" structure.
# Returns hashtable[testNum][taskNum] -> @{ Band; Answer; Comment } for tests where pattern matches.
function Parse-AnswerTxt-NumberedTests([string]$raw) {
  $result = @{}
  if (-not $raw) { return $result }
  # Split on test/task headers. Capture order: number, task, then segment content.
  $pattern = '(?im)^\s*TEST\s+(\d+)\s*,\s*WRITING\s+TASK\s+(\d+)\s*$'
  $matches = [regex]::Matches($raw, $pattern)
  for ($i = 0; $i -lt $matches.Count; $i++) {
    $m = $matches[$i]
    $testN = [int]$m.Groups[1].Value
    $taskN = [int]$m.Groups[2].Value
    $start = $m.Index + $m.Length
    $end   = if ($i + 1 -lt $matches.Count) { $matches[$i + 1].Index } else { $raw.Length }
    $seg   = $raw.Substring($start, $end - $start)

    # Band X.Y from either "achieved a Band X.Y score" OR examiner-prepared note.
    $bandMatch = [regex]::Match($seg, '(?i)Band\s+(\d+(?:\.\d+)?)')
    $band = if ($bandMatch.Success) { [double]$bandMatch.Groups[1].Value } else { 0 }

    # Strip the introductory sentence (band declaration).
    $body = $seg
    $body = [regex]::Replace($body, '(?im)^\s*This is an answer written by a candidate who achieved a Band[^\r\n]*\r?\n', '')
    $body = [regex]::Replace($body, '(?im)^\s*This model has been prepared by an examiner[^\r\n]*\r?\n', '')

    # Split on "Here is the examiner's comment:" / "Here are comments from another examiner:"
    $commentMarker = '(?im)^\s*(?:Here is the examiner''s comment:|Here are comments from another examiner:)\s*$'
    $split = [regex]::Split($body, $commentMarker, 2)
    $answerText = $split[0].Trim()
    $commentText = if ($split.Count -gt 1) { $split[1].Trim() } else { $null }

    if (-not $result.ContainsKey($testN)) { $result[$testN] = @{} }
    $result[$testN][$taskN] = @{ Band = $band; Answer = $answerText; Comment = $commentText }
  }
  return $result
}

# Ielts 20 Answer.txt does NOT use "TEST N, WRITING TASK M" headers.
# It lists 8 essays in fixed Task 1/Task 2 alternation per test.
# Headers seen: "Essay [Candidate Essay]" and "Great composition [Candidate composition]".
function Parse-AnswerTxt-Ielts20([string]$raw) {
  $result = @{}
  if (-not $raw) { return $result }

  $pattern = '(?im)^\s*(?:Essay\s*\[Candidate\s+Essay\]|Great composition\s*\[Candidate\s+composition\])\s*$'
  $matches = [regex]::Matches($raw, $pattern)
  for ($i = 0; $i -lt $matches.Count; $i++) {
    $m = $matches[$i]
    $start = $m.Index + $m.Length
    $end   = if ($i + 1 -lt $matches.Count) { $matches[$i + 1].Index } else { $raw.Length }
    $seg   = $raw.Substring($start, $end - $start)

    $bandMatch = [regex]::Match($seg, '(?i)Band\s+(\d+(?:\.\d+)?)')
    $band = if ($bandMatch.Success) { [double]$bandMatch.Groups[1].Value } else { 0 }

    $body = $seg
    $body = [regex]::Replace($body, '(?im)^\s*This is an answer written by a candidate who achieved a Band[^\r\n]*\r?\n', '')

    # Comment marker for Ielts20 uses "[ Examiner's comments]" and then "Here is the examiner's comment:".
    # The "[ Examiner's comments]" line just marks the START of the comment block.
    $commentMarker = '(?im)^\s*(?:\[?\s*Examiner''?s?\s*comments?\s*\]?|Here is the examiner''s comment:)\s*$'
    $split = [regex]::Split($body, $commentMarker, 2)
    $answerText = $split[0].Trim()
    $commentText = if ($split.Count -gt 1) { $split[1].Trim() } else { $null }

    # Map essay index to (test, task)
    $testN = [int][math]::Floor($i / 2) + 1
    $taskN = ($i % 2) + 1

    if (-not $result.ContainsKey($testN)) { $result[$testN] = @{} }
    $result[$testN][$taskN] = @{ Band = $band; Answer = $answerText; Comment = $commentText }
  }
  return $result
}

# Load and parse Answer.txt for each book.
$answers = @{}
foreach ($folder in @('Ielts 15','Ielts 16','Ielts 17','Ielts 18','Ielts20')) {
  $ap = Join-Path $tgRoot "$folder\Writing\Answer\Answer.txt"
  $raw = Read-TxtRaw $ap
  if ($folder -eq 'Ielts20') {
    $answers[$folder] = Parse-AnswerTxt-Ielts20 $raw
  } else {
    $answers[$folder] = Parse-AnswerTxt-NumberedTests $raw
  }
  $count = if ($answers[$folder]) { $answers[$folder].Count } else { 0 }
  Write-Output ("Parsed {0}: {1} tests" -f $folder, $count)
}

# Construct an OrderedDictionary-like list of new test set DTOs.
$newSets = New-Object System.Collections.Generic.List[object]
foreach ($m in $M) {
  $codeNum = "{0:D2}" -f $m.N
  $code    = "acad-w-test$codeNum"
  $folder  = $m.Folder
  $tn      = $m.T

  $numero = [char]0x2116
  $task1Txt = Read-TxtRaw (Join-Path $tgRoot "$folder\Writing\Test$numero$tn\Task1.txt")
  $task2Txt = Read-TxtRaw (Join-Path $tgRoot "$folder\Writing\Test$numero$tn\02.txt")
  $task1Prompt = Trim-Prompt $task1Txt
  $task2Prompt = Trim-Prompt $task2Txt

  $t1Models = New-Object System.Collections.Generic.List[object]
  $t2Models = New-Object System.Collections.Generic.List[object]
  $bookAns = $answers[$folder]
  if ($bookAns -and $bookAns.ContainsKey($tn)) {
    $perTest = $bookAns[$tn]
    if ($perTest.ContainsKey(1)) {
      $a = $perTest[1]
      $t1Models.Add([ordered]@{ bandLevel = [int][math]::Round($a.Band); answerText = $a.Answer; examinerComment = $a.Comment })
    }
    if ($perTest.ContainsKey(2)) {
      $a = $perTest[2]
      $t2Models.Add([ordered]@{ bandLevel = [int][math]::Round($a.Band); answerText = $a.Answer; examinerComment = $a.Comment })
    }
  }

  $set = [ordered]@{
    code = $code
    title = "IELTS Test Book $($m.Book), Test $tn"
    attribution = "IELTS Test Book $($m.Book) Academic - Test $tn"
    task1 = [ordered]@{
      code = "$code-t1"
      kind = 'Task1Academic'
      promptText = $task1Prompt
      imageFile  = $m.Img
      chartType  = $m.Chart
      topicCategory = $m.Cat1
      minWords = 150
      recommendedMinutes = 20
      modelAnswers = $t1Models
    }
    task2 = [ordered]@{
      code = "$code-t2"
      kind = 'Task2'
      promptText = $task2Prompt
      topicCategory = $m.Cat2
      minWords = 250
      recommendedMinutes = 40
      modelAnswers = $t2Models
    }
  }
  $newSets.Add($set)
}

# Load existing seed JSON, keep entries that aren't being rebuilt (by code), then append new ones.
$raw = Get-Content -LiteralPath $seedJson -Encoding UTF8 -Raw
$existing = $raw | ConvertFrom-Json
$rebuildCodes = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($n in $newSets) { [void]$rebuildCodes.Add($n.code) }

$existingList = New-Object System.Collections.Generic.List[object]
foreach ($e in $existing) {
  if (-not $rebuildCodes.Contains($e.code)) { $existingList.Add($e) }
}
foreach ($n in $newSets) { $existingList.Add([pscustomobject]$n) }

# Emit pretty JSON with UTF-8 (no BOM) and LF endings to match other repo files.
$json = ($existingList | ConvertTo-Json -Depth 32)
# .NET ConvertTo-Json escapes non-ASCII with \uXXXX. Decode for readability.
$json = [System.Text.RegularExpressions.Regex]::Replace($json, '\\u([0-9a-fA-F]{4})', { param($mt) [char][int]::Parse($mt.Groups[1].Value, 'HexNumber') })

[System.IO.File]::WriteAllText($seedJson, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("Wrote {0} test sets to {1}" -f $existingList.Count, $seedJson)
