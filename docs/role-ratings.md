# Gear roles, item comparisons, and help

Gear's main roles are **Damage**, **Balanced**, and **Survival**. All three consider the whole character: casting resources still matter in a melee-oriented build, and survival still matters in a damage-oriented build. These are editable preference points, not predicted DPS, healing, damage reduction, or proc rates.

## Class-aware priorities

The active trio determines the casting priority:

- INT: Bard, Enchanter, Magician, Necromancer, Shadow Knight, Wizard.
- WIS: Beastlord, Cleric, Druid, Paladin, Ranger, Shaman.
- Neutral: Berserker, Monk, Rogue, Warrior.

Two or three classes from one casting family give that family the entire casting-attribute budget. One INT class, one WIS class, and one neutral class split it equally. One caster with two neutral classes uses that caster's family. Three neutral classes give no mana or casting-attribute priority. Missing, duplicate, or unknown classes cannot establish an automatic priority.

For example, SHD/SHM/BRD prioritizes INT and retains direct mana preferences in all three goals. MAG/MNK/CLR splits the casting-attribute budget equally between INT and WIS. This is a rating rule, not a statement that the minority attribute cannot affect the game's actual mana pool. The class mapping and its evidence limits are documented by [EQL Tools](https://eqltools.com/attributes).

The automatic goals start with these preference priorities:

| Priority | Damage | Balanced | Survival |
|---|---:|---:|---:|
| AC | 1 | 2 | 3 |
| HP | 0.05 | 0.1 | 0.15 |
| STA | 0.2 | 0.4 | 0.6 |
| AGI | 0.2 | 0.4 | 0.5 |
| Each Fire/Cold/Magic/Disease/Poison resist | 0.1 | 0.15 | 0.25 |
| Weapon ratio × martial factor | 100 | 50 | 15 |
| ATK × martial factor | 1 | 0.5 | 0.15 |
| Haste × martial factor | 3 | 1.5 | 0.5 |
| STR × martial factor, plus 0.05 carrying utility | 0.5 | 0.35 | 0.15 |
| DEX × martial factor | 0.5 | 0.35 | 0.15 |
| Mana × casting factor × sustain preference | 0.1 | 0.1 | 0.08 |
| Casting-attribute budget × casting factor × sustain preference | 0.5 | 0.4 | 0.25 |
| Raw Endurance: martial factor + casting factor | 0.03 + 0.02 | 0.04 + 0.03 | 0.03 + 0.02 |

The martial factor averages explicit design preferences across the trio: 1 for WAR/MNK/ROG/BER/PAL/SHD/RNG/BST, 0.75 for BRD, 0.25 for CLR/DRU/SHM, and 0.1 for pure INT casters. The casting factor is the number of mana-using classes divided by three. A trio containing CLR/DRU/SHM/PAL/SHD/BST receives a 1.25 mana/casting-attribute sustain preference. These factors express priorities, not measured game coefficients or ability-use forecasts.

Raw Endurance is read only from documented item fields. No STR/STA-to-Endurance, INT/WIS-to-Mana, or STA-to-HP conversion is invented. Weapon damage and delay are represented through ratio rather than counted again. Other stats remain visible even when their default weight is zero.

## Editing and preserving roles

**Tools → Add role…** creates a named Self role from the selected role's resolved weights. **Tools → Edit role weights…** presents AC/resources, attributes/weapons, and resists in three columns. Finite negative weights are allowed. Save commits the changes; Cancel leaves the saved role unchanged.

Editing an automatic goal creates an exact custom override for Self. **Reset weights to default** inside that goal's editor restores automatic class priorities when saved. The Gear choices are Damage, Balanced, Survival, and named custom roles. Balanced Tank, Melee, Caster, Legacy Tank, and the old Tank alias are retired from Gear. Saved selections migrate to the corresponding current goal. Customized retired presets are preserved as named Custom roles; their original definitions remain stored internally. Pet weights and Pet selections are separate and unchanged.

## Reading a comparison

The item's information button names the candidate, its preview rank, the replaced equipment and its recorded rank, and the destination. Unbuffed and Usual buffs use the same resolved weights with their own confirmed context and limits.

Gains and Losses each appear in AC, Resources, Stats, and Resists order. A real stat loss stays visible even when its weight is zero. The explanation distinguishes a role exclusion from a confirmed cap or missing evidence. Only documented changes appear; missing data is not a zero-value fact.

The formula lists contributions and their sum. Compact values are rounded, with approximate coefficients marked and any rounding adjustment identified. **Exact arithmetic** reveals the unrounded stored terms. Only the final inspector display is rounded to one decimal. An internal regression preserves the former +32.5-point example for Slime Blood of Cazic-Thule +5 replacing Gauntlets of Fiery Might +4. The current goals deliberately use different priorities.

Preview rank never changes ownership, saved inventory ranks, or other candidate rows. Missing buffed totals, incomplete equipment, and unresolved mechanics remain explicitly unavailable or conditional. Effects and procs are shown separately.

## Tooltip preferences

**Settings → Appearance → Tooltips** saves per character and applies immediately:

- **Essential** (default): meaningful control, title, metric, information, and evidence help; routine grip and slider hints are suppressed.
- **All**: includes routine interaction hints.
- **None**: suppresses tooltips while retaining control names and keyboard accessibility.

Delay options are 0.25, 0.5, **0.75 (default)**, 1, and 1.5 seconds. The selected delay also applies when moving between controls.
