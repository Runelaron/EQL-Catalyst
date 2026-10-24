# Item and pet measurement checklist

Use this process after the October 22, 2026 update. Keep original screenshots, exports and logs locally. Share selected measurements and short relevant log excerpts rather than your whole profile or log folder. The fillable JSON template is `measurement-template.json` beside this guide.

## What changed

Monster Summoning pets are now Warrior/Berserker. Summoned pet HP depends on STA, and mana depends on both INT and WIS. Weapon preferences and the damage bonus at exactly 28 delay changed. The official notes publish these dependencies, not their numerical formulas. Earlier measurements remain historical evidence.

Source: https://www.everquestlegends.com/patch-notes/eql-update-notes-10-22-2026

## Before every test

Record a run ID, local time with UTC offset, installed patch/build, character class trio and class level, race and separate race level, summon spell/rank, actual pet level, zone, relevant AA ranks, focus items, owner effects that affect pets, and all pet buffs. Record taunt, positioning, Dual wield, gear order and any uncertain facts. Blank/null means unknown; it does not mean zero or absent.

The pet's appearance varies with Monster Summoning. Record the successful summon and owned pet identity; appearance, a mob name or Frenzy alone does not establish the family.

## Complete pet sheets and rank tracking

Template version 2 explicitly tracks current and maximum HP/mana/endurance, all seven attributes (STR, STA, AGI, DEX, INT, WIS, CHA), both displayed AC and Attack values in their original order, attack speed, resource regeneration, and Fire/Cold/Magic/Disease/Poison/Void/Corruption resists where supported. It also reserves separate fields for documented offense/accuracy/mitigation/evasion, innate damage/delay, damage bonus, skills, damage shields and absorption. A field unavailable in the client remains null; a real zero remains zero. Preserve invalid raw values such as negative maximum HP with an invalid-evidence reason and exclude them from fitting. Never copy cropped values from another pet.

Keep four layers separate: raw equipped snapshot, explicit gear contributions, explicit buff contributions, and equipment-subtracted estimates. Equipment-subtracted attributes retain any unmeasured owner focus/AA/passive context. Removing item HP alone does not remove the HP gained through its STA. Absorption is not maximum HP. Assign per-field evidence and preserve the canonical summon separately from an unrelated inspection left open.

Record spell rank and actual pet level independently, including upgrades where level does not change. The historical guide describes one additional pet level per rank, capped at caster level, plus 6% HP, +1 innate base damage and +5 skill points per upgraded pet level. It also states that HP can continue increasing from rank 9 to 10 while a capped pet remains level 50. Therefore, a level cap must not automatically stop all rank bonuses. This is older reference guidance, not a validated October 22 numerical model; linear versus compounded HP, rounding, focus order, skills at the cap and current coefficients remain unresolved. Do not multiply all attributes, mana, resists, weapon damage or DPS by 6%.

Source: https://eqlwiki.com/index.php?title=Pet_Guide&oldid=198219#Pet_Spell_Upgrades (October 16, 2026 reference).

Represent the new dependencies as unresolved functions of context: HP depends on summon/rank, actual level, STA, focus/AA, equipment, buffs and patch; mana also depends on INT and WIS. The official October 22 notes do not publish constants. A zero-mana pet observation does not establish a zero coefficient for every pet family. Never add a separate STA term to an HP baseline that already includes the same STA contribution.

For a controlled local test, candidate HP per STA is (change in maximum HP minus independently established direct HP/buff changes) divided by change in STA. Candidate mana per INT and per WIS use the same difference method, changing one attribute while holding the other fixed and accounting for direct mana. These are local slopes only: establish additivity, repeat baseline → change → baseline, test several stat ranges/levels, and retain an independent holdout before using a coefficient elsewhere.

## 1. Item before/after upgrade

1. Choose one identifiable physical copy. Take an inventory export and a complete item-inspection screenshot immediately before upgrading.
2. Capture name, item ID if available, rank, tier progress numerator/denominator, damage, delay, displayed ratio, damage bonus, accuracy, AC, resources, attributes, resists, haste, weight and every configured effect. Capture all tabs if necessary. Do not crop out the item's name or rank.
3. Upgrade that same copy. Capture the merge message, another complete inspection screenshot and another inventory export. Record effects that changed or were removed.
4. Repeat at a completed rank boundary. To investigate fractional scaling, also capture partial tier progress with its exact numerator/denominator. Do not infer partial progress from rank alone.
5. Transcribe raw displayed values first; preserve absent versus unreadable fields. Review OCR against the image, especially minus signs, decimals, rank and damage/delay. Keep model predictions in separate columns/fields.

