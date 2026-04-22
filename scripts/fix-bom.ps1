<#
.SYNOPSIS
    Ensures all C# source files are encoded as UTF-8 with BOM (utf-8-bom),
    matching the `.editorconfig` `charset = utf-8-bom` rule enforced by
    `dotnet format style`.

.DESCRIPTION
    Scans the repository for *.cs files and rewrites any file that is
    missing the UTF-8 BOM (EF BB BF). Safe to run repeatedly; files that
    already have a BOM are left untouched.

.PARAMETER Path
    Root directory to scan. Defaults to the repository root (the parent of
    the directory containing this script).

.PARAMETER Include
    File globs to include. Defaults to *.cs.
#>
[CmdletBinding()]
param(
    [string]$Path = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string[]]$Include = @('*.cs')
)

$ErrorActionPreference = 'Stop'
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$fixed = 0

Get-ChildItem -Path $Path -Recurse -File -Include $Include |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' } |
    ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        if (-not $hasBom) {
            $text = [System.IO.File]::ReadAllText($_.FullName)
            [System.IO.File]::WriteAllText($_.FullName, $text, $utf8Bom)
            Write-Host "Added BOM: $($_.FullName)"
            $fixed++
        }
    }

Write-Host "Done. Files fixed: $fixed"
