param(
  [string]$CoreOutputDir = "frontend/src/content/lexicon-core",
  [string]$TailOutputDir = "frontend/public/lexicon-data",
  [int]$TotalCount = 20000,
  [int]$CoreCount = 3000,
  [int]$MinWordLength = 1,
  [int]$MaxWordLength = 12,
  [int]$ShardSize = 500,
  [string]$SeedWordsCsv = "ESS,ARA,EKA,ALN,ÖRA,Å,LO,NORR,OST",
  [switch]$Preview,
  [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$sourceFileRelativePaths = @(
  'SwedishCrossword/Data/custom-words.json',
  'SwedishCrossword/Data/dsso-words.json',
  'SwedishCrossword/Data/kelly-words.json',
  'SwedishCrossword/Data/lexin-words.json',
  'SwedishCrossword/Data/synonym-words.json'
)
$sourceFiles = $sourceFileRelativePaths | ForEach-Object { Join-Path $repoRoot $_ }
$resolvedCoreOutputDir = if ([System.IO.Path]::IsPathRooted($CoreOutputDir)) { $CoreOutputDir } else { Join-Path $repoRoot $CoreOutputDir }
$resolvedTailOutputDir = if ([System.IO.Path]::IsPathRooted($TailOutputDir)) { $TailOutputDir } else { Join-Path $repoRoot $TailOutputDir }
$resolvedShardDir = Join-Path $resolvedTailOutputDir 'shards'

$seedWords = @()
if ($SeedWordsCsv) {
  $seedWords = $SeedWordsCsv.Split(',') |
	ForEach-Object { $_.Trim().ToUpperInvariant() } |
	Where-Object { $_ }
}

$allRows = foreach ($file in $sourceFiles) {
  $source = [System.IO.Path]::GetFileNameWithoutExtension($file)
  Get-Content $file -Raw | ConvertFrom-Json | ForEach-Object {
	$altClues = @()
	if ($_.PSObject.Properties.Name -contains 'AlternativeClues' -and $_.AlternativeClues) {
	  $altClues = @($_.AlternativeClues)
	}

	[pscustomobject]@{
	  Word = ("$($_.Word)".Trim().ToUpperInvariant())
	  Clue = ("$($_.Clue)".Trim())
	  AlternativeClues = $altClues
	  Category = if ($_.PSObject.Properties.Name -contains 'Category') { "$($_.Category)" } else { '' }
	  Difficulty = if ($_.PSObject.Properties.Name -contains 'Difficulty') { "$($_.Difficulty)" } else { '' }
	  Source = $source
	}
  }
}

$allRows = $allRows | Where-Object { $_.Word -and $_.Clue }
$wordGroups = $allRows | Group-Object Word | Sort-Object -Property @{Expression='Count';Descending=$true}, @{Expression='Name';Descending=$false}
$availableWords = $wordGroups.Name
$missingSeedWords = $seedWords | Where-Object { $_ -notin $availableWords }

$wordPool = $wordGroups |
  Where-Object {
	$_.Name.Length -ge $MinWordLength -and
	$_.Name.Length -le $MaxWordLength
  } |
  ForEach-Object { $_.Name }

$selectedWords = New-Object System.Collections.Generic.List[string]
foreach ($word in $seedWords) {
  if ($word -in $availableWords -and -not $selectedWords.Contains($word)) {
	$selectedWords.Add($word)
  }
}
foreach ($word in $wordPool) {
  if ($selectedWords.Count -ge $TotalCount) { break }
  if (-not $selectedWords.Contains($word)) {
	$selectedWords.Add($word)
  }
}

function Get-SlugBase([string]$word) {
  return $word.ToLowerInvariant().Replace('å', 'a').Replace('ä', 'a').Replace('ö', 'o')
}

function Get-Slug([string]$word, [hashtable]$seen) {
  $base = "lexikon-$(Get-SlugBase $word)"
  if (-not $seen.ContainsKey($base)) {
	$seen[$base] = 1
	return $base
  }

  $seen[$base] += 1
  return "$base-$($seen[$base])"
}

function Build-LexiconEntry([string]$word, [string]$slug) {
  $entries = $allRows | Where-Object { $_.Word -eq $word }
  $clues = @(
	@($entries.Clue + $entries.AlternativeClues) |
	  Where-Object { $_ -and $_.Trim().Length -gt 0 } |
	  ForEach-Object { $_.Trim() } |
	  Select-Object -Unique
  )

  $sources = $entries.Source | Select-Object -Unique
  $difficulty = ($entries.Difficulty | Where-Object { $_ } | Group-Object | Sort-Object Count -Descending | Select-Object -First 1).Name
  if (-not $difficulty) { $difficulty = 'Medium' }

  $definition = if ($clues.Count -gt 0) { $clues[0] } else { 'Saknar definition i källfilerna.' }
  $alternativeMeanings = if ($clues.Count -gt 1) { $clues[1..($clues.Count - 1)] } else { @() }

  $relatedWords = $allRows |
	Where-Object { $_.Word -ne $word -and $_.Word.Length -eq $word.Length } |
	Group-Object Word |
	Sort-Object -Property Count -Descending |
	Select-Object -First 8 |
	ForEach-Object { $_.Name }

  if ($relatedWords.Count -lt 8) {
	$fallback = $allRows |
	  Where-Object { $_.Word -ne $word } |
	  Group-Object Word |
	  Sort-Object -Property @{Expression='Count';Descending=$true}, @{Expression='Name';Descending=$false} |
	  ForEach-Object { $_.Name } |
	  Where-Object { $_ -notin $relatedWords } |
	  Select-Object -First (8 - $relatedWords.Count)
	$relatedWords += $fallback
  }

  $today = (Get-Date).ToString('yyyy-MM-dd')
  $keywords = "korsord, svenska korsord, $word, korsordslexikon, korsordsledtrådar"
  $title = "$word – korsordslexikon"
  $description = "Betydelse, vanliga ledtrådar och exempel för korsordsordet $word."

  return [pscustomobject]@{
	word = $word
	slug = $slug
	title = $title
	description = $description
	keywords = $keywords
	category = 'Lexikon'
	author = 'SvensktKorsord.se'
	published = $today
	definition = $definition
	clues = $clues
	alternativeMeanings = $alternativeMeanings
	relatedWords = @($relatedWords | Select-Object -First 8)
	difficulty = $difficulty
	sources = $sources
	seoTitle = "$word i korsord – betydelse, ledtrådar och exempel | SvensktKorsord.se"
	metaDescription = "Läs vad $word betyder i korsord, se vanliga ledtrådar, alternativa betydelser och praktiska exempel med svar. Ett snabbt uppslagsverk för svenska korsordslösare."
  }
}

function Build-CoreMarkdown($entry) {
  $commonCluesList = if ($entry.clues.Count -gt 0) {
	($entry.clues | ForEach-Object { "- $_" }) -join "`r`n"
  } else {
	'- Inga etablerade ledtrådar hittades i källfilerna.'
  }

  $altMeaningsList = if ($entry.alternativeMeanings.Count -gt 0) {
	($entry.alternativeMeanings | Select-Object -First 8 | ForEach-Object { "- $_" }) -join "`r`n"
  } else {
	'- Ordet används främst i en etablerad korsordsbetydelse i källmaterialet.'
  }

  $examples = @()
  $exampleClues = if ($entry.clues.Count -ge 4) { $entry.clues | Select-Object -First 4 } else { $entry.clues }
  if ($exampleClues.Count -eq 0) {
	$exampleClues = @($entry.definition)
  }
  foreach ($c in $exampleClues) {
	$examples += "- Ledtråd: **$c**  "
	$examples += "  Svar: **$($entry.word)**"
  }
  $exampleBlock = $examples -join "`r`n"
  $relatedList = ($entry.relatedWords | ForEach-Object { "- $_" }) -join "`r`n"

  $faqBlock = @(
	"### Vad betyder $($entry.word) i korsord?",
	"$($entry.word) används oftast i betydelsen: $($entry.definition)",
	"",
	"### Vilka ledtrådar är vanligast för $($entry.word)?",
	"De vanligaste i våra källor är sådana som liknar listan i avsnittet ‘Common crossword clues’.",
	"",
	"### Är $($entry.word) ett svårt korsordsord?",
	"Källmaterialet klassar ordet främst som **$($entry.difficulty)**.",
	"",
	"### Varifrån kommer definitionerna för $($entry.word)?",
	"Definitioner och ledtrådar är sammanställda från: $($entry.sources -join ', ')."
  ) -join "`r`n"

  return @"
---
title: "$($entry.title)"
description: "$($entry.description)"
slug: "$($entry.slug)"
keywords: "$($entry.keywords)"
category: "$($entry.category)"
author: "$($entry.author)"
published: "$($entry.published)"
---

# $($entry.word)

## Definition
$($entry.definition)

## Common crossword clues
$commonCluesList

## Alternative meanings
$altMeaningsList

## Example clues and answers
$exampleBlock

## Related crossword words
$relatedList

## FAQ
$faqBlock

## SEO title
$($entry.seoTitle)

## Meta description
$($entry.metaDescription)
"@
}

$slugSeen = @{}
$entries = foreach ($word in $selectedWords) {
  $slug = Get-Slug -word $word -seen $slugSeen
  Build-LexiconEntry -word $word -slug $slug
}

$coreCountEffective = [Math]::Min($CoreCount, $entries.Count)
$coreEntries = @($entries | Select-Object -First $coreCountEffective)
$tailEntries = @($entries | Select-Object -Skip $coreCountEffective)

if ($Preview) {
  [pscustomobject]@{
	TotalCountRequested = $TotalCount
	SelectedCount = $entries.Count
	CoreCount = $coreEntries.Count
	TailCount = $tailEntries.Count
	MissingSeedWords = ($missingSeedWords -join ', ')
	CoreOutputDir = $resolvedCoreOutputDir
	TailOutputDir = $resolvedTailOutputDir
	FirstWords = ($selectedWords | Select-Object -First 25) -join ', '
  } | Format-List | Out-String | Write-Output
  exit 0
}

New-Item -ItemType Directory -Path $resolvedCoreOutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedShardDir -Force | Out-Null

if ($Clean) {
  Get-ChildItem (Join-Path $resolvedCoreOutputDir '*.md') -ErrorAction SilentlyContinue | Remove-Item -Force
  Get-ChildItem (Join-Path $resolvedShardDir '*.json') -ErrorAction SilentlyContinue | Remove-Item -Force
  Remove-Item (Join-Path $resolvedTailOutputDir 'index.json') -ErrorAction SilentlyContinue -Force
  Remove-Item (Join-Path $resolvedTailOutputDir 'manifest.json') -ErrorAction SilentlyContinue -Force
}

foreach ($entry in $coreEntries) {
  $path = Join-Path $resolvedCoreOutputDir "$($entry.slug).md"
  Set-Content -Path $path -Value (Build-CoreMarkdown -entry $entry) -Encoding UTF8
}

$indexRows = @()
foreach ($entry in $coreEntries) {
  $indexRows += [pscustomobject]@{
	word = $entry.word
	slug = $entry.slug
	title = $entry.title
	description = $entry.description
	isCore = $true
	shard = $null
  }
}

$tailBatches = @{}
if ($tailEntries.Count -gt 0) {
  for ($i = 0; $i -lt $tailEntries.Count; $i += $ShardSize) {
	$batch = @($tailEntries | Select-Object -Skip $i -First $ShardSize)
	$shardName = ('shard-{0:D4}' -f ([int]($i / $ShardSize) + 1))
	$tailBatches[$shardName] = $batch

	foreach ($entry in $batch) {
	  $indexRows += [pscustomobject]@{
		word = $entry.word
		slug = $entry.slug
		title = $entry.title
		description = $entry.description
		isCore = $false
		shard = $shardName
	  }
	}
  }
}

foreach ($key in $tailBatches.Keys) {
  $payload = [pscustomobject]@{
	shard = $key
	entries = @($tailBatches[$key] | ForEach-Object {
	  [pscustomobject]@{
		word = $_.word
		slug = $_.slug
		title = $_.title
		description = $_.description
		keywords = $_.keywords
		category = $_.category
		author = $_.author
		published = $_.published
		definition = $_.definition
		clues = $_.clues
		alternativeMeanings = $_.alternativeMeanings
		relatedWords = $_.relatedWords
		difficulty = $_.difficulty
		sources = $_.sources
		seoTitle = $_.seoTitle
		metaDescription = $_.metaDescription
	  }
	})
  }
  $shardPath = Join-Path $resolvedShardDir "$key.json"
  Set-Content -Path $shardPath -Value ($payload | ConvertTo-Json -Depth 8) -Encoding UTF8
}

$indexPath = Join-Path $resolvedTailOutputDir 'index.json'
Set-Content -Path $indexPath -Value ([pscustomobject]@{ entries = $indexRows } | ConvertTo-Json -Depth 6) -Encoding UTF8

$manifestPath = Join-Path $resolvedTailOutputDir 'manifest.json'
Set-Content -Path $manifestPath -Value ([pscustomobject]@{
  generatedAt = (Get-Date).ToString('o')
  totalEntries = $entries.Count
  coreEntries = $coreEntries.Count
  tailEntries = $tailEntries.Count
  shardCount = $tailBatches.Keys.Count
  shardSize = $ShardSize
  missingSeedWords = $missingSeedWords
} | ConvertTo-Json -Depth 4) -Encoding UTF8

"Generated $($entries.Count) total lexicon entries" | Write-Output
"Core markdown files: $($coreEntries.Count) in $resolvedCoreOutputDir" | Write-Output
"Tail shard entries: $($tailEntries.Count) in $resolvedShardDir" | Write-Output
if ($missingSeedWords.Count -gt 0) {
  "Missing seed words (not in sources): $($missingSeedWords -join ', ')" | Write-Output
}
