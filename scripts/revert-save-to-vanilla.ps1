<#
.SYNOPSIS
    Reverts an Against the Storm profile modified by Imperial Logistics & Overtime back to 100% vanilla-compatible saves.
.DESCRIPTION
    Creates a timestamped backup of all .save files in each target profile folder, then cleanly rewrites:
      - Imperial Highways -> Reinforced Road (in Save.save, MetaSave.save, WorldSave.save, and *_Backup.save)
      - Star-upgraded workshop recipe clones (*_ImpG1/2/3, *_ImpBaseG1*) -> original vanilla recipe names
      - Overtime Grand Orders (*_GrandOrder_Wave*) -> removed cleanly
      - Hearth Storm-Ward perk (*_HearthStormWard_Effect) -> removed cleanly (and hostility points restored if recorded)
    Preserves all vanilla buildings, goods, villagers, camp/storage upgrades, vanilla 4th recipes,
    Citadel currencies (Food Stockpiles, Machinery, Artifacts), Seal Fragments, and Cycle Embarkation Boons.
.PARAMETER SaveDir
    Optional path to a specific Against the Storm profile folder (or root save folder).
    Defaults to "$env:USERPROFILE\AppData\LocalLow\Eremite Games\Against the Storm".
.PARAMETER GameDir
    Optional path to the Against the Storm Steam install (used to load Newtonsoft.Json.dll for lossless JSON round-tripping).
.PARAMETER DryRun
    Preview changes without writing any files.
.EXAMPLE
    .\scripts\revert-save-to-vanilla.ps1 -DryRun
.EXAMPLE
    .\scripts\revert-save-to-vanilla.ps1
