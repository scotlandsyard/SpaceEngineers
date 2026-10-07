# Copies the mod's game files (and nothing else) to the game's local Mods folder, for testing and for publishing.
# Publish (and publish updates) from there in game. The game keeps the workshop id in modinfo.sbmi in that folder;
# this script never touches it. It holds the publisher's Steam ID, so it isn't kept in this repo.
#   powershell -ExecutionPolicy Bypass -File Stage-Mod.ps1
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$target = Join-Path $env:AppData 'SpaceEngineers\Mods\TieredGasBottles'

New-Item -ItemType Directory -Force $target | Out-Null
foreach ($folder in 'Data', 'Textures') {
    $path = Join-Path $target $folder
    if (Test-Path $path) { Remove-Item $path -Recurse -Force -Confirm:$false }
    Copy-Item (Join-Path $source $folder) $path -Recurse
}
Copy-Item (Join-Path $source 'thumb.jpg') $target -Force

if (-not (Test-Path (Join-Path $target 'modinfo.sbmi'))) {
    Write-Warning 'modinfo.sbmi is missing: publishing now would create a NEW workshop item. Restore it first to update the existing one (workshop id 3815194485).'
}
Write-Host "Staged to $target"
