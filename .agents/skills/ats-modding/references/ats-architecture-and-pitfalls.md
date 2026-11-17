# Against the Storm Architecture & Critical Pitfalls Reference

This reference captures the core `Eremite` engine conventions and hard-won pitfalls discovered while developing **Imperial Logistics & Overtime**.

---

## 1. Critical Engine Pitfalls (Must-Read)

### Pitfall A: Non-Readable Vanilla GPU Sprites Crash `ATS_API` `.SetIcon()`
- **Symptom:** `UnityException: Texture '...' is not readable, the texture memory can not be accessed from scripts` inside `ATS_API.TextMeshProManager`.
- **Cause:** Vanilla `GoodModel.icon` and `BuildingModel.icon` sprites live on GPU-only atlas textures (`texture.isReadable == false`). Calling `builder.SetIcon(sprite)` in `ATS_API` attempts to copy pixels on the CPU to build a TMP sprite sheet.
- **Fix:** Never pass vanilla sprites into `.SetIcon(...)`. Instead, assign the field directly on the built model:
  ```csharp
  var model = builder.EffectModel;
  model.overrideIcon = GoodsTypes.Hearth_Parts.ToGoodModel()?.icon;
  ```

### Pitfall B: Mid-Game `LocalizationManager.AddString` Causes Infinite Scene Reloads
- **Symptom:** Opening a tooltip or changing a config slider freezes or infinitely reloads the settlement scene.
- **Cause:** Calling `ATS_API.Localization.LocalizationManager.AddString(...)` after `MainController.InitReferences` triggers `TextsService.OnTextsChanged`, which causes `LanguageGameReloader` to reload the entire active scene!
- **Fix:**
  1. Only call `LocalizationManager.ToLocaText` / `AddString` **once** inside `MainController.InitReferences`.
  2. For dynamic or config-driven text (e.g., Difficulty-scaled rewards or live speed sliders), patch the property getter (`EffectModel.Description` or `BuildingModel.Description`) with a `[HarmonyPostfix]` that checks `ReferenceEquals(__instance, _myCustomModel)` first before computing the string.

### Pitfall C: Cloning `ScriptableObject` Models (`WorkshopRecipeModel`, `RoadModel`, etc.)
- When cloning a vanilla `ScriptableObject` at runtime via `UnityEngine.Object.Instantiate(source)`:
  1. Set `clone.name = newUniqueName;`
  2. Set `clone.hideFlags = HideFlags.HideAndDontSave;` so Unity scene cleanup never destroys or serializes runtime clones.
  3. Filter out deprecated vanilla models whose names contain `-_todelete_-` before batch-cloning recipes or buildings.
  4. Append new models to the backing array on `MB.Settings` (e.g., `settings.workshopsRecipes`) **and** rebuild or insert into its private dictionary cache if `Settings` caches lookups by name (`settings.workshopsRecipesCache`).

### Pitfall D: Sidecar Save Persistence & Settlement Fingerprinting
- **Why Sidecar JSON:** Injecting custom classes into `Eremite`'s Sirenix-serialized `Save.save` breaks player saves if they ever disable or uninstall your mod. Write a sidecar JSON file next to `Save.save` (resolved via `ProfilesService.GetSaveFilePath()`).
- **Transactional Flushing:** Patch `StateService.SaveState` and `StateService.ForceSaveState` so your sidecar file flushes at the exact moment the game autosaves or exits.
- **Settlement Fingerprinting:** Because `Save.save` is reused at the same path from one settlement to the next, record a fingerprint (`$"{seed}:{biomeName}:{worldMapField}"`) inside your sidecar JSON. On `StartGame`, if the fingerprint differs from the active settlement, reset your sidecar state cleanly so upgrades/flags never bleed into a new town!

