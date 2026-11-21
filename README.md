# Imperial Logistics & Overtime

A comprehensive Quality-of-Life and Late-Game progression mod for *Against the Storm* that streamlines settlement logistics, lets you specialize workshops & camps using crafted packs, adds Tier 4 **Imperial Highways**, and turns **Continue Settlement** into a rewarding post-victory endgame.

---

## Features

### 1. Automated Smart Supply Chains
- **Surplus-First Ingredient Priority (`0–3`):** Passively prioritizes whichever allowed recipe ingredient currently has the largest warehouse stockpile—while strictly respecting any ingredients you manually check or uncheck.
- **Raw Food & Fuel Safety Floors:** Automatically pauses non-food and non-fuel industrial recipes from draining emergency edible Raw Food (default floor: `25`) or Hearth Fuel (`25`) so villagers never starve or freeze due to crafting, returning unused buffered stock to the Warehouse.
- **Demand-Scaled Intermediate Caps:** Assigns smart default production limits (`25 + 10 per active consumer`) to intermediate goods (*Planks, Bricks, Fabric, Pipes, Flour, Containers, Pigment, Bars*) while respecting both local and global limits you set manually.

### 2. Workshop Mastery (Per-Building Upgrades)
Select any finished Workshop to access its **Mastery Dock**:
- **Individual Recipe Star Upgrades (`+1★` up to `★★★`):** Spend *Packs of Building Materials*, *Packs of Trade Goods*, and *Wildfire Essence* to step individual recipes up in speed (~18% faster per star) and input efficiency (~20% fewer ingredients per star):
  - `0★ → ★1`: `4` Packs of Building Materials
  - `★1 → ★2`: `5` Packs of Building Materials + `3` Packs of Trade Goods
  - `★2 → ★3`: `6` Packs of Trade Goods + `1` Wildfire Essence
- **Curated 4th Bonus Recipe (`★1`):** Unlock 1 thematic bonus recipe per workshop (`4` Build Packs on starter workshops like *Flour* on Field Kitchen & *Barrels* on Crude Workstation; `6` Build Packs on blueprint workshops).

### 3. Imperial Highways, Smart Camp Auto-Move & Haulers
- **Imperial Highways (Available Anytime):** Unlocked from Year 1 in the **Roads** build tab (`1 Pack of Building Materials` per tile) with distinct **gilded royal-gold flagstones** and matching build icon so they stand out clearly from Reinforced Roads. Grants **`+45%` Villager Walk Speed** by default (configurable live up to **`+500%`**) and can be dragged directly over Dirt, Paved, or Reinforced Roads to upgrade them in place.
- **Smart Camp Auto-Move:** Unlockable on **Woodcutters' Camps** (`3` Build Packs) and **Gathering Camps** (`4` Build Packs) with an `Active / Paused` toggle and `Scout & Move Now` button. Relocates idle depleted camps onto path-reachable terrain near valid targets closest to Warehouses while strictly obeying your Woodcutter **Glade Safety Mode** (*Avoid Glades*, *Only Marked*, etc.).
- **Outpost Carts (Tier 2 Camp Upgrade):** Spend `4` Trade Packs + `1` Wildfire Essence for `+5` camp carry capacity and `+20%` gathering speed.
- **Warehouse Hauler Automatons:** Deploy an autonomous Hauler Bot plus `+5` carry capacity & `+5` range on any Main/Small Warehouse (`8` Build Packs + `6` Trade Packs + `1` Wildfire Essence).

