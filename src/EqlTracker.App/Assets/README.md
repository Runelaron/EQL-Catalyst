# EQL Catalyst icon

Blue Orb remains the default executable identity. Settings → Choose app icon offers Blue Orb, Gold Elements and 16 original class-emblem orbs. The choice is manual and saved per character; tray and open/new companion windows switch immediately. The tooltip remains EQL Catalyst. Launcher/Explorer icons remain blue.

`catalyst-orb-master.png`, `gold-elements-master.png`, and `class-*-master.png` are transparent production masters. Class artwork used the built-in imagegen tool with the blue orb as a style reference. Motifs, palette descriptions, source files and prompt specification are recorded in `scripts/class-icon-generation.json`; these are original emblems rather than copied game icon pixels.

Run `scripts/render-orb-brand.py` with Python and Pillow, or its `render-brand.ps1` wrapper, to regenerate transparent PNGs, the nine-frame Windows ICO (16, 20, 24, 32, 40, 48, 64, 128, 256), and light/dark taskbar proofs. Small variants discard faint outer halo pixels. `catalyst.svg` is the retired gold-hands prototype and is retained only as design history; the renderer does not consume it.

The renderer wrapper also runs `scripts/package-class-icons.py` for all 18 icon families. Existing project masters allow offline rebuilding; original generation paths are optional provenance. Each family has nine PNG/ICO frames. Small frames remove faint halo pixels and use restrained sharpening; the contact sheet verifies the silhouettes on light/dark backgrounds. The app caches frozen WPF image resources and owns/disposes tray icon replacements. Display/DPI changes refresh the tray size. Executable and profile directory names remain EQLTracker for compatibility.

Image prompts: blue master — preserve the supplied circular three-arm cyan/deep-blue orb and white center, remove the dark backdrop to actual alpha, simplify contrast for 16 pixels, no hands/text. Alternate — preserve the gold ring/four-arm silhouette and repaint Earth #CFA779, Water #64BDEE, Air #DCECEF, Fire #EC8C89 with restrained EQ-gold highlights, transparent exterior. Both edits used the built-in tool; packaging/resizing uses Pillow.
