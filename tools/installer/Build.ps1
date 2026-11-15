param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$compiler = Join-Path $root '.tools\inno\ISCC.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Install Inno Setup 6.7.4 into .tools\inno first.' }
$modRoot = Join-Path $root '.tools\windhawk\AppData\Engine\Mods'
$config = Get-Content (Join-Path $modRoot 'aienter-taskbar-ai.ini')
$library = ($config | Where-Object { $_ -like 'LibraryFileName=*' } | Select-Object -First 1).Split('=',2)[1]
$payload = Join-Path $root '.build\installer-payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $modRoot ('64\' + $library)) -Destination (Join-Path $payload 'aienter-taskbar-ai.dll') -Force
foreach ($file in @('libc++.whl','libunwind.whl','windhawk-mod-shim.dll')) {
    Copy-Item -LiteralPath (Join-Path $modRoot ('64\' + $file)) -Destination $payload -Force
}
& $compiler (Join-Path $PSScriptRoot 'AIEnter.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
