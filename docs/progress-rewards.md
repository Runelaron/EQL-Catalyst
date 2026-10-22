# Progress walkthrough and reward inspection

Progress presents each instruction once. Exact copies, normalized punctuation/whitespace copies and generated/truncated instruction headings share one presentation path with quest checklists and prerequisite details. Separate quest steps keep their identities even when they use the same words. Tracking, Queue, quantities, completion and prerequisites are unchanged.

## Browse by quest or inventory item

Progress's Search, Quest/Item selector and More controls share a 28-DIP height. Complete groups wrap together.

Enable **By item** to browse documented quest connections for physical items in bags, equipment, bank or shared bank. Copies and ranks share one item entry. Collection-only presence and planned items do not count as physical inventory. Hover an item for recorded ownership and uncertainty.

Item browsing starts with **All**. Each mode remembers its own search, Tracked/All filter and selection for this window. Search matches item names/aliases and related quest/reward names. Every related quest appears as a separate tree root, with the item's branches emphasized and initially revealed. An alternate-route note explains connections outside the selected route without changing it. Browsing never tracks, queues, equips or completes anything.

Select a root or step to set the context for Reward, Route, Open walkthrough and completion actions. Inventory exports, loot and repaired metadata refresh the choices. Quest navigation from elsewhere returns to quest mode.

## How to obtain

A documented drop uses a single route card: **Mob → Zone → Drop**. Duplicate source records share that card. Open **Evidence** for source revisions, availability and conflicting notes. Additional requirements such as raid status, mob level, coordinates and special conditions stay visible. Prose is suppressed only when an anchored, complete drop instruction is fully represented by that card. Other instructions, crafting and turn-ins remain intact. Unknown locations and actors remain explicitly undocumented.

## Step status and Reward

Step status and Reward share an equal-width row below the quest tree, capped at 96 DIP. Text wraps and long content scrolls within its half. Multiple reward alternatives remain separately selectable.

Select a reward name to inspect it over the right-hand walkthrough panel. Names are plain text toggles: the active name stays selected while opening/open, and selecting it again closes the view. Enter, Space and accessibility toggles are supported. Read-only facts use the same classic item card, type icons and ownership evidence as equipment inspection. Recorded physical copies can be selected; **Catalog +0** shows the base catalog item. Legacy recorded ranks above +10 remain visible. Browsing does not change inventory, equipment, ranks or ownership. This view has no rank-preview or rating controls.

Only the reward surface slides over the stationary walkthrough in the same clipped pane over 180 milliseconds. Covered walkthrough text is clipped underneath the reward, preventing text bleed through transparent surfaces. Closing slides the reward out to the right and reveals the walkthrough in place. Smooth motion and Windows animation preferences are respected. Disabled motion switches immediately. The tree, pane widths and footer stay in place.

**Back to walkthrough**, the close control or Escape dismisses the reward. Clicking elsewhere in the Tracker also dismisses it and performs the clicked action. Controls inside the reward remain usable; selecting another reward updates the open view. Escape returns focus to its reward action. The walkthrough keeps its selection and scroll position.

Selecting a different tree node closes the reward with animation and performs that selection. Tab, character and workspace changes close it immediately. An ordinary refresh of the same quest keeps it open and refreshes known facts. Shared catalog refreshes continue serving other views; stale reward contexts cannot replace the current selection. The open view and animation state are transient and are not saved in a profile.

## Progress actions

**Collapse all** sits above the quest tree, aligned with its right edge. Expand all remains in **More**. More also includes **Include optional steps**, a checked command for the selected quest. Existing saved route choices remain intact; recipe-specific routes stay beside their recipe. Source coverage is available in the quest's tooltip and **Quest evidence**.

**Queue** and **Open walkthrough** share one wrapping row beneath the detail title. Queue stays pressed for that exact pinned goal. Select it again to remove only the pin; tracking, completion and other queue entries stay intact. Its tooltip changes between adding and removing. Multi-reward roots do not expose an arbitrary Open item wiki button; select a specific component/reward first.

## Compact acquisition sources

Items with several drop sources use one card per zone with a **Mobs** selector. Purchase routes use **Vendors**. Selecting an actor shows its documented conditions and Evidence. Single actors remain plain text. Ground-spawn records with matching normalized coordinates share a location card; coordinates and pickup directions appear once, while landmarks and distinct conditions remain visible. Evidence retains the original records and source links. Separate locations and conflicting facts are not erased.

## Rolling inventory evidence

Loot updates physical stock between complete captures. Supported destruction/removal observations update stock when the matching rank is established. If several ranks could have been removed, the app retains the recorded copies and marks the uncertainty until a complete export. A persisted bounded event-ID ledger prevents duplicate application during restart/export replay. Complete captures replace stale estimates; ordinary unlogged transfers or destruction cannot be inferred.

The client's Quest flag is also used for tradeskill items. By item uses documented quest relationships, not that flag; Lava Rock is a deliberate exclusion when no quest relationship is documented. Capture schedules and opt-ins are unchanged.

## Window resizing

Transparent companion windows use a live corner grip. It fits requests to Windows' actual tracking limits converted from physical pixels to the current window's DIPs. It coalesces movement per rendered frame and commits the displayed dimensions on release. Escape/capture loss cancels; changing DPI discards old drag coordinates. Main windows keep their themed transparency and ordinary editor windows keep their existing surface policy.

Reward-name treatments **Raised Lettering**, **Inset Plaque** and **Engraved Text** are review examples only. Production reward styling remains unchanged pending selection.

## Compact controls in Beta 1.4

By item, Tracked and All sit beside the Quest/Item title above its dropdown. Collapse all toggles to Expand all immediately before More, including after manual branch changes. Optional steps remain in More. The update-ready icon appears immediately to the right of Settings after a verified download.
