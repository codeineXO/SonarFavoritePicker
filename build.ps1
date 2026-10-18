param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\generate-icon.ps1"
& $Dotnet publish "$PSScriptRoot\SonarFavoritePicker.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Ready: $PSScriptRoot\dist\SonarFavoritePicker.exe"
