# Third-party notices

## EverQuest Legends Wiki

Bundled catalog, quest, item and spell text is derived from [EverQuest Legends Wiki](https://eqlwiki.com/), its contributors, and their attributed upstream sources. The wiki documents [CC BY-SA 4.0](https://eqlwiki.com/EQLWiki%3AAbout) for original text unless otherwise noted. This data remains under [Creative Commons Attribution–ShareAlike 4.0](https://creativecommons.org/licenses/by-sa/4.0/); it is not relicensed as application code. Parsing, normalization, classification and checklist curation are modifications. Source page URLs, revision IDs and evidence notes are retained in the bundled JSON. Follow those records for page history and per-page exceptions. Credit also belongs to Project 1999 Wiki contributors where acknowledged by the source wiki. Game trademarks and game assets retain their owners' rights.

## Font

Cinzel Decorative by Natanael Gama is distributed under SIL Open Font License 1.1. Its original copyright, reserved font name and complete terms are retained in `src/EqlTracker.App/Assets/Fonts/OFL.txt` and the installed `Assets/Fonts/OFL.txt`.

## Runtime and database

.NET and Microsoft.Data.Sqlite use their upstream licenses/notices; SQLite is public-domain software and SQLitePCLRaw uses its own upstream license. The self-contained package includes upstream runtime and dependency notices under `licenses/`. The SBOM records the distributed versions. The application GPL does not replace dependency licenses.

## Client spell format reference

The independently implemented read-only Pet spell adapter follows the format research documented in [Amerzel/eql-info, SPELL_FORMAT.md](https://github.com/Amerzel/eql-info/blob/main/SPELL_FORMAT.md). That repository's code is MIT-licensed; its license does not cover game data. No raw game files, extracted spell database or upstream implementation code is distributed in this application. Stacking-model background is attributed to [EQL Tools](https://eqltools.com/sources); computed rules remain distinct from verified server behavior.

## Installer tooling

Built using Inno Setup 7.1.0, maintained by JRSoftware/Pyrsys. The compiler is a build tool, not an application dependency. Its distribution remains governed by its upstream license. No Authenticode signature is applied to the EQL Catalyst installer.
