# Standard pet buffs

In Pet Details, **With standard buffs** shows a scenario with your learned sustained pet buffs assumed active. It starts unchecked whenever the popup opens. **Added** remains separate: its parentheses contain effective gear and configured effects, never received buffs.

The tracker learns from successful effects correlated with your cast and a confirmed owned-pet recipient. Interrupted casts, other players, unrelated pets and self-only group messages do not establish a pet buff. Frenzied Burnout stays in burst history and is excluded from the standard set.

Use **Review buffs** to exclude a spell or override its scenario rank. **Use learned rank** resumes following confirmed rank changes. Exclusions survive automatic updates. This setup is character-specific; it does not modify inventory, planned summon settings or equipment preference ratings.

## Reading the sheet

- The unchecked view prefers a matching unbuffed equipped capture. A related capture retains its original loadout and conditions.
- The checked view is labeled **Standard-buff scenario**. It is an assumption, not a complete live roster or an uptime measurement.
- Exact inspected effects and explicitly reviewed paired captures take priority. Original observations remain unchanged under Evidence.
- Without an applicable baseline, numerical effects or supported stacking, the sheet retains captured content and identifies what cannot be calculated.
- Raw spell AC differs from either displayed AC number. A measured conversion for one pet and capture is not a general rule.
- Gear is already present in an equipped capture. It is never added again, and innate resists remain unknown.

## Scoped rank estimates

For Burnout III and the inspected version of Verses of Victory only, uncaptured ranks 0–10 can use the locally adopted estimate:

`positive STR / AGI / spell AC = ceil(base × (1 + 0.02 × rank))`

`spell haste = base + rank percentage points`

Exact inspected fields override estimates individually. Above +10 and unknown ranks are retained without extrapolation. Spell-line numerals, upgrade rank and actual pet level are distinct. Instrument, focus and AA modifiers remain part of the evidence context.

These fits disagree with the fixed-magnitude description in [EQL Tools](https://eqltools.com/learn/spell-upgrades). They are not a universal spell rule. The [October 22 patch notes](https://www.everquestlegends.com/patch-notes/eql-update-notes-10-22-2026) do not supply STA→HP or INT/WIS→mana coefficients; none are introduced here.

## Caps and measured phases

Small inline cap hints are informational. **Capped / excess unknown** means the observed display reached its context-qualified ceiling without establishing the hidden uncapped total. **X over** requires a supported uncapped total. An ordinary-haste ceiling is not automatically an overhaste ceiling.

An excess contribution does not make an entire item obsolete. Other stats, effects and alternate builds still matter. This iteration does not change equipment ratings or remove items.

Enabled local research builds show phase DPS in Pet data and comparison evidence, separating ordinary Burnout, Frenzied, mixed and unresolved periods. Original damage and encounter duration are conserved. Targets, positioning and short phase durations can differ, so historical phase changes do not establish a causal buff multiplier.

Personal stat captures, log evidence and learned choices stay in the character's local data. Client files are read-only. No uploads or game-input automation are added.
