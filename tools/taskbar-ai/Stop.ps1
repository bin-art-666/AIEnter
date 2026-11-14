$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$windhawk = Join-Path $workspace '.tools\windhawk\windhawk.exe'
if (Test-Path -LiteralPath $windhawk) { Start-Process -FilePath $windhawk -ArgumentList '-exit -wait' -WindowStyle Hidden -Wait }
$program = Join-Path $workspace 'dist\AIEnter.exe'
if (Test-Path -LiteralPath $program) {
    $instances = @(Get-Process AIEnter -ErrorAction SilentlyContinue | Where-Object Path -eq $program)
    Start-Process -FilePath $program -ArgumentList '--quit' -WindowStyle Hidden -Wait
    foreach ($instance in $instances) {
        if (!$instance.WaitForExit(15000)) { throw 'AIEnter is still exiting; build cancelled.' }
    }
}