### Pitfall E: Granting World-Map / Cycle Rewards Inside a Settlement
- Inside a settlement scene (`GameMB`), `MB.MetaStateService` and `MB.WorldStateService` exist, **but `WorldMB.CycleEffectsService` is typically null** because the World Map scene is unloaded.
- If you grant Smoldering City currencies or Cycle Caravan Boons mid-settlement:
  1. Add currencies via `MB.MetaPerksService.GetMetaCurrencyGain(...)` + `MB.MetaEconomyService.Add(...)`.
  2. If incrementing `WorldStateService.CycleEffects.bonusEmbarkVillagers`, also call `MB.MetaStateService.RebuildEmbarkBonuses()` immediately — otherwise the 3 pre-generated embarkation caravans for the next settlement won't get the extra villager!
  3. Call **both** `MetaStateService.SaveState()` and `MetaStateService.SaveGameWonBackup()` (vanilla *Continue Settlement* restores/reads backup state on return to the World Map).

### Pitfall F: Moving Buildings / Pathfinding Reachability
- `ConstructionService.CanConstructOn(model, pos, rot, GridCheck.GridOnlyOthers | GridCheck.Glades | GridCheck.Self)` verifies tile occupancy and glade bounds, **not** whether villagers can walk there! Cliffs and ponds create isolated walkability islands.
- Always compare the A* `GraphNode.Area` component ID of a candidate tile (`RoadsService.GetNode(candidatePos)`) against the Main Warehouse entrance tile before auto-relocating any camp.

### Pitfall G: Villager Movement Speed Clamping
- Vanilla `EffectsService.GetActorSpeed` clamps villager speed between `PerksConfig.minVillagerAbsoluteSpeed` (`0.05`) and `PerksConfig.maxVillagerAbsoluteSpeed` (`20.0`, against `RaceModel.baseSpeed = 1.8`).
- `RoadModel.modificator` is a multiplier (`1.0` = Dirt `+0%`, `1.15` = Paved `+15%`, `1.25` = Reinforced `+25%`, `2.0` = `+100%`), **not** an additive delta.

---

## 2. Key `Eremite` Service Map (Quick Lookup)

See `Helpers/Svc.cs` in this repository for clean typed wrappers around `Eremite.MB` (global singletons) and `Eremite.GameMB` (active settlement singletons):

- **Static Config DB (`Eremite.MB.Settings`):**
  - `Goods`, `Buildings`, `Workshops`, `workshopsRecipes`, `roads`, `orders`, `effects`, `Automatons`, `PerksConfig`
- **Settlement Services (`Eremite.GameMB` — valid only when `GameMB.IsGameActive` is `true`):**
  - `StateService`: Active settlement save state (`GameObjectives`, `Prefs`, `Buildings`, etc.)
  - `StorageService`: Main Warehouse inventory (`Main.GetAmount(goodName)`, `Main.Store(...)`, `Main.Remove(...)`)
  - `BuildingsService`: Live dictionaries of `Workshops`, `Camps`, `Woodcutters`, `Gatherers`, `Hearths`, `Storages`, `Roads`
  - `WorkshopsService`: Global production limits (`GetGlobalLimitFor`, `SetGlobalLimitFor`)
  - `ConstructionService` & `GridService`: Grid queries and placement validation
  - `OrdersService`: Active `Orders`, `OnOrderCompleted` (`IObservable<OrderState>`)
  - `HostilityService`: Forest hostility (`AddEffectHostility`, `RemoveEffectHostility`)
  - `AutomatonsService`: Spawning autonomous workers (`SpawnNewAutomaton`)
  - `NewsService`: Top-right HUD notifications (`PublishNews`)
- **Meta / World Services (`Eremite.MB`):**
  - `MetaEconomyService`, `MetaPerksService`, `MetaStateService`, `WorldStateService`

---

## 3. UI Extension Guidelines

- Native building panels (`WorkshopPanel`, `CampPanel`, `WoodcuttersPanel`, `HearthPanel`, `StoragePanel`, `BuildingPanel`) use Unity `RectTransform` hierarchies with `TMP_Text` (`Unity.TextMeshPro`) and `UnityEngine.UI.Button`.
- When docking custom UI controls onto a vanilla panel:
  - Clone an existing `TMP_Text` font/material reference (`TMP_Text.font`) from the panel so text matches the game's localized font asset cleanly.
  - Refresh button interactability if Warehouse pack/essence counts change while the panel stays open.
  - Note: Fixed-height popups like `DecisionPopup` (`Move On` / confirmation dialogs) have limited vertical space (~3–4 compact lines before overlapping the `YES`/`NO` buttons); use `<size=88%>` rich text and keep copy concise.
