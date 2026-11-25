# Changelog

## 1.1.0
- **Overtime Smoldering City XP & Level-Ups:**
  - Grand Orders I–IV now award Difficulty-scaled **Smoldering City XP** (`+15 / +25 / +35 / +45` base, configurable) so your Citadel Level stays in sync with your Overtime Citadel Resources.
  - Each Excess Reputation Acclaim milestone also grants Difficulty-scaled **Smoldering City XP** (`+5` base, configurable).
  - Crossing a level threshold in Overtime automatically advances your Smoldering City Level, unlocks that level's rewards (`MetaRewardModel.Consume()`), and posts a Level Up alert.
  - Updated Grand Order tooltips, Reputation Bar tooltip, Move-On Summary (`+N XP (+L Lv)`), and the Victory / Continue Settlement prompts to reflect Overtime XP.

## 1.0.0
- Initial release:
  - **Automated Smart Supply Chains:** Surplus-first ingredient priority (`0–3`), edible raw food & hearth fuel safety floors (returning unused buffered ingredients), and demand-scaled intermediate production caps respecting manual local and global limits.
  - **Workshop Mastery:** Per-building Mastery Dock with individual stepped star upgrades (`0★ → ★1 → ★2 → ★3`) and 1 curated thematic 4th bonus recipe per workshop.
  - **Tier 4 Imperial Highways:** Distinct gilded royal-gold flagstones & build icon, `+45%` default walk speed (configurable live up to `+500%`), drag-to-upgrade over lower roads, available anytime from Year 1.
  - **Smart Camp Auto-Move & Haulers:** Unlockable path-verified auto-relocation (respecting Woodcutter glade safety modes) and Tier 2 Outpost Carts for Woodcutters' & Gathering Camps, plus Warehouse Hauler Automatons.
  - **Continue Settlement (*Imperial Overtime*):** Updated Victory & Continue Settlement prompts, Hearth Storm-Wards (`-45` Hostility), sequential Grand Orders I–IV awarding Difficulty-scaled Citadel currencies, Cycle Caravan Boons, and Cycle Seal Fragments (Shards), escalating Excess Reputation Acclaim rewards (Amber, Food Stockpiles, capped Cycle Embarkation Point, and alternating Machinery/Artifacts), and a compact Move-On summary.
  - **Vanilla Save Compatibility:** Native `MetaSave`/`WorldSave` hygiene when finishing settlements, a two-step Pause Menu **Prepare Save for Vanilla** converter with automatic backups, and an offline PowerShell rescue script (`scripts/revert-save-to-vanilla.ps1`).
  - **Developer & LLM Modding Kit:** Bundled `AGENTS.md` and `.agents/skills/ats-modding/SKILL.md` for extending this mod or creating new *Against the Storm* mods.
