# Rebuild all brand exports from docs/assets/mailhelper-mark.svg.
# Usage: pwsh -File tools/make-icon.ps1 [-NodePath node] [-SharpPath <existing sharp package>]
# See make-icon.mjs for a local sharp installation command. Normal builds need no Node.js.
param([string]$NodePath = 'node', [string]$SharpPath = '')
$ErrorActionPreference = 'Stop'
$previousSharpPath = $env:MAILHELPER_ICON_SHARP_PATH
try {
    if ($SharpPath) { $env:MAILHELPER_ICON_SHARP_PATH = $SharpPath }
    & $NodePath (Join-Path $PSScriptRoot 'make-icon.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
} finally {
    $env:MAILHELPER_ICON_SHARP_PATH = $previousSharpPath
}
