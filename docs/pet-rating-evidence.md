# Pet rating corrections and item evidence

Pet equipment ratings compare the complete effective equipment before and after a replacement. Dual wield, inactive Secondary configuration, two-handed conflicts, numbered-position effects and physical-copy limits apply to the same equipment projection used by the inventory and Compare pets.

**Best compatible** recognizes an equipped item at its recorded rank. Replacing an unchanged Evensong +5 with itself gives zero. A move uses one copy and removes its previous placement. LORE EQUIPPED duplicates cannot contribute twice. Ordinary Pet weapon ratios come from active Primary/Secondary only; a weapon's Ranged classification does not add a third ratio. Self ranged equipment remains supported.

## Observed ranked values

The following item facts were transcribed from client inspections supplied on October 21, 2026. No character information or original screenshots are bundled.

| Field | Revultant Whip +4 | Evensong +5 |
|---|---:|---:|
| Tier progress | 0/16 XP | 0/32 XP |
| Damage | 7 | 16 |
| Delay | 14 | 25 |
| Ratio, calculated from damage/delay | 0.50 | 0.64 |
| DEX / AGI | 9 / 8 | Not displayed |
| Disease / Poison / Void | 9 / 9 / 4 | Not displayed |
| Accuracy | 10% | 12.5% |
| Weight | 0.7 | 1.7 |
| Inspection damage bonus | 6 | 12 |

These observations apply at their exact rank, tier progress and compatible catalog baseline. They do not modify base item values or establish values at another rank. Damage bonus and accuracy remain display evidence, separate from Pet ratio/preference points. Void is preserved as its documented field; it is not silently equated with Corruption.

The shared resolver records evidence per field: catalog base, tier-start estimate, or client observation. New catalog fields retain their own evidence even when an item's weapon values were observed. Current fractional item XP is not available from ordinary inventory exports.

## Estimates and preference points

[EQLWiki's upgrade guide](https://eqlwiki.com/index.php/Item_Upgrade_System) describes ordinary-stat scaling and a 5% weapon rule. [EQL Tools](https://eqltools.com/learn/upgrades) explicitly records the conflicting weapon rules. The supplied client values contradict the former application of a minimum +1 damage at every rank. Unobserved ranked weapon values therefore remain **Conditional**, rather than establishing a confirmed winner. The provisional damage estimate uses the published percentage without the unsupported per-rank minimum. Ranked haste and negative-stat scaling also remain unverified. Delay stays fixed. Positive ordinary-stat estimates retain the documented rounding/minimum behavior.

The normal Pet DPS priorities are unchanged. Whip +4 replacing Evensong +5 contributes:

`(0.50 − 0.64) × 100 + 9 × 0.5 = −14 + 4.5 = −9.5 points`

AGI and resists remain visible even when their selected weight is zero. These are editable preference points, never predicted DPS. Unchanged uncertain equipment cancels from a supported marginal replacement; its absolute loadout totals remain estimates. Conditional estimates remain inspectable in arithmetic/details and do not appear as confirmed upgrades.

The October 13 [official pet changes](https://www.everquestlegends.com/patch-notes/eql-update-notes-10-13-2026) emphasize weapon ratio without publishing a complete pet DPS or hand-selection formula. No dual-wield multiplier is inferred.
