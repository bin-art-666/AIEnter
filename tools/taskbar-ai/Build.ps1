param([switch]$Enable)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runtime = Join-Path $workspace '.tools\windhawk'
$compilerRoot = Join-Path $runtime 'Compiler'
$compiler = Join-Path $compilerRoot 'bin\clang++.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Deploy official Windhawk 1.7.3 portable with developer tools into .tools\windhawk first.' }
$program = Join-Path $workspace 'dist\AIEnter.exe'
if (!(Test-Path -LiteralPath $program)) { throw 'Build AIEnter with build.ps1 first.' }
$modRoot = Join-Path $runtime 'AppData\Engine\Mods'
$dllRoot = Join-Path $modRoot '64'
New-Item -ItemType Directory -Path $dllRoot -Force | Out-Null
foreach ($pair in @(@('libc++.dll', 'libc++.whl'), @('libunwind.dll', 'libunwind.whl'), @('windhawk-mod-shim.dll', 'windhawk-mod-shim.dll'))) {
    $destination = Join-Path $dllRoot $pair[1]
    if (!(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath (Join-Path $compilerRoot ('x86_64-w64-mingw32\bin\' + $pair[0])) -Destination $destination }
}
$library = 'aienter-taskbar-ai_' + (Get-Date -Format 'yyyyMMddHHmmss') + '.dll'
$arguments = @('-std=c++23', '-O2', '-shared', '-DUNICODE', '-D_UNICODE', '-DWINVER=0x0A00',
    '-D_WIN32_WINNT=0x0A00', '-DWH_MOD', '-DWH_MOD_ID=L"aienter-taskbar-ai"', '-DWH_MOD_VERSION=L"1.4.0"',
    '-include', 'windhawk_api.h', '-target', 'x86_64-w64-mingw32', '-Wl,--export-all-symbols',
    (Join-Path $runtime 'Engine\1.7.3\64\windhawk.lib'),
    (Join-Path $PSScriptRoot 'aienter-taskbar-ai.wh.cpp'), '-o', (Join-Path $dllRoot $library),
    '-lole32', '-loleaut32', '-lruntimeobject', '-lshell32', '-luuid')
Push-Location $compilerRoot
try { & $compiler @arguments; if ($LASTEXITCODE -ne 0) { throw 'Taskbar extension compilation failed.' } }
finally { Pop-Location }
# Update only our isolated portable runtime. No system service or startup registration.
$disabled = if ($Enable) { 0 } else { 1 }
$config = @"
[Mod]
LibraryFileName=$library
Disabled=$disabled
LoggingEnabled=1
DebugLoggingEnabled=0
Include=explorer.exe
Architecture=x86-64
Version=1.4.0
SettingsChangeTime=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
[Settings]
executable=$program
"@
Set-Content -LiteralPath (Join-Path $modRoot 'aienter-taskbar-ai.ini') -Value $config -Encoding unicode
Write-Output "Built: $library"
if ($Enable) { Start-Process -FilePath (Join-Path $runtime 'windhawk.exe') -ArgumentList '-tray-only' -WindowStyle Hidden }
