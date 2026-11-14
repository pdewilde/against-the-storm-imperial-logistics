<#
.SYNOPSIS
Creates a metadata-only stripped reference archive (obj/ci-refs.zip) for GitHub Actions CI.
All executable IL method bodies and embedded resources are stripped out so zero game code is stored in CI secrets/private storage.
Note: Written to obj\ci-refs.zip (never dist\) so manual release uploads from dist\ never include reference stubs.
#>
param(
    [string]$GameManagedDir = "C:\Program Files (x86)\Steam\steamapps\common\Against the Storm\Against the Storm_Data\Managed",
    [string]$BepInExCoreDir = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\AgainstTheStorm\profiles\Default\BepInEx\core",
    [string]$OutZip = "$PSScriptRoot\..\obj\ci-refs.zip"
)

$ErrorActionPreference = "Stop"
Add-Type -Path (Join-Path $BepInExCoreDir "Mono.Cecil.dll")

$stageDir = Join-Path ([System.IO.Path]::GetTempPath()) "ats_ci_refs_stage"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir | Out-Null

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($GameManagedDir)
$readerParams = New-Object Mono.Cecil.ReaderParameters
$readerParams.AssemblyResolver = $resolver

$dllNames = @(
    "Assembly-CSharp.dll",
    "UnityEngine.UI.dll",
    "Unity.TextMeshPro.dll",
    "UniRx.dll",
    "UniTask.dll",
    "Sirenix.Serialization.dll"
)

function Strip-Types($types) {
    foreach ($type in $types) {
        foreach ($method in $type.Methods) {
            if ($method.HasBody) {
                $body = New-Object Mono.Cecil.Cil.MethodBody($method)
                $il = $body.GetILProcessor()
                $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
                $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Throw))
                $method.Body = $body
            }
        }
        if ($type.HasNestedTypes) {
            Strip-Types $type.NestedTypes
        }
    }
}

foreach ($name in $dllNames) {
    $src = Join-Path $GameManagedDir $name
    $dst = Join-Path $stageDir $name
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($src, $readerParams)
    $asm.MainModule.Resources.Clear()
    Strip-Types $asm.MainModule.Types
    $asm.Write($dst)
    $asm.Dispose()
    Write-Host "Stripped metadata stub: $name ($((Get-Item $dst).Length) bytes)"
}

$outDir = Split-Path -Parent $OutZip
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (Test-Path $OutZip) { Remove-Item $OutZip -Force }

Compress-Archive -Path "$stageDir\*" -DestinationPath $OutZip -Force
Remove-Item $stageDir -Recurse -Force
Write-Host "`nCreated metadata-only CI reference bundle: $OutZip ($((Get-Item $OutZip).Length) bytes)"
