# EQL Quest Display

EQL Quest Display Regular reconstructs the supplied lettering as scalable TrueType outlines. All 52 letters come from the separated `eqfont_letters.png` strip; digits and punctuation come from four original strips. Reward-name buttons use it by default. Optional display typography is available under **Settings → Appearance → Heading style**; **Classic (Default)** preserves the established Cinzel/Georgia treatment. The display choice is saved independently per character and updates decorative headings and its live sample immediately; Catalyst wordmarks remain Classic. Ordinary UI text and page captions keep their existing fonts. It does not require installing a system font.

- Desktop font: `src/EqlTracker.App/Assets/Fonts/EqlQuestDisplay-Regular.ttf`
- Web font: `src/EqlTracker.App/Assets/Fonts/EqlQuestDisplay-Regular.woff2`
- Family: **EQL Quest Display**, weight **400 / Regular**
- Coverage: all **95 printable ASCII characters**, plus 11 typographic/spacing characters.
- Source images: `design/font-sources/eq-display/`, preserved byte for byte.
- Source/output SHA-256 values, glyph assignments, source baselines and kerning pairs: `src/EqlTracker.App/Assets/Fonts/EqlQuestDisplay.manifest.json`.

The images supply upper/lowercase letters, digits and 27 punctuation marks. Five missing ASCII characters (`"`, `#`, `\`, grave accent and `|`) are constructed from related strokes. Curly quotes, dashes, ellipsis, nonbreaking space and minus sign are derivatives, not additional observed glyphs. Blank columns separate the 52 source letters; detached dots and punctuation islands remain with their glyph. Raster tracing preserves the supplied shapes but cannot recover the original font's exact vector curves, hinting or spacing. This is a display face; body text stays in the existing readable UI font.

WPF can reuse `CatalystHeading.DisplayFont`; its pack resource resolves the embedded TTF with Georgia as a fallback. The font has a single regular master; custom-font runs explicitly use Regular to preserve the supplied strokes. Branding resources always select Classic; the optional display resource is separate. Reward controls reference the custom face directly and remain independent of heading selection.

```css
@font-face {
  font-family: "EQL Quest Display";
  src: url("EqlQuestDisplay-Regular.woff2") format("woff2");
  font-style: normal;
  font-weight: 400;
  font-display: swap;
}
.reward-name { font-family: "EQL Quest Display", Georgia, serif; }
```

Rebuild with Python 3.12 and the versions in `scripts/eq-font-requirements.txt`:

```text
python scripts/build-eq-font.py --proof-dir <folder-outside-the-checkout>
```

The native preview option `--preview <folder> --offline --eq-font-only` checks pack-font resolution, character maps, glyph outlines, separate Classic branded runs, title/context separation and compact sizing. It captures actual WPF headings across the three themes, normal/minimum short windows and 100/150/200/300% synthetic scales. Synthetic rendering is distinct from physical monitor/game acceptance.

The supplied designs retain their original owners' rights; see `Assets/Fonts/EqlQuestDisplay-NOTICE.txt`. This conversion does not assert a new license for the source lettering and is not an official EverQuest font.

## Beta 1.8 licensing and branding
Version 1.001 updates metadata only. Contributor rights in the locally generated font software use SIL OFL 1.1; original reference hashes remain recorded. Third-party rights are not assigned or relicensed. Catalyst wordmarks remain Classic in every Heading style; reward lettering and optional decorative headings remain independent. See the bundled notice and EqlQuestDisplay-OFL.txt.
