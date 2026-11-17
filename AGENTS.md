# AGENTS.md — LLM & Coding Agent Guide

Welcome! This repository contains **Imperial Logistics & Overtime** (`ATS_ImperialLogistics`), a C# `BepInEx 5` + `HarmonyX` + `ATS_API` mod for *Against the Storm*.

If you are an AI coding agent helping a developer **improve this mod** or **create a brand-new *Against the Storm* mod**, start by reading the bundled modding skill:

- **Skill Instructions:** [.agents/skills/ats-modding/SKILL.md](.agents/skills/ats-modding/SKILL.md)
- **Engine Architecture & Pitfalls Reference:** [.agents/skills/ats-modding/references/ats-architecture-and-pitfalls.md](.agents/skills/ats-modding/references/ats-architecture-and-pitfalls.md)

---

## Repository Map

| Path | Responsibility |
| :--- | :--- |
| `Plugin.cs` | `BepInEx` entry point, lifecycle hooks (`MainController.InitReferences`, `GameController.StartGame`/`OnDestroy`), and throttled simulation ticks |
| `ModConfig.cs` | All `BepInEx` `ConfigEntry<T>` toggles, costs, multipliers, and `AcceptableValueRange` bounds |
| `Helpers/Svc.cs` | Typed accessors around `Eremite.MB` (global) and `Eremite.GameMB` (active settlement) services |
| `Helpers/EconomyHelper.cs` | Warehouse pack/essence affordability checks, spending, and cost formatting |
| `Persistence/SettlementModState.cs` | Fingerprint-isolated sidecar JSON state saved transactionally alongside `Save.save` |
| `SupplyChain/SmartSupplyChainManager.cs` | Surplus-first ingredient priority, Raw Food / Fuel safety floors, and demand-scaled intermediate caps |
| `Workshops/WorkshopMasteryManager.cs` | Cloned `★1–★3` recipe star upgrades and curated 4th bonus recipes per workshop |
| `Roads/ImperialHighwayManager.cs` | Tier 4 `Imperial_Highway` `RoadModel`, drag-over road upgrading, and live speed multiplier updates |
| `Camps/SmartCampAutoMover.cs` | Path-verified camp auto-relocation (`WoodcuttersCamp` & `Camp`) and Tier 2 Outpost Carts |
| `Overtime/ImperialOvertimeManager.cs` | Post-victory Grand Orders I–III, Citadel/Cycle meta-rewards, Hearth Storm-Wards, and Warehouse Automatons |
| `UI/ImperialUiHelper.cs` | Shared `RectTransform` / `TMP_Text` dock builder and live affordability refresh on building panels |
| `scripts/pack-ci-refs.ps1` | Generates metadata-only stripped reference stubs (`obj/ci-refs.zip`) for clean GitHub Actions builds |

---

## Core Rules for Agents

1. **Clean-Room Modding Only:** Never copy decompiled `Eremite` method implementations into repository files, and never commit `.dll`, `.pdb`, `.zip`, or game asset files to git. Inspect public/internal signatures via reflection or `Mono.Cecil`.
2. **No Local User Paths:** Always use `$(APPDATA)` in `.csproj` and `$env:APPDATA` in PowerShell scripts — never hardcode local OS user folder names.
3. **Avoid Known `ATS_API` / Unity Traps:**
   - Never pass vanilla GPU sprites (`texture.isReadable == false`) to `ATS_API`'s `.SetIcon(...)` — assign `overrideIcon = sprite` / `icon = sprite` directly.
   - Never call `LocalizationManager.AddString` after `MainController.InitReferences` (causes an infinite scene-reload loop); use Harmony property getter postfixes for dynamic text.
   - Wrap Harmony prefix/postfix bodies in `try/catch` blocks logging via `Plugin.Log.LogError(...)` so an unexpected game update never bricks vanilla UI or saves.
4. **Build & Verify:** Run `dotnet build -c Release` to compile, package `dist/*.zip`, and deploy to the local Thunderstore `Default` profile; check `%APPDATA%\Thunderstore Mod Manager\DataFolder\AgainstTheStorm\profiles\Default\BepInEx\LogOutput.log` after testing in-game.