#>
[CmdletBinding()]
param(
    [string]$SaveDir = (Join-Path $env:USERPROFILE "AppData\LocalLow\Eremite Games\Against the Storm"),
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Against the Storm",
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Load Newtonsoft.Json from the game's Managed folder for 100% lossless JSON round-tripping (no PS 5.1 2MB/array bugs)
$newtonsoftPath = Join-Path $GameDir "Against the Storm_Data\Managed\Newtonsoft.Json.dll"
if (-not (Test-Path $newtonsoftPath)) {
    throw "Could not find Newtonsoft.Json.dll at '$newtonsoftPath'. Pass -GameDir pointing to your Against the Storm install."
}
Add-Type -Path $newtonsoftPath

function Test-IsHighway([string]$name) {
    return (-not [string]::IsNullOrEmpty($name)) -and $name.EndsWith("_Imperial_Highway", [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsGrandOrder([string]$name) {
    return (-not [string]::IsNullOrEmpty($name)) -and ($name.IndexOf("imperiallogistics_GrandOrder_", [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Test-IsStormWard([string]$name) {
    return (-not [string]::IsNullOrEmpty($name)) -and ($name.IndexOf("imperiallogistics_HearthStormWard", [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Test-IsRecipeClone([string]$name) {
    return (-not [string]::IsNullOrEmpty($name)) -and ($name.IndexOf("imperiallogistics_", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) `
        -and (($name.IndexOf("_ImpG", [System.StringComparison]::Ordinal) -ge 0) -or ($name.IndexOf("_ImpBaseG1", [System.StringComparison]::Ordinal) -ge 0))
}

function Resolve-VanillaRecipeName([string]$cloneName, [hashtable]$exactMap) {
    if ($exactMap.ContainsKey($cloneName)) {
        return $exactMap[$cloneName]
    }

    # Check suffix match in case the GUID prefix changed across versions
    $idx = $cloneName.IndexOf("_")
    if ($idx -gt 0) {
        $suffix = $cloneName.Substring($idx + 1)
        foreach ($k in $exactMap.Keys) {
            if ($k.EndsWith($suffix, [System.StringComparison]::Ordinal)) {
                return $exactMap[$k]
            }
        }
    }

    # Best-effort fallback for older saves made before CloneToVanillaRecipeMap was persisted:
    # Strip "<guid>_" prefix and "_ImpBaseG1" / "_ImpG[123]" suffixes, then restore common "[Category] " prefixes.
    $stripped = $cloneName -replace "^[^_]+_", ""
    $stripped = $stripped -replace "(_ImpBaseG1)?_ImpG[1-3]$", ""
    $stripped = $stripped -replace "_ImpBaseG1$", ""

    $bracketPrefixes = @(
        @("Building_Material_", "[Building Material] "),
        @("Food_Processed_", "[Food Processed] "),
        @("Needs_Fullfilment_", "[Needs Fullfilment] "),
        @("Needs_Fulfillment_", "[Needs Fulfillment] "),
        @("Mat_Processed_", "[Mat Processed] "),
        @("Vessel_", "[Vessel] "),
        @("Valuable_", "[Valuable] ")
    )
    foreach ($pair in $bracketPrefixes) {
        if ($stripped.StartsWith($pair[0], [System.StringComparison]::Ordinal)) {
            $rest = $stripped.Substring($pair[0].Length).Replace("_", " ")
            return ($pair[1] + $rest)
        }
    }

    return $stripped.Replace("_", " ")
}

function Remove-MatchingJArrayStrings([Newtonsoft.Json.Linq.JArray]$arr, [scriptblock]$pred) {
    if ($null -eq $arr) { return 0 }
    $count = 0
    for ($i = $arr.Count - 1; $i -ge 0; $i--) {
        if ($arr[$i].Type -eq [Newtonsoft.Json.Linq.JTokenType]::String -and (& $pred ([string]$arr[$i]))) {
            $arr.RemoveAt($i)
            $count++
        }
    }
    return $count
}

function Replace-MatchingJArrayStrings([Newtonsoft.Json.Linq.JArray]$arr, [scriptblock]$pred, [string]$replacement) {
    if ($null -eq $arr) { return 0 }
    $count = 0
    for ($i = 0; $i -lt $arr.Count; $i++) {
        if ($arr[$i].Type -eq [Newtonsoft.Json.Linq.JTokenType]::String -and (& $pred ([string]$arr[$i]))) {
            $arr[$i] = [Newtonsoft.Json.Linq.JValue]::new($replacement)
            $count++
        }
    }
    return $count
}

if (-not (Test-Path $SaveDir)) {
    throw "Save directory not found: '$SaveDir'"
}

# Discover target directories containing *.save files (root + immediate profile subdirectories)
$profileDirs = New-Object System.Collections.Generic.List[string]
if (Get-ChildItem -Path $SaveDir -Filter "*.save" -File -ErrorAction SilentlyContinue) {
    $profileDirs.Add($SaveDir)
}
Get-ChildItem -Path $SaveDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "VanillaRevertBackup_*" } | ForEach-Object {
    if (Get-ChildItem -Path $_.FullName -Filter "*.save" -File -ErrorAction SilentlyContinue) {
        $profileDirs.Add($_.FullName)
    }
}

if ($profileDirs.Count -eq 0) {
    Write-Host "No Against the Storm .save files found under '$SaveDir'."
    exit 0
}

$vanillaRoad = "Reinforced Road"
$candidateSaveNames = @(
    "Save.save",
    "Save_Backup.save",
    "MetaSave.save",
    "MetaSave_Backup.save",
    "MetaSave_GameWonBackup.save",
    "MetaSave_GameWonBackup_Backup.save",
    "WorldSave.save",
    "WorldSave_Backup.save"
)

foreach ($dir in $profileDirs) {
    Write-Host "`n=== Scanning profile folder: $dir ===" -ForegroundColor Cyan

    # Load sidecar state if present for exact CloneToVanillaRecipeMap and Storm-Ward hostility restoration
    $exactRecipeMap = @{}
    $stormWardTotalReduction = 0
    $sidecarPath = Join-Path $dir "ImperialLogistics_SettlementState.json"
    if (Test-Path $sidecarPath) {
        try {
            $sidecarObj = [Newtonsoft.Json.Linq.JObject]::Parse([System.IO.File]::ReadAllText($sidecarPath))
            if ($sidecarObj["CloneToVanillaRecipeMap"] -is [Newtonsoft.Json.Linq.JObject]) {
                foreach ($p in $sidecarObj["CloneToVanillaRecipeMap"].Properties()) {
                    $exactRecipeMap[$p.Name] = [string]$p.Value
                }
            }
            if ($sidecarObj["StormWardAppliedReductionByHearth"] -is [Newtonsoft.Json.Linq.JObject]) {
                foreach ($p in $sidecarObj["StormWardAppliedReductionByHearth"].Properties()) {
                    $val = [int]$p.Value
                    if ($val -gt 0) { $stormWardTotalReduction += $val }
                }
            }
            Write-Host "  Loaded sidecar map ($($exactRecipeMap.Count) recipe mappings, Storm-Ward hostility=$stormWardTotalReduction)."
        }
        catch {
            Write-Warning "  Could not parse sidecar JSON '$sidecarPath': $($_.Exception.Message)"
        }
    }

    $filesToEdit = New-Object System.Collections.Generic.List[string]
    foreach ($name in $candidateSaveNames) {
        $full = Join-Path $dir $name
        if (-not (Test-Path $full)) { continue }
        $raw = [System.IO.File]::ReadAllText($full)
        if ($raw.IndexOf("imperiallogistics_", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $filesToEdit.Add($full)
        }
    }

    if ($filesToEdit.Count -eq 0) {
        Write-Host "  Already clean — 0 Imperial Logistics model IDs found." -ForegroundColor Green
        continue
    }

    if (-not $DryRun) {
        $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupDir = Join-Path $dir "VanillaRevertBackup_$stamp"
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        Get-ChildItem -Path $dir -Filter "*.save" -File | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $backupDir $_.Name) -Force
        }
        if (Test-Path $sidecarPath) {
            Copy-Item -Path $sidecarPath -Destination (Join-Path $backupDir (Split-Path $sidecarPath -Leaf)) -Force
        }
        Write-Host "  Backup created: $backupDir" -ForegroundColor Yellow
    }

    foreach ($filePath in $filesToEdit) {
        $fileName = Split-Path $filePath -Leaf
        $raw = [System.IO.File]::ReadAllText($filePath)
        $root = [Newtonsoft.Json.Linq.JObject]::Parse($raw)

        $changes = 0

        # 1. content lists
        $content = $root["content"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $content) {
            $changes += Remove-MatchingJArrayStrings ($content["buildings"] -as [Newtonsoft.Json.Linq.JArray]) { param($s) Test-IsHighway $s }
            $changes += Remove-MatchingJArrayStrings ($content["essentialBuildings"] -as [Newtonsoft.Json.Linq.JArray]) { param($s) Test-IsHighway $s }
            $changes += Remove-MatchingJArrayStrings ($content["replacedBuildings"] -as [Newtonsoft.Json.Linq.JArray]) { param($s) Test-IsHighway $s }
        }

        # 2. WorldSave cycle.buildingsConstructed
        $cycle = $root["cycle"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $cycle) {
            $changes += Replace-MatchingJArrayStrings ($cycle["buildingsConstructed"] -as [Newtonsoft.Json.Linq.JArray]) { param($s) Test-IsHighway $s } $vanillaRoad
        }

        # 3. Save stats.buildingsConstructed
        $stats = $root["stats"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $stats) {
            $changes += Replace-MatchingJArrayStrings ($stats["buildingsConstructed"] -as [Newtonsoft.Json.Linq.JArray]) { param($s) Test-IsHighway $s } $vanillaRoad
        }

        # 4. buildings.roads & buildings.workshops
        $buildings = $root["buildings"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $buildings) {
            $roads = $buildings["roads"] -as [Newtonsoft.Json.Linq.JArray]
            if ($null -ne $roads) {
                foreach ($r in $roads) {
                    $ro = $r -as [Newtonsoft.Json.Linq.JObject]
                    if ($null -ne $ro -and (Test-IsHighway ([string]$ro["model"]))) {
                        $ro["model"] = [Newtonsoft.Json.Linq.JValue]::new($vanillaRoad)
                        $changes++
                    }
                }
            }

            $workshops = $buildings["workshops"] -as [Newtonsoft.Json.Linq.JArray]
            if ($null -ne $workshops) {
                foreach ($ws in $workshops) {
                    $wso = $ws -as [Newtonsoft.Json.Linq.JObject]
                    if ($null -eq $wso) { continue }

                    $recipes = $wso["recipes"] -as [Newtonsoft.Json.Linq.JArray]
                    if ($null -ne $recipes) {
                        foreach ($rec in $recipes) {
                            $reco = $rec -as [Newtonsoft.Json.Linq.JObject]
                            if ($null -ne $reco -and (Test-IsRecipeClone ([string]$reco["model"]))) {
                                $vanillaName = Resolve-VanillaRecipeName ([string]$reco["model"]) $exactRecipeMap
                                $reco["model"] = [Newtonsoft.Json.Linq.JValue]::new($vanillaName)
                                $changes++
                            }
                        }
                    }

                    $prods = $wso["production"] -as [Newtonsoft.Json.Linq.JArray]
                    if ($null -ne $prods) {
                        foreach ($prod in $prods) {
                            $po = $prod -as [Newtonsoft.Json.Linq.JObject]
                            if ($null -ne $po -and (Test-IsRecipeClone ([string]$po["recipe"]))) {
                                $vanillaName = Resolve-VanillaRecipeName ([string]$po["recipe"]) $exactRecipeMap
                                $po["recipe"] = [Newtonsoft.Json.Linq.JValue]::new($vanillaName)
                                $changes++
                            }
                        }
                    }
                }
            }
        }

        # 5. trends
        $trends = $root["trends"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $trends) {
            $queued = $trends["queuedOperations"] -as [Newtonsoft.Json.Linq.JArray]
            if ($null -ne $queued) {
                foreach ($op in $queued) {
                    $opo = $op -as [Newtonsoft.Json.Linq.JObject]
                    if ($null -ne $opo -and (Test-IsHighway ([string]$opo["ownerModel"]))) {
                        $opo["ownerModel"] = [Newtonsoft.Json.Linq.JValue]::new($vanillaRoad)
                        $changes++
                    }
                }
            }

            $goodsOps = $trends["goodsOperations"] -as [Newtonsoft.Json.Linq.JObject]
            if ($null -ne $goodsOps) {
                foreach ($prop in $goodsOps.Properties()) {
                    $list = $prop.Value -as [Newtonsoft.Json.Linq.JArray]
                    if ($null -eq $list) { continue }
                    foreach ($op in $list) {
                        $opo = $op -as [Newtonsoft.Json.Linq.JObject]
                        if ($null -ne $opo -and (Test-IsHighway ([string]$opo["ownerModel"]))) {
                            $opo["ownerModel"] = [Newtonsoft.Json.Linq.JValue]::new($vanillaRoad)
                            $changes++
                        }
                    }
                }
            }
        }

        # 6. orders.currentOrders
        $orders = $root["orders"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $orders) {
            $curOrders = $orders["currentOrders"] -as [Newtonsoft.Json.Linq.JArray]
            if ($null -ne $curOrders) {
                for ($i = $curOrders.Count - 1; $i -ge 0; $i--) {
                    $oo = $curOrders[$i] -as [Newtonsoft.Json.Linq.JObject]
                    if ($null -ne $oo -and (Test-IsGrandOrder ([string]$oo["model"]))) {
                        $curOrders.RemoveAt($i)
                        $changes++
                    }
                }
            }
        }

        # 7. effects.perks (Storm-Ward) + optional hostility restoration
        $perksRemoved = 0
        $effects = $root["effects"] -as [Newtonsoft.Json.Linq.JObject]
        if ($null -ne $effects) {
            $perks = $effects["perks"] -as [Newtonsoft.Json.Linq.JObject]
            if ($null -ne $perks) {
                $keysToRemove = @($perks.Properties() | Where-Object { Test-IsStormWard $_.Name } | ForEach-Object { $_.Name })
                foreach ($k in $keysToRemove) {
                    $perks.Remove($k) | Out-Null
                    $perksRemoved++
                    $changes++
                }
            }
        }

        if ($perksRemoved -gt 0 -and $stormWardTotalReduction -gt 0) {
            $hostility = $root["hostility"] -as [Newtonsoft.Json.Linq.JObject]
            if ($null -ne $hostility) {
                if ($null -ne $hostility["points"]) {
                    $hostility["points"] = [Newtonsoft.Json.Linq.JValue]::new(([int]$hostility["points"]) + $stormWardTotalReduction)
                }
                $sources = $hostility["sources"] -as [Newtonsoft.Json.Linq.JObject]
                if ($null -ne $sources -and $sources["1001"] -is [Newtonsoft.Json.Linq.JObject]) {
                    $s1001 = $sources["1001"]
                    if ($null -ne $s1001["points"]) {
                        $s1001["points"] = [Newtonsoft.Json.Linq.JValue]::new(([int]$s1001["points"]) + $stormWardTotalReduction)
                    }
                }
            }
        }

        $outJson = $root.ToString([Newtonsoft.Json.Formatting]::Indented)
        $remaining = ([regex]::Matches($outJson, "imperiallogistics_", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count

        if ($DryRun) {
            Write-Host "  [DryRun] $fileName : $changes replacements (remaining mod IDs: $remaining)" -ForegroundColor Yellow
        }
        else {
            [System.IO.File]::WriteAllText($filePath, $outJson, [System.Text.UTF8Encoding]::new($false))
            Write-Host "  Updated $fileName : $changes replacements (remaining mod IDs: $remaining)" -ForegroundColor Green
        }
    }
}

Write-Host "`nDone! You can now launch Against the Storm in vanilla without mods." -ForegroundColor Cyan
