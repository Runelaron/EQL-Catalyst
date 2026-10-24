# Pet Details

Use **Pet Details**, beside Tools, to open summon settings and a compact character sheet without expanding the main workspace. The flat 28-DIP action resembles the former disclosure header, without a dropdown arrow. The window is nonmodal; opening it again focuses the same window. Close or Escape dismisses it. Changing workspace, character or Pet tab closes it; ordinary inventory refreshes retain it.

**Current** follows the last owned pet whose summon could be correlated with ownership. The log supplies the cast spell, not the merely selected spell gem. Failed or interrupted attempts do not change the confirmed context. Upgrade suffixes are resolved after matching the catalog spell: Monster Summoning II VIII is spell II at upgrade rank 8. Spell-required level, upgrade rank and actual pet level remain distinct. Auto level is an estimate.

Choose **Override build details** to edit Current assumptions, or **Use detected pet** to resume following. These controls do not change detected identity, DPS icons or historical encounters. Existing explicit identity overrides remain visible, and a conflicting family is never paired with another family's spell. Unknown summon metadata remains unknown.

New empty plans open Pet Details after naming. They begin with the current family and rank +0 as a planning assumption; choose the summon. Existing plans retain their own context. Creating a plan from Current also opens its copied context for editing. Valid edits update the workspace draft. Save commits; Undo restores the complete last-saved baseline. Theorycraft uses the same actions and requires a name when first saved. Activating a named plan preserves its selected context as a Current build override.

The identity header shows the pet icon, name or planned-build name, family and documented class pairing. **Observed level** comes from a matching capture, while **Spell rank** describes the selected summon. Automatic or manually assumed levels remain in summon settings and are never promoted to observed levels.

The **Observed snapshot** sheet groups Vitals, Combat and Regeneration alongside Attributes and **Resists (added)**. These sections stack at narrow widths. HP is red and Mana blue. Endurance and its regeneration remain in historical evidence rather than the visible sheet. Resources show current / maximum only when both were recorded; otherwise only the known maximum is shown. AC and Attack pairs retain the client's recorded order. One outer scrollbar serves the sheet, with Close fixed at the bottom.

**Added** starts unchecked each time the popup opens. It appends direct effective gear and active configured-effect contributions, for example `204 (+47)` or `Unknown (+22)`. Resource additions describe maximum capacity. Received buffs are excluded. Raw gear AC/ATK is labeled separately because its conversion to either displayed number is unresolved; haste is not converted into displayed attack speed. No STA-to-HP or INT/WIS-to-mana coefficient is assumed.

**Resists (added)** always shows signed effective gear subtotals: `+15`, `+0` or a negative value. Innate pet resists remain unknown. A coverage warning marks incomplete item metadata; a known subtotal is not a complete total. Any recorded total resists remain in Evidence.

**Loadout weapons** uses the effective Primary and Secondary equipment, recorded item rank and shared ranked-stat projection. It shows name, damage, delay, ratio, accuracy and weight. Exact supported observations override the adopted upgrade model. Inferred hand assignments and catalog base weight are labeled; empty hands show None, dormant Secondary is inactive, and shields do not acquire weapon damage. These are equipment facts, not additive bonuses to unknown innate pet damage. Without recorded fractional progress, values describe the rank's tier start.

**Known spells & abilities** separates documented capabilities from owned-pet observations, including Water's Backstab. Counts describe retained damage/healing records or cast attempts, with context in tooltips. An attempt does not establish a successful spell, and spell damage with unresolved proc origin stays labeled. Received buff evidence is historical landing/fade evidence for matching owned-pet intervals, not a complete current roster. Older skills/effects/absorption observations remain retained in research and Evidence rather than appearing as speculative sheet rows.

**Equipment contributions** stays separate below the sheet. Its compact summary and expandable groups use effective equipment only. Never add these contributions to an already equipped observed snapshot. **Evidence** contains summon provenance, automatic-level assumptions, full capture details and related snapshots.

An observed capture describes its labeled rank, actual level, time and conditions. It does not become a calculated total for a different setup. Matching stat profiles are available normally; local research sheets supplement them only in an enabled local build. Related captures remain labeled, and incompatible ranks or known equipment fingerprints do not supply current totals. Completed values remain visible during recalculation; obsolete results are rejected.

Resources, paired AC/Attack displays, all seven attributes, regeneration, all seven added resists and loadout weapons remain accessible. Blank evidence stays Unknown; negative HP is invalid. Gear contributions respect inactive slots, Dual wield and configured effects. Preference points never become predicted DPS.

Recovery of earlier summon metadata reads the retained log through the existing cancellable evidence pass. It does not replay combat ingestion or issue game input. Nullable metadata and follow preferences extend existing local records without replacing inventory or personal research.

The Tracker header keeps character, zone and inventory update information on the right, using a second line at narrow widths. Double-click noninteractive header space in Tracker or Combat Lab to collapse/restore the title bar. Buttons and selectors retain their actions, locked windows stay locked, and Windows taskbar minimization is unchanged. Collapsed Tracker reads exactly **EQL C - Tracker**. Objective overlay entries use compact class/NPC/item summaries; quantities, destination, alternate sources and full instructions stay in their tooltips.

See [Connected spell evidence](connected-spell-evidence.md) for spell identity, buff history and caster-specific focus restrictions in existing evidence views.

See [Pet workspace](pet-workspace.md) for the current Tools menu, draft workflow, fixed positions and slot editor.
