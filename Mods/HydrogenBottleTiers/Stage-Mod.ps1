# Copies the mod's game files (and nothing else) to the game's local Mods folder, for testing and for publishing.
# Publish from there in game; afterwards run this again to copy the new modinfo.sbmi back into the repo.
#   powershell -ExecutionPolicy Bypass -File Stage-Mod.ps1
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$target = Join-Path $env:AppData 'SpaceEngineers\Mods\HydrogenBottleTiers'

# Bring back the workshop id the game wrote when the mod was published.
$publishedInfo = Join-Path $target 'modinfo.sbmi'
if ((Test-Path $publishedInfo) -and -not (Test-Path (Join-Path $source 'modinfo.sbmi'))) {
    Copy-Item $publishedInfo $source
    Write-Host 'Copied modinfo.sbmi back into the repo: commit it.'
}

New-Item -ItemType Directory -Force $target | Out-Null
foreach ($folder in 'Data', 'Textures') {
    $path = Join-Path $target $folder
    if (Test-Path $path) { Remove-Item $path -Recurse -Force -Confirm:$false }
    Copy-Item (Join-Path $source $folder) $path -Recurse
}
foreach ($file in 'thumb.jpg', 'modinfo.sbmi') {
    $path = Join-Path $source $file
    if (Test-Path $path) { Copy-Item $path $target -Force }
}
Write-Host "Staged to $target"
