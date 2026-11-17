---
name: ats-modding
description: >-
  Workflow, architecture patterns, and critical engine pitfalls for creating or extending
  Against the Storm C# mods with BepInEx 5, HarmonyX, and ATS_API. Activate this skill
  whenever building new features in ATS_ImperialLogistics, debugging BepInEx logs,
  inspecting game assemblies, or bootstrapping a new Against the Storm mod.
---

# Against the Storm Modding Skill (`BepInEx 5` + `HarmonyX` + `ATS_API`)

Use this skill to extend **Imperial Logistics & Overtime** or to scaffold a brand-new *Against the Storm* C# mod cleanly without hitting engine traps or copyright issues.

Read [references/ats-architecture-and-pitfalls.md](./references/ats-architecture-and-pitfalls.md) whenever touching `ATS_API` builders, localization, custom UI, save persistence, pathfinding, or meta-progression rewards.

---

## 1. Clean-Room & Licensing Guardrails

1. **Never copy decompiled game method bodies.** Inspect public/internal type signatures, fields, enums, and events from `Assembly-CSharp.dll` and `API.dll` using metadata reflection or `Mono.Cecil` (see Step 4), then write original C# logic and Harmony prefixes/postfixes.
2. **Never commit game or third-party `.dll`s, `.assets`, or saves to git.** Keep `*.dll`, `*.pdb`, `*.zip`, `bin/`, `obj/`, `dist/`, and `ci-libs/` in `.gitignore`.
3. **Never hardcode local OS usernames in committed files.** Use `$(APPDATA)` in MSBuild and `$env:APPDATA` in PowerShell scripts.

---

## 2. Build, Package & Verify Loop

### Local Build & Auto-Deploy
From the project root:

```powershell
dotnet build -c Release
```

In local development mode (`UseCiReferences != true`), `ATS_ImperialLogistics.csproj`:
- Compiles against `$(GameDir)\Against the Storm_Data\Managed` (`C:\Program Files (x86)\Steam\steamapps\common\Against the Storm` by default; override via `-p:GameDir="..."`) and `%APPDATA%\Thunderstore Mod Manager\DataFolder\AgainstTheStorm\profiles\Default\BepInEx`.
- Packages `dist/<PackagePrefix>-<Version>.zip` for Thunderstore.
- Copies the compiled `.dll` directly into `%APPDATA%\Thunderstore Mod Manager\DataFolder\AgainstTheStorm\profiles\Default\BepInEx\plugins\<PackagePrefix>\` if that folder exists.

### Log Verification (After Launching the Game)
Always inspect the BepInEx log after testing in-game:

```text
%APPDATA%\Thunderstore Mod Manager\DataFolder\AgainstTheStorm\profiles\Default\BepInEx\LogOutput.log
```

Confirm:
- Your plugin logs initialization cleanly during `MainController.InitReferences`.
- Zero `NullReferenceException`, `MissingMethodException`, texture readability exceptions, or scene-reload loops appear.

---

## 3. Game Lifecycle & Hook Timing

*Against the Storm* initializes data and services in distinct phases. Hooking the wrong phase causes `NullReferenceException`s or infinite loops:

| Phase | Entry Point | What Exists | What to Do Here |
| :--- | :--- | :--- | :--- |
| **Plugin Boot** | `BaseUnityPlugin.Awake()` | `Config`, `Logger`, `Harmony` (`SO.Settings` is **null**) | Bind `BepInEx` `ConfigEntry<T>` settings and call `_harmony.PatchAll()`. Do **not** touch `Svc.Cfg` or `ATS_API` model builders yet. |
| **Static DB Ready** | `[HarmonyPostfix]` on `MainController.InitReferences` | `MB.Settings` (`Svc.Cfg`) — all vanilla `GoodModel`, `BuildingModel`, `WorkshopRecipeModel`, `RoadModel`, `OrderModel` assets | Register custom `ATS_API` effects/orders, clone/inject `ScriptableObject` models, and register static `LocalizationManager` strings **once** (guard with a `_modelsRegistered` flag). |
| **Settlement Start** | `[HarmonyPostfix]` on `GameController.StartGame` | `GameMB.StateService`, `StorageService`, `BuildingsService`, `OrdersService`, etc. (`Svc.Active == true`) | Load sidecar settlement state, reconcile saved mod upgrades onto live buildings, and subscribe to `UniRx` `IObservable<T>` streams (disposing them on settlement exit!). |
| **Periodic Simulation** | Throttled `Update()` (e.g., every `1–4s`) | Active settlement services | Guard with `if (!Svc.Active || Time.timeScale <= 0f) return;` so automation never runs on the Main Menu, World Map, or while paused / in Victory popups. |
| **Save & Teardown** | `StateService.SaveState` / `GameController.OnDestroy` | Settlement save path | Flush sidecar JSON state transactionally with `Save.save`, and `.Dispose()` all `IDisposable` `UniRx` subscriptions on `OnDestroy`. |

---

## 4. Safe Metadata Inspection Recipe (PowerShell)

When you need exact method names, parameter types, or enum values from `Assembly-CSharp.dll` or `API.dll`, inspect metadata only via `Mono.Cecil` (bundled in `BepInEx/core/Mono.Cecil.dll`):

```powershell
$cecil = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\AgainstTheStorm\profiles\Default\BepInEx\core\Mono.Cecil.dll"
$asmPath = "C:\Program Files (x86)\Steam\steamapps\common\Against the Storm\Against the Storm_Data\Managed\Assembly-CSharp.dll"
[System.Reflection.Assembly]::LoadFrom($cecil) | Out-Null
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath)
$type = $asm.MainModule.GetType("Eremite.Services.OrdersService")
$type.Methods | ForEach-Object { "$($_.ReturnType.Name) $($_.Name)($(( $_.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name } ) -join ', '))" }
$asm.Dispose()
```

---

## 5. Bootstrapping a Brand-New Mod From Scratch

To start a separate *Against the Storm* mod using this repository as a template:

1. **Copy the project skeleton:** Copy `ATS_ImperialLogistics.csproj`, `NuGet.Config`, `.gitignore`, `manifest.json`, and `Helpers/Svc.cs`.
2. **Rename identifiers:**
   - Update `<AssemblyName>`, `<RootNamespace>`, `<Author>`, and `<PackagePrefix>` in your `.csproj`.
   - Choose a new `PluginGuid` (e.g., `com.<author>.ats.<modname>`) in `Plugin.cs`.
   - Update `name`, `version_number`, `description`, and `website_url` in `manifest.json` (`dependencies` should include `BepInEx-BepInExPack-5.4.2100` and `ATS_API_Devs-API-3.2.1` or newer).
   - Provide a `256x256` `icon.png` (required by Thunderstore).
3. **Review engine pitfalls before coding:** Read [references/ats-architecture-and-pitfalls.md](./references/ats-architecture-and-pitfalls.md) so you avoid the GPU sprite crash, localization reload loop, and save-desync traps.
4. **(Optional) GitHub Actions Trusted Releases:** Run `scripts/pack-ci-refs.ps1` locally to generate a stripped metadata-only `obj/ci-refs.zip` (all IL bodies replaced with `throw null;` and embedded resources cleared), host it in a private storage URL pointed to by repo secret `ATS_REFS_URL`, and reuse `.github/workflows/release.yml`.
