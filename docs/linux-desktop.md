# Native Linux desktop development

The Linux frontend uses Avalonia with the same desktop controller, Core
calculations, SQLite persistence, log ingestion and evidence projections as
Windows. Windows retains WPF. Neither frontend writes client spell files or sends
game inputs. Linux data is separate under `$XDG_DATA_HOME/eql-catalyst`, or
`~/.local/share/eql-catalyst` when the variable is absent or relative.

Select the EQ directory within a Wine/Proton prefix, not its Windows drive name.
Game-file resolution handles case differences and rejects ambiguous names. Do
not open one live database from Windows and Linux simultaneously.

Build with the SDK pinned in `global.json`:

```
dotnet publish src/EqlTracker.Linux -c Release -r linux-x64 --self-contained true
python3 scripts/build-linux.py --payload PATH --output OUTPUT_DIRECTORY
```

The portable archive runs from a user-owned directory. Ubuntu's `.deb` adds a
desktop entry and launcher; removal must preserve XDG profiles. Steam Deck uses
the portable archive without changing the read-only system partition. Linux
updates must not invoke the Windows updater.

Sharing is off by default. Enabling it requires `secret-tool`, an available and
unlocked Secret Service, and an explicitly trusted active NetworkManager LAN.
Private keys and device credentials are encrypted on disk with a random key in
Secret Service. Missing secure storage fails closed. Existing TLS, pairing,
permissions and same-subnet enforcement continue to apply.

## Acceptance gates

Compilation and WSLg rendering are development evidence, not Ubuntu/Steam Deck
acceptance. Before supported publication, verify all editing workflows, Pet
sheet/appearance options, companion enrollment controls, drag/drop, chart and
comparison tools, shortcuts, tray behavior, independent selectors, persisted
bounds, accessibility and compositor restrictions. Current frontend coverage is
in progress; no parity claim is made.

Test real Ubuntu and Steam Deck Desktop Mode separately. Gaming Mode overlays
remain experimental. Compositor limitations must have specific explanations;
never silently pretend a requested overlay/shortcut is active.
