param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [Parameter(Mandatory = $true)][string]$TyrantExe
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $TyrantExe)) { throw 'Choose Tyrant?s sidecar tyrant.exe.' }
& $TyrantExe unity prepare --project $PSScriptRoot --game $GameRoot
if ($LASTEXITCODE -ne 0) { throw 'Tyrant could not prepare the Unity references.' }
