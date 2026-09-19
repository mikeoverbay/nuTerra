# Snapshot, build, launch.
#
# "i want all the of this file copied before each run. i want instant revert."
#   - the owner, 2026-09-17.
#
# WHY A COPY AND NOT GIT. The repo is shared with other sessions and the stash
# stack is shared with every worktree, so a scratch commit or a stash is a
# thing that can collide with somebody else's work. A folder of files collides
# with nothing, needs no repo state, and can be read with Explorer when
# everything else has gone wrong.
#
# WHAT IT SAVES is every .vb in BrainTesting plus the shaders - the whole of
# what this app is. Not the bin, not obj: those rebuild.
#
# Usage:  .\run.ps1                 snapshot, build, launch
#         .\run.ps1 -NoLaunch       snapshot and build only
#         .\run.ps1 -Map 19_monastery
param(
    [string]$Map = "19_monastery",
    [switch]$NoLaunch,
    # Start with the node board driving instead of RangeBrain.
    [switch]$Graph,
    [int]$Keep = 30
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
# WHERE THE SNAPSHOTS LIVE, AND WHY NOT IN THE PROJECT.
#
# The first version put _snapshots inside BrainTesting\ and the build died
# instantly: SDK-style projects glob **/*.vb, so thirty-three copied files
# became SOURCE. Every type was defined twice - "Action is ambiguous",
# "NullBrain must implement IBrain.Name", a page of errors none of which
# mentioned copying anything.
#
# A backup that breaks the build by existing is worse than no backup. They go
# in a sibling folder, outside anything the project globs.
$snapRoot = Join-Path (Split-Path -Parent $here) "_brain_snapshots"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$snap = Join-Path $snapRoot $stamp

New-Item -ItemType Directory -Force -Path $snap | Out-Null
Copy-Item (Join-Path $here "*.vb") $snap
$shaderSrc = Join-Path $here "shaders"
if (Test-Path $shaderSrc) {
    $shaderDst = Join-Path $snap "shaders"
    New-Item -ItemType Directory -Force -Path $shaderDst | Out-Null
    Copy-Item (Join-Path $shaderSrc "*") $shaderDst -Recurse -Force
}
$n = @(Get-ChildItem (Join-Path $snap "*.vb")).Count
Write-Output "snapshot $stamp - $n file(s)"

# PRUNE, OLDEST FIRST. A folder that only grows is one nobody looks in twice.
$all = @(Get-ChildItem $snapRoot -Directory | Sort-Object Name)
if ($all.Count -gt $Keep) {
    $all[0..($all.Count - $Keep - 1)] | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force
    }
    Write-Output "pruned to the last $Keep"
}

# A running app holds the exe.
Get-Process BrainTesting -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$proj = Join-Path $here "BrainTesting.vbproj"
$vct = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VC\v180\"
$out = & dotnet build $proj -c Debug -p:Platform=x64 -p:BuildProjectReferences=false "-p:VCTargetsPath=$vct" 2>&1
$errors = @($out | Select-String -Pattern "error BC")
if ($errors.Count -gt 0) {
    Write-Output "BUILD FAILED - the snapshot is at $snap"
    $errors | Select-Object -First 8 | ForEach-Object { Write-Output $_.Line }
    exit 1
}
Write-Output "build ok"

# -Graph starts with the node board driving instead of RangeBrain, which is
# what a scorecard run against it needs - see the graph arg in Program.vb.
$launchArgs = @($Map, "perteam=1", "restore", "learn")
if ($Graph) { $launchArgs += "graph" }

if (-not $NoLaunch) {
    $exe = Join-Path $here "bin\x64\Debug\net8.0-windows\BrainTesting.exe"
    Start-Process -FilePath $exe `
        -ArgumentList ($launchArgs + @("owner=Tank AI work")) `
        -WorkingDirectory (Split-Path -Parent $exe)
    Start-Sleep -Milliseconds 1500
    $p = @(Get-Process BrainTesting -ErrorAction SilentlyContinue)
    if ($p.Count -gt 0) { Write-Output ("running, PID " + $p[0].Id) }
    else { Write-Output "did not start" }
}
