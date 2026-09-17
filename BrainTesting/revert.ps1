# Put the sources back to a snapshot. Instant.
#
# "i want instant revert."  - the owner, 2026-09-17.
#
#   .\revert.ps1              back to the newest snapshot
#   .\revert.ps1 -Back 2      two snapshots ago
#   .\revert.ps1 -List        show what there is, newest first
#   .\revert.ps1 -Stamp 20260917_143355
#
# IT SNAPSHOTS BEFORE IT REVERTS, into _snapshots\pre_revert_<stamp>. Reverting
# on top of an hour of unsaved work is the one way this tool could cost more
# than it saves, and it costs a directory copy to make that impossible.
param(
    [int]$Back = 0,
    [string]$Stamp = "",
    [switch]$List,
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
# Outside the project - see the note in run.ps1: a snapshot inside it is
# compiled as source and every type ends up defined twice.
$snapRoot = Join-Path (Split-Path -Parent $here) "_brain_snapshots"

if (-not (Test-Path $snapRoot)) {
    Write-Output "no snapshots yet - run.ps1 makes them"
    exit 1
}

# Newest first, and ignore the pre-revert copies so they cannot be reverted TO
# by counting back - they are an undo, not a history.
$all = @(Get-ChildItem $snapRoot -Directory |
         Where-Object { $_.Name -notlike "pre_revert_*" } |
         Sort-Object Name -Descending)
if ($all.Count -eq 0) {
    Write-Output "no snapshots yet - run.ps1 makes them"
    exit 1
}

if ($List) {
    for ($i = 0; $i -lt $all.Count; $i++) {
        $c = @(Get-ChildItem (Join-Path $all[$i].FullName "*.vb")).Count
        Write-Output ("[{0}] {1}  {2} file(s)" -f $i, $all[$i].Name, $c)
    }
    exit 0
}

$pick = $null
if ($Stamp -ne "") {
    $pick = $all | Where-Object { $_.Name -eq $Stamp } | Select-Object -First 1
    if ($null -eq $pick) { Write-Output "no snapshot named $Stamp"; exit 1 }
} else {
    if ($Back -ge $all.Count) { Write-Output ("only " + $all.Count + " snapshot(s)"); exit 1 }
    $pick = $all[$Back]
}

# The undo for the revert itself.
$pre = Join-Path $snapRoot ("pre_revert_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Force -Path $pre | Out-Null
Copy-Item (Join-Path $here "*.vb") $pre

Get-Process BrainTesting -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

Copy-Item (Join-Path $pick.FullName "*.vb") $here -Force
$shaderSrc = Join-Path $pick.FullName "shaders"
if (Test-Path $shaderSrc) {
    Copy-Item (Join-Path $shaderSrc "*") (Join-Path $here "shaders") -Recurse -Force
}
$n = @(Get-ChildItem (Join-Path $pick.FullName "*.vb")).Count
Write-Output ("reverted to " + $pick.Name + " - " + $n + " file(s)")
Write-Output ("what was there is in " + (Split-Path -Leaf $pre))

if ($Build) {
    $proj = Join-Path $here "BrainTesting.vbproj"
    $vct = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VC\v180\"
    $out = & dotnet build $proj -c Debug -p:Platform=x64 -p:BuildProjectReferences=false "-p:VCTargetsPath=$vct" 2>&1
    $errors = @($out | Select-String -Pattern "error BC")
    if ($errors.Count -gt 0) {
        Write-Output "BUILD FAILED after revert"
        $errors | Select-Object -First 8 | ForEach-Object { Write-Output $_.Line }
        exit 1
    }
    Write-Output "build ok"
}
