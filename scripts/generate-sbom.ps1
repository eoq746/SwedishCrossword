#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates a Software Bill of Materials (SBOM) in SPDX 2.2 format.
.DESCRIPTION
    Uses Microsoft SBOM Tool to scan the published application output and produce
    a machine-readable SBOM compliant with the EU Cyber Resilience Act (CRA).
    Optionally converts to human-readable formats using sbom2doc (if installed).
.PARAMETER Version
    The version string for the product (e.g. "1.0.0").
.PARAMETER OutputPath
    Directory where the SBOM will be generated. Defaults to ./sbom-output.
.EXAMPLE
    ./scripts/generate-sbom.ps1 -Version "1.2.3"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.0.0",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = (Join-Path $PSScriptRoot ".." "sbom-output")
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "SwedishCrossword.Api"
$publishPath = Join-Path $repoRoot "publish"

# --- 1. Publish the application ---
Write-Host "Publishing application..." -ForegroundColor Cyan
dotnet publish $projectPath -c Release -o $publishPath --nologo

# --- 2. Locate or download sbom-tool ---
$sbomTool = Get-Command sbom-tool -ErrorAction SilentlyContinue
if (-not $sbomTool) {
    $toolDir = Join-Path $repoRoot ".tools"
    $sbomToolExe = Join-Path $toolDir "sbom-tool$(if ($IsWindows -or $env:OS -match 'Windows') { '.exe' } else { '' })"

    if (-not (Test-Path $sbomToolExe)) {
        Write-Host "Downloading Microsoft SBOM Tool..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $toolDir -Force | Out-Null

        $rid = if ($IsWindows -or $env:OS -match 'Windows') { "win-x64" }
               elseif ($IsMacOS) { "osx-x64" }
               else { "linux-x64" }

        $downloadUrl = "https://github.com/microsoft/sbom-tool/releases/latest/download/sbom-tool-$rid"
        if ($rid -eq "win-x64") { $downloadUrl += ".exe" }

        Invoke-WebRequest -Uri $downloadUrl -OutFile $sbomToolExe
        if (-not ($IsWindows -or $env:OS -match 'Windows')) {
            chmod +x $sbomToolExe
        }
    }
    $sbomToolPath = $sbomToolExe
} else {
    $sbomToolPath = $sbomTool.Source
}

# --- 3. Generate SBOM ---
$manifestDir = Join-Path $OutputPath "_manifest"
if (Test-Path $manifestDir) {
    Remove-Item -Path $manifestDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

Write-Host "Generating SBOM (SPDX 2.2)..." -ForegroundColor Cyan
& $sbomToolPath generate `
    -b $publishPath `
    -bc $repoRoot `
    -pn "SwedishCrossword" `
    -pv $Version `
    -ps "SwedishCrossword" `
    -nsb "https://github.com/eoq746/SwedishCrossword" `
    -m $OutputPath `
    -mi "SPDX:2.2"

$manifestFile = Get-ChildItem -Path $OutputPath -Recurse -Filter "manifest.spdx.json" | Select-Object -First 1
if (-not $manifestFile) {
    Write-Error "SBOM generation failed - no manifest file found."
    return
}

Write-Host "SBOM generated: $($manifestFile.FullName)" -ForegroundColor Green

# --- 4. Sort packages for deterministic output ---
$data = Get-Content -Raw $manifestFile.FullName | ConvertFrom-Json
if ($null -ne $data.packages) {
    $data.packages = @($data.packages | Sort-Object name)
    $data | ConvertTo-Json -Depth 100 | Set-Content $manifestFile.FullName -Encoding UTF8
    Write-Host "Sorted SBOM packages alphabetically." -ForegroundColor Green
}

# --- 5. Generate human-readable formats (install sbom2doc if missing) ---
if (-not (Get-Command sbom2doc -ErrorAction SilentlyContinue)) {
    if (-not (Get-Command pip -ErrorAction SilentlyContinue)) {
        Write-Host "pip not found - skipping human-readable output. Install Python to enable this." -ForegroundColor Yellow
    } else {
        Write-Host "Installing sbom2doc..." -ForegroundColor Yellow
        pip install sbom2doc --quiet
    }
}

if (Get-Command sbom2doc -ErrorAction SilentlyContinue) {
    $mdFile = "$($manifestFile.FullName).md"
    $htmlFile = "$($manifestFile.FullName).html"

    & sbom2doc -i $manifestFile.FullName -o $mdFile -f markdown
    & sbom2doc -i $manifestFile.FullName -o $htmlFile -f html
    Write-Host "Generated markdown: $mdFile" -ForegroundColor Green
    Write-Host "Generated HTML: $htmlFile" -ForegroundColor Green
}

# --- Cleanup publish folder ---
Remove-Item -Path $publishPath -Recurse -Force

Write-Host "`nDone! SBOM files are in: $OutputPath" -ForegroundColor Cyan
