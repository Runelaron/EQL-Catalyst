# DPS title selector

Click the icon beside the DPS panel title to choose **Pet**, **Self**, **Combined**, or **Group** directly. The selected choice is checked in the dropdown; the title and icon update immediately. This remains available at the minimum panel size, while collapsed, and in Minimal, Frost and Luminous appearances. The footer selector and existing menus remain available.

Tab to the title selector, then use Space, Enter, Down, Alt+Down or F4 to open it. Arrow keys navigate the choices, Enter selects, and Escape closes the menu. It uses the existing overlay menu sizing, focus, repeated-click and dismissal behavior. Click-through mode retains its existing restrictions; disable click-through to interact with the panel.

The title icon controls the main panel; the footer icon controls its secondary numeric readout independently. Both offer Pet, Self, Combined and Group. For example, keep Group in the title and Pet in the footer. Changing either choice leaves the other choice and Combat Lab unchanged. Each choice is saved separately and restored after reopening/restarting. Existing profiles start both choices at their previously selected display, then retain each choice independently.

Both readouts use this panel's selected fight/session and filters. Personal rates share the same encounter-duration denominator. Group data is prepared in background whenever either readout selects Group; unavailable group coverage remains unavailable. Damage calculations and group membership/ownership evidence are unchanged.

## Group artwork

The Vibrant (detailed) style now includes a painted party emblem rather than falling back to the flat Clean glyph. It is shared by title, footer and menu Group icons. Clean keeps its existing glyph.

Production asset: `src/EqlTracker.App/Assets/Painted/actor-group-256.png` (256 × 256 RGBA). Created with the built-in imagegen tool using the existing Self and Combined icons as style references. Only resampling and metadata-free production export followed generation. Attribution and hash are retained in the asset inventory and painted-role manifest; no private reference/session path is included.

Generation prompt: Create one compact fantasy RPG party emblem matching the painted metallic/gemstone application icons. Three readable adventurer heads/bust tops: a silver-and-gold armored helmet forward, blue hooded spellcaster and green/brown ranger behind. Use dimensional hand-painted shading, gold trim, clear separation and a strong small-size silhouette. No flat person symbols, lettering, numbers, frame, UI or backdrop. Center the square composition on a truly transparent background with clean alpha edges.