Worked model example: Wu's Fist has catalog damage 16 and delay 22. The adopted local model gives floor(16 × 1.1) = 17 damage at +1 and floor(16 × 1.4) = 22 at +4. These are model expectations until paired client captures confirm them. Exact existing observations remain item/rank-specific: Revultant Whip +4 is 7/14; Evensong +5 is 16/25. Damage bonus is separate from the displayed damage/delay ratio.

## 2. Unbuffed pet baseline and one-item changes

1. Summon a fresh pet at the chosen spell/rank and confirm its actual level. Record focus/passive AA effects even if they cannot be disabled. Remove removable buffs and wait for HP/mana to settle. Capture the full stat sheet, buff window, equipment/inventory and summon identity.
2. Take baseline A with no test item. Record total HP, mana, STA, INT, WIS, AC and all other visible stats. A percentage HP bar is not a total-HP measurement.
3. Add exactly one item and confirm what the game equips. Take B with the same windows and context. Record every changed stat, not just the stat being studied.
4. Restore A. If pet item removal cannot reliably restore it, resummon the same pet and verify level/focus/buffs. Take A again to detect drift or hidden state.
5. Repeat. Use multiple independent items, then reserve a different item or level as a holdout. Treat level-specific results as level-specific until tested elsewhere.

To isolate STA → HP, prefer items without raw HP; to isolate INT/WIS → mana, prefer separate INT-only and WIS-only changes without raw mana. Otherwise record raw HP/mana and every confound. Do not subtract owner-derived STA conversions or assume player formulas apply to pets.

Illustrative example only: A has 100 STA and 1,000 HP; B has 110 STA and 1,050 HP with no raw HP or other changes; restored A returns to 1,000 HP. This suggests 5 HP/STA in that exact context, but is not an implemented game coefficient. A second item, repeat and holdout are required before extending it. Mixed-stat equipment alone cannot isolate a coefficient.

## 3. Weapon selection and damage bonus

Test Primary alone, Secondary alone where valid, both hands, shield/weapon order, two-handed conflicts and different insertion orders as separate runs. Capture the actual effective weapons and full inspect values each time. Keep slot effects, worn haste, spell haste, ranks and pet level constant. Re-summon between tests when order or retained gear could matter.

For the 28-delay fix, collect a current-patch 28-delay case with exact damage bonus, pet level and equipment. Nearby-delay weapons are useful comparisons, but different damage, stats or weapon class are confounds. Do not derive a universal dual-wield multiplier or sum-of-ratios selection rule from a few samples.

## 4. Combat/log samples

Use repeated finished fights against comparable targets. Record target level/defenses when known, difficulty, duration, front/behind positioning, buffs, taunt, casting and equipment transitions. Keep transition fights separate. Use the app's summed encounter duration consistently; changing denominators can look like a gear improvement.

Compare landed-hit cadence, damage distributions, attempts/misses and melee, special attacks, spells and procs separately. Log timestamps have limited precision: they do not establish an exact swing timer. A spell's casting time alone does not prove equivalent lost melee time. Keep measured DPS separate from equipment preference points and from calibrated estimates.

In Compare pets, use Record benchmark for measured DPS or total HP. Confirm the shown setup and explicitly confirm that the measurement was after the October 22 update. Without those confirmations, retain it as historical evidence. Use Stat profiles for manually confirmed visible totals; logs do not replace a complete active-stat snapshot.

## Other collection routes

* Inventory exports: complete captures establish physical copies, locations, quantities and recorded names/ranks. They do not contain a full pet stat sheet or prove pet transfers. Continue using the existing verified Sync hotbutton/opt-in capture schedule.
* Logs: correlate loot, destruction and merges with inventory revisions; correlate successful summons with owned-pet evidence; inspect damage, misses, healing, spell landing/fade messages and AA observations. An observed cast/landing is not a complete active buff roster. Do not use target names alone to identify a pet.
* Installed client spell files: the app reads validated spell formats and messages without modifying them. Preserve file SHA-256, modification date and parser version. Client parameters are computed reference evidence; server overrides and stacking exceptions still need confirmation. Changed or invalid formats are rejected.
* Official pages/wiki APIs: use canonical identities, redirects, page revisions and dates; refresh changed pages incrementally and retain sources. A related-quest link does not establish a reward. Missing new-content pages do not invalidate the official announcement. Reused source material is not independent confirmation.
* Screenshots/OCR: useful for exact inspect fields and pet totals absent from exports. Review each transcription. The app does not yet include an automatic screenshot/OCR capture window; submit paired images and the filled template for review.

## Review and promotion

Keep raw evidence, observations, computed differences, proposed rules and adopted models separate. Record alternative explanations, sample count and applicability. Promote a candidate formula only after repeated controlled tests and an independent holdout reconcile; retain any conflicting observations. Numeric predictions require their supported context, not just a plausible fit.

New content reference: https://www.everquestlegends.com/news/eql-nektropos-castle
