# Pet workspace

Pet edits are drafts. **Save**, beside **Undo**, commits the selected build and establishes a new baseline. **Undo** restores everything since that save, including items, ranks, Exaltations, Dual wield and summon assumptions. Both actions are disabled when the draft matches its baseline. Searching, browsing and temporary item previews do not change the draft.

Changing builds, leaving Pet, hiding the workspace or exiting with edits offers **Save / Discard / Cancel**. Theorycraft uses these same actions and still requires a name when first saved. A conflicting change to the saved build blocks Save and retains the draft for review; Undo reloads the saved version. Automatic detected summon changes remain separate from user edits.

**Tools** contains **Compare pets** and **Weights**. **Pet data** appears only in explicitly enabled local research builds. These tools retain their own Save actions. **Pet Details** opens the character sheet. Its Level control displays the matching reviewed actual level in Auto mode when available, otherwise the existing estimate. Its tooltip identifies the source. Endurance remains available in historical evidence but is omitted from the visible pet sheet.

Pet Details also offers **With standard buffs** and **Review buffs**. These are separate from gear-only **Added** values and do not change equipment ratings. See [Standard pet buffs](standard-pet-buffs.md) for learned choices, scoped rank estimates, cap hints and measured buff phases.

## Fixed positions and drops

Position 1 is Primary. Position 2 is Secondary with Dual wield enabled. Other positions retain armor and capacity rules. Existing unusual arrangements remain available for review. Manual placement describes user-assigned loadout equipment; it is not evidence that the client automatically equipped an item.

- Drop a candidate onto an occupied position to replace that item, or onto an empty position to fill it.
- Drop a Pet row onto another row to swap those two items. Item identity and rank travel with the item; Exaltations stay at their numbered positions.
- Drop a Pet row onto the candidate list to remove it from the draft. Its position's Exaltations remain configured.

Compatibility feedback appears before dropping. Rejected, canceled and same-position drops leave the draft unchanged. Retained locked, incompatible or conflicting Exaltations remain available for review and do not become socket-transfer evidence. These actions do not change player-inventory quantities or locations.

## Slot editor

**Edit pet slot N** edits the selected fixed position. Choose an Item and choose or type Rank 0–20; an existing recorded higher rank can remain unchanged. **Save** applies valid changes to the workspace draft. It is disabled for unchanged or invalid values and while required metadata loads. The workspace's Save is still required to commit the build.

Expanding Exaltations grows the compact editor vertically within the monitor's work area. Collapsing restores its compact height; a manually selected width is preserved. The footer remains accessible when content needs scrolling.

## Progress browsing

Checked **Tracked** shows tracked quests; unchecked shows all. **By item** and **By zone** are separate, mutually exclusive modes. Unchecking the active mode returns to Quest browsing. Each mode retains its own search, selection, tracking filter and expansion state during refreshes.

By item lists physically owned items with documented quest relationships. By zone lists normalized documented quest locations, including acquisition sources, quest starts and named destinations in steps. Zone mode starts with Tracked unchecked and selects the current zone if available. Search includes aliases and linked quest/item names. Each related quest has its own full tree, with connected branches initially revealed. Selecting a quest-specific action uses that node's quest; browsing does not change routes, tracking, Queue or completion.


Tracker and Combat Lab now attach to a shared 28-DIP workspace toolbar. See [toolbar help](workspace-toolbar.md) for switching, width-only collapsed resizing, reordering and the review-only pet artwork gallery.
