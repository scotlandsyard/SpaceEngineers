<#
.SYNOPSIS
    Builds the plugins from source and puts the DLLs in the Build folder.

.DESCRIPTION
    Needs the .NET SDK (https://dotnet.microsoft.com/download) and Space Engineers installed.
    The game is found through Steam automatically; pass -Bin64 if that fails.

.EXAMPLE
    .\Build.ps1
    Builds every plugin.

.EXAMPLE
    .\Build.ps1 "BaR Maid", Wilson
    Builds only those plugins (names as in the Projects folder).

.EXAMPLE
    .\Build.ps1 -Bin64 "D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64"
#>
param(
    [string[]]$Plugin,
    [string]$Bin64
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Bin64 {
    $candidates = @()
    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) {
            $candidates += $steam
            $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
            if (Test-Path $vdf) {
                foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                    $candidates += $m.Groups[1].Value -replace '\\\\', '\'
                }
            }
        }
    } catch { }
    $candidates += 'C:\Program Files (x86)\Steam'
    foreach ($lib in $candidates) {
        $dir = Join-Path $lib 'steamapps\common\SpaceEngineers\Bin64'
        if (Test-Path (Join-Path $dir 'Sandbox.Game.dll')) { return $dir }
    }
    return $null
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not installed. Get it from https://dotnet.microsoft.com/download and run this again.'
}
if (-not $Bin64) { $Bin64 = Find-Bin64 }
if (-not $Bin64 -or -not (Test-Path (Join-Path $Bin64 'Sandbox.Game.dll'))) {
    throw 'Space Engineers was not found. Pass its Bin64 folder: .\Build.ps1 -Bin64 "<Steam library>\steamapps\common\SpaceEngineers\Bin64"'
}
Write-Host "Game: $Bin64"

$projects = Get-ChildItem (Join-Path $root 'Projects') -Directory | Sort-Object Name
if ($Plugin) {
    # "powershell -File" passes "A,B" as one string.
    $Plugin = @($Plugin | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $unknown = $Plugin | Where-Object { $projects.Name -notcontains $_ }
    if ($unknown) { throw "Unknown plugin: $($unknown -join ', '). Choose from: $($projects.Name -join ', ')" }
    $projects = $projects | Where-Object { $Plugin -contains $_.Name }
}

$out = Join-Path $root 'Build'
New-Item -ItemType Directory -Force $out | Out-Null
$results = @()
foreach ($p in $projects) {
    $csproj = Get-ChildItem (Join-Path $p.FullName 'Source') -Filter *.csproj | Select-Object -First 1
    if (-not $csproj) { continue }
    Write-Host "`nBuilding $($p.Name)..."
    & dotnet build $csproj.FullName -c Release -nologo -v quiet "-p:Bin64=$Bin64"
    if ($LASTEXITCODE -ne 0) { throw "$($p.Name) failed to build." }
    $name = ([xml](Get-Content $csproj.FullName)).Project.PropertyGroup.AssemblyName | Where-Object { $_ } | Select-Object -First 1
    $dll = Join-Path $csproj.DirectoryName "bin\Release\net481\$name.dll"
    Copy-Item $dll $out -Force
    $results += [pscustomobject]@{ Plugin = $p.Name; Dll = "$name.dll"; SHA256 = (Get-FileHash $dll -Algorithm SHA256).Hash }
}

Write-Host "`nDone. The DLLs are in $out"
$results | Format-Table -AutoSize
Write-Host 'Copy the ones you want into %AppData%\Pulsar\Legacy\Local, start the game with Pulsar and tick them in the plugin list.'
