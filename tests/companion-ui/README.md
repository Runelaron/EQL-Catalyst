# Browser component checks

First export the synthetic projection with the Windows app's `--preview test-results/integrated-companions --integrated-companions-only` entry point. From the repository root, run `npm install --prefix tests/companion-ui`, then `npm test --prefix tests/companion-ui`.

The checks use installed Microsoft Edge by default. `CATALYST_BROWSER_CHANNEL` can select another installed Playwright browser channel. They load the bundled browser components with synthetic responses, generate actual screenshots, and check layout, history separation, sharing pauses and protocol mismatch. They start no network listener and use no certificate-validation bypass. Actual HTTPS/WSS security tests are in the .NET regression project.
