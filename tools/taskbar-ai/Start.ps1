$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runtime = Join-Path $workspace '.tools\windhawk'
$config = Join-Path $runtime 'AppData\Engine\Mods\aienter-taskbar-ai.ini'
if (!(Test-Path -LiteralPath $config)) { throw 'Run tools\taskbar-ai\Build.ps1 first.' }
$text = Get-Content -LiteralPath $config -Raw
$text = $text -replace '(?m)^Disabled=1\r?$', 'Disabled=0'
Set-Content -LiteralPath $config -Value $text -Encoding unicode
Start-Process -FilePath (Join-Path $runtime 'windhawk.exe') -ArgumentList '-tray-only' -WindowStyle Hidden
