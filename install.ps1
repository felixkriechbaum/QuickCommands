# Builds QuickCommands and installs it for the current user (Windows):
#   exe   -> %LOCALAPPDATA%\Programs\QuickCommands\QuickCommands.exe
#   entry -> Start menu "QuickCommands"
# Run again to update. Your commands and settings are not touched.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish\win-x64'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\QuickCommands'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'QuickCommands.lnk'

dotnet publish (Join-Path $root 'src\QuickCommands.App') -c Release -r win-x64 -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Get-Process QuickCommands -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir*" } | Stop-Process -Force
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $publishDir 'QuickCommands.exe') $installDir -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = Join-Path $installDir 'QuickCommands.exe'
$link.WorkingDirectory = $installDir
$link.Description = 'Eigene Terminal-Befehle verwalten'
$link.Save()

Write-Host "Installed to $installDir"
Write-Host "Start menu entry: $shortcut"
