# Painted pet icons at every size

Vibrant now uses the selected original painting at every icon size, including 13, 14, 16, 20 and 24 DIP. There is no small-icon switch to simplified artwork. The shared renderer applies this consistently to DPS selectors and menu choices, summaries, comparisons, Settings previews and Pet Details.

Clean retains the simplified vectors as an independent appearance style. Family choices, Classic alternatives, Monster A and the optional Dragon/Aviak appearances remain unchanged. Rendering still decodes the original painting for the display’s pixel density and reuses bounded cache entries. Missing artwork retains its existing fallback.

Open `previews/painted-pet-icons-review.html` for the native production family board. The earlier side-by-side painting/vector comparison remains available at `previews/painted-small-review.html`; the painted treatment on its right is now used by Vibrant at all sizes. Approved source paintings were not regenerated or altered.

Focused verification covers exact rendered pixels against direct paintings for all 19 approved family/appearance roles at seven sizes and four rendering scales, plus Clean-vector equivalence, decode resolution and cache reuse. Existing native artwork checks cover saved choices, restart, character switching, generic fallback and independent DPS selectors. Complete core and companion/security checks, targeted privacy review, package exclusions and local backup/data-retention checks remain part of delivery. Physical multi-monitor/live-game acceptance is separate from synthetic rendering.
