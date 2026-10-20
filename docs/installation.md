# Install, update and remove

1. Download **EQL-Catalyst-1.1.0-beta.1-win-x64-setup.exe** from the [Beta 1.1 prerelease](https://github.com/Runelaron/EQL-Catalyst/releases/tag/v1.1.0-beta.1).
2. Compare its SHA-256 with the release checksum file (`Get-FileHash -Algorithm SHA256`). This build is unsigned; Windows warnings or policy blocks are possible. Do not disable security protections.
3. Choose **Install for me** for `%LOCALAPPDATA%\Programs\EQL Catalyst`, or **Install for all users** for native `Program Files\EQL Catalyst` with elevation. Start Menu access is included; the desktop shortcut is optional.
4. On first launch choose the game folder containing `eqgame.exe` and a character/server. Discovery uses filenames; blank game folder supports offline startup. The last successfully opened profile is remembered.
5. Enable game logging with `/log on` and create a complete inventory capture with `/outputfile inventory`. Optional native controls and automatic capture require explicit setup and successful shortcut verification. Installation creates no game hotkeys or capture opt-ins.

Profiles, inventories, saved builds, sessions and caches are stored under `%LOCALAPPDATA%\EQLTracker`, independently for each Windows user. A complete game export can refresh Current build when **Auto Update** is enabled; loot alone does not establish worn equipment.

Before updating, close the application from its tray menu and resolve any unsaved Theorycraft prompt. Setup refuses running/locked files and never force-terminates the app. Reinstall repairs missing application files. Downgrades and conflicting installation scopes are refused; uninstall first to change scope.

Uninstall removes installed application files and shortcuts while preserving character data and game files. Reinstall discovers existing profiles. For portable use, extract the entire ZIP into a writable application folder; profiles still use LocalAppData. Never install over the game or profile directory.

## Troubleshooting

- **Offline / log missing:** confirm folder and character/server, `/log on`, and a fresh complete inventory export. Offline records remain usable.
- **Unknown facts or rating unavailable:** supply evidence or an explicit correction. Unknowns are not guessed from item names or current ownership.
- **Capture unavailable:** native sync is optional. Review and verify the shortcut before enabling scheduled capture; the default interval is two hours once enabled.
- **Windows blocks the app:** respect local policy. No paid signing service or bypass is supplied.
- **First launch cancelled:** no new profile is created. Run the app again when ready.
