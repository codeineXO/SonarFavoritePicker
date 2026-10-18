param([string]$Executable = "$PSScriptRoot\dist\SonarFavoritePicker.exe")
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$bytes = [System.IO.File]::ReadAllBytes($Executable)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ([BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68) -ne 2) {
    throw 'The executable must use the Windows GUI subsystem for the native runtime download prompt.'
}

# Select an isolated runtime location for this child process only. Do not
# uninstall, rename, or change any runtime/environment setting on the PC.
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SonarPickerRuntimeTest-' + [guid]::NewGuid())
$fxr = Get-ChildItem "$env:ProgramFiles\dotnet\host\fxr" -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$fxr) { throw 'An installed .NET hostfxr is required to run this isolated verification.' }
try {
    $hostFolder = Join-Path $testRoot "host\fxr\$($fxr.Name)"
    New-Item -ItemType Directory -Path $hostFolder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $fxr.FullName 'hostfxr.dll') -Destination $hostFolder
    foreach ($scenario in @('No runtimes', 'Desktop runtime missing')) {
        if ($scenario -eq 'Desktop runtime missing') {
            # A framework version must contain a deps manifest to be discovered.
            # No runtime is executed: Desktop resolution fails first.
            $coreFolder = Join-Path $testRoot 'shared\Microsoft.NETCore.App\8.0.99'
            New-Item -ItemType Directory -Path $coreFolder -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $coreFolder 'Microsoft.NETCore.App.deps.json') -Value '{}'
        }
        $info = [System.Diagnostics.ProcessStartInfo]::new($Executable)
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardError = $true
        $info.RedirectStandardOutput = $true
        $info.Environment['DOTNET_ROOT_X64'] = $testRoot
        $info.Environment['DOTNET_ROOT'] = $testRoot
        # Suppress the actual dialog only in this verification child, so the
        # test neither blocks on a user click nor opens a browser/download.
        $info.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
        $process = [System.Diagnostics.Process]::Start($info)
        try {
            $errorText = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -eq 0 -or $errorText -notmatch 'https://aka.ms/dotnet-core-applaunch\?[^\s]+') {
                throw "Missing-runtime detection failed: $errorText"
            }
            $url = $Matches[0]
            if ($url -notmatch 'framework=Microsoft.WindowsDesktop.App') {
                throw "Download link did not target the Desktop runtime: $errorText"
            }
            if ($url -notmatch 'arch=x64') { throw "Incorrect runtime architecture: $url" }
            Write-Host "PASS: $scenario — native host detects the dependency and supplies $url"
        }
        finally { $process.Dispose() }
    }
    Write-Host 'PASS: Windows GUI launcher supports the native download prompt. Test dialogs were suppressed.'
}
finally {
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $expectedParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($resolved) -notlike 'SonarPickerRuntimeTest-*') {
        throw 'Refusing to clean a path outside this verification temp folder.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