### 4. Continue Settlement Overhaul (*Imperial Overtime*)
Clicking **Continue Settlement** after winning (including **Sealed Forest** victories) activates **Imperial Overtime**:
- **Hearth Storm-Wards:** Light Wildfire Storm-Wards on Hearths (`10` Build Packs + `2` Wildfire Essence) for **`-45` Forest Hostility** per upgraded Hearth to offset endless yearly hostility scaling.
- **Grand Orders I–IV (Difficulty-Scaled Citadel & Seal Payouts):** Complete four sequential industrial commissions to bank permanent Smoldering City currencies (**Food Stockpiles**, **Machinery**, **Artifacts** — scaled automatically by your settlement's **Difficulty Multiplier** and Smoldering City `%` bonus), **Cycle-long Caravan Boons** (`+2` Bonus Embarkation Points & `+1` Bonus Starting Villager across Charters II & III), and **Cycle Seal Fragments / Shards** (`+1` base scaled by Difficulty on Charter IV).
- **Excess Reputation Acclaim:** Surplus Reputation earned past Victory (from **High Resolve**, **Abandoned Caches**, **Glade Events**, remaining **Orders**, and **Perks**) continues to fill an Overtime Acclaim meter (visible on the Reputation Bar tooltip). Every `+1.0` Excess Reputation delivers **`+15` Amber** and banks **`+3` Citadel Food Stockpiles** (Difficulty-scaled), with every **3rd** milestone triggering a **Major Acclaim** (`+1` Wildfire Essence plus **`+1` Cycle Embarkation Point** up to a configurable cap of `2` per settlement, then falling back to Difficulty-scaled **Machinery & Artifacts**).
- **Move-On Summary:** Clicking **Move On** in the pause menu displays a compact summary of all Citadel currencies, Seal Fragments, Excess Reputation milestones, and Cycle boons banked during Overtime.

---

## Save Compatibility & Reverting to Vanilla

- **Finished Settlements Are Natively Vanilla-Safe:** Once you finish a settlement (**Move On** / return to the World Map), your `MetaSave.save` and `WorldSave.save` contain zero custom mod IDs — you can disable the mod and continue your Cycle and Citadel in vanilla with all earned currencies, Seal Fragments, and Embarkation Boons intact.
- **Reverting an Active In-Progress Settlement (In-Game):** Open the Pause Menu (`Esc`) and use the two-step **`Prepare Save for Vanilla (Uninstall Helper)...`** → **`Confirm Revert to Vanilla`** control. It automatically creates a timestamped backup folder, converts Imperial Highways to Reinforced Roads, restores star-upgraded recipes to their vanilla base recipes, removes Overtime Grand Orders/Storm-Wards, and saves cleanly so you can exit and disable the mod.
- **Offline Save Rescue Script:** If you already uninstalled the mod and hit an infinite loading spinner on an active settlement, run [scripts/revert-save-to-vanilla.ps1](scripts/revert-save-to-vanilla.ps1) (`-DryRun` supported) to back up and clean your save folder automatically.

---

## Configuration

Every pillar, cost, reserve floor, speed bonus, and reward value can be toggled or tuned in:
`BepInEx/config/com.pdewilde.ats.imperiallogistics.cfg` (or live in-game via the **Mods** options tab).

---

## Getting Started (Developing & Modding with LLMs)

Whether you want to tweak this mod, implement one of the ideas below, or use the [GitHub repository](https://github.com/pdewilde/ATS-ImperialLogistics) as a starter template to build your own *Against the Storm* mod with an AI coding assistant, everything is set up to build locally in one command.

### Prerequisites
1. [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or newer).
2. *Against the Storm* installed via Steam (if installed outside `C:\Program Files (x86)\Steam\steamapps\common\Against the Storm`, pass `-p:GameDir="D:\Path\To\Against the Storm"`).
3. [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager) (or **r2modman**) with **`BepInExPack`** and **`ATS_API_Devs-API`** installed in your profile.

### Build & Auto-Deploy
```powershell
dotnet build -c Release
```
Building in `Release` compiles `ATS_ImperialLogistics.dll`, packages a Thunderstore-ready archive in `dist/`, and automatically copies the `.dll` into your Thunderstore `Default` profile's `BepInEx/plugins/` folder if present.

### Bundled LLM Agent Skill & Modding Docs
This repository includes a ready-to-use **Agent Skill** and context guide summarizing *Against the Storm*'s service architecture, lifecycle hooks, clean-room metadata inspection commands, and engine pitfalls (such as non-readable GPU sprite crashes, localization scene-reload loops, and sidecar save fingerprinting):
- **[AGENTS.md](AGENTS.md)** — Automatic repository map and guardrails picked up by coding agents (*Antigravity, Claude Code, Cursor, Codex, GitHub Copilot*, etc.).
- **[.agents/skills/ats-modding/SKILL.md](.agents/skills/ats-modding/SKILL.md)** — Step-by-step workflow for extending this mod or bootstrapping a brand-new *Against the Storm* mod from scratch.
- **[ats-architecture-and-pitfalls.md](.agents/skills/ats-modding/references/ats-architecture-and-pitfalls.md)** — Deep-dive reference on `Eremite` services, `ATS_API` gotchas, UI docking, and meta-reward persistence (can also be pasted directly into any chat LLM).

---

## Future Ideas (Contributions Welcome!)

Active development on this mod may be limited going forward, so forks, pull requests, and community spin-offs are very welcome! Here are some open ideas if you'd like to build on it:

- **Feature & Economy Balance:** Fine-tuning pack/essence upgrade costs, reserve floors, intermediate cap formulas, highway speed bonuses, and Overtime payout curves across all Prestige tiers.
- **Roguelike Variety in Overtime:** Procedural/randomized late-game Grand Order objectives and reward pools so every Continue Settlement run feels fresh.
- **Native Building Upgrade UI Integration:** Migrating custom docked upgrade rows into *Against the Storm*'s native building upgrade menu/tab instead of drawing external boxes alongside building panels.
- **DLC Testing & Integration:** Thorough testing and bonus recipe/upgrade coverage across *Keepers of the Stone* and other DLC content.

---

## Trusted Builds & Verification

Official release archives are built directly from tagged source commits on GitHub-hosted runners via [GitHub Actions](.github/workflows/release.yml) with **SLSA Build Provenance Attestations** (`actions/attest-build-provenance`). You can cryptographically verify any release archive with the GitHub CLI:

```bash
gh attestation verify pdewilde-ImperialLogistics-1.0.0.zip --owner pdewilde
```

---

## AI Disclosure

Generative AI tools were used in the process of researching, designing, and writing this mod, alongside in-game playtesting and iterative refinement.

## Acknowledgments & Disclaimer

Built with [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX), and [ATS_API](https://thunderstore.io/c/against-the-storm/p/ATS_API_Devs/API/).
This is an unofficial fan-made mod for *Against the Storm* and is not affiliated with or endorsed by Eremite Games or Hooded Horse.

## License

Licensed under the [MIT License](LICENSE).
