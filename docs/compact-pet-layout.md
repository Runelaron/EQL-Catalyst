# Compact Pet layout and catalog coverage

Gear and Pet keep three horizontal panes. Pane minimums are 180 / 210 / 220 DIP, excluding the two 5-DIP handles. The workspace scrolls horizontally below its combined minimum; Item Details remains available. Filters scroll internally at short heights.

Pet uses Search (165 DIP, 120 narrow), Expansion (120), Slot (84), and Set Rank (68). Actual uses recorded ranks, including legacy values above +10, or catalog +0. Explicit 0–10 changes comparison previews without changing owned ranks. Conditional results use “Cond.” with full explanations in the tooltip and accessible name.

The two Pet tables receive compact defaults once. Panel proportions, sorting, selection and other tabs’ column widths are preserved. Reset columns restores the new defaults. Header resizing cascades through visible neighbors; window fitting preserves saved preferences. Type text contributes to preferred width, not its permanent minimum. Ellipses retain full-value tooltips.

## Compare pets

Cards lead with Damage preference, Defense preference, Gear HP, and separate changes versus Current. These are computed from the shared effective-equipment projection and your editable weights. Gear HP excludes innate pet HP and buffs. Conditional equipment and partial coverage remain visible. Identical compatible equipment can produce identical preference values across pet families: these numbers do not measure innate pet performance.

Measured DPS and total HP remain secondary and require matching context. Unsupported absolute predictions remain unavailable. Benchmark setup and unmatched historical measurements are retained in advanced details. Preference points never become damage predictions.

## Offline catalog repairs

Parser version 9 reads mixed-case slot names, normalizes exact slot/category tokens, and separates effect activation requirements from item wearing requirements. Source fixtures in `data/catalog-slot-repairs.json` retain page text and revision IDs under the existing EQLWiki CC BY-SA 4.0 attribution.

Eleven verified corrections: Aegis of Life, Black Silk Cape, Blade of Abrogation, Blazing Wand, Leatherfoot Raider Skullcap, Midnight Mallet, Mystic Cloak, Orb of Mastery, SoulFire, Spiroc Wingblade and Weeping Wand. Spiroc Wingblade is Primary/Secondary; level 45 applies to Serpent Sight activation, not wearing the weapon.

Stained Parchment Top and Bottom document Class/Race NONE and no equipment slots; their AC does not make them wearable. Blood Soaked Plasmatic Priest Robe documents AC and classes but no slot/category that establishes a wearable position. That slot remains unverified.

The local audit covers 6,808 definitions and 18,872 indexed titles. 6,768 titles match a definition or alias; 12,104 await a materialized definition. This is coverage accounting, not a claim that every indexed title is wearable. Owned, visible and incomplete records receive priority in the existing resumable background refresh. Unchanged revisions are reparsed after a parser-version change. Non-equipment results are now cached rather than repeatedly discarded.

## Expanders

Arrow and label share one brush. Theme Accent, Antique Gold and Bright Parchment are review-only alternatives; production retains the existing helper-text prominence until a treatment is selected.

## Quest detail repairs

A truncated copy of a full instruction no longer becomes an extra heading. Distinct headings and canonical data stay intact. Acquisition cards group the same zone/mob/method while preserving each source’s evidence in the tooltip.
