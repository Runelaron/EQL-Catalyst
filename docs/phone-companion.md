# Phone and browser sharing

Sharing is optional, read-only and disabled on a fresh profile. Open **Settings → Phone** on the PC to review configuration. Select a private LAN interface and port, then enable sharing explicitly. The desktop must remain running. Do not expose the service through a public router or reverse proxy.

Pair a device using the desktop's pairing flow, verify the displayed server identity, and approve the requested permissions on the PC. Access to one character does not grant access to another. Review devices and revoke access when no longer needed. Revocation closes active access; disabling sharing stops the listener. Sharing does not enable game input, inventory editing or quest completion from a phone.

Browser access additionally requires a locally trusted TLS certificate matching the configured hostname. Configure the hostname and certificate through Settings using an existing appropriate certificate. A missing, expired, untrusted or mismatched certificate is a setup failure: do not bypass browser certificate warnings or disable validation. Pairing credentials and host identity remain in private Windows-protected storage, outside catalogs and exports.

History is separate from live/session context. A historical selection stays fixed and does not change PC overlays. Connection loss, revoked permission, paused sharing, incomplete history and protocol mismatch are displayed explicitly. Reconnect or pair again through the normal flow when appropriate.

The Android protocol client remains supported in source and tests. Beta 1.7 publishes no Android APK. The release includes the Windows host and bundled browser interface; it does not enroll accounts, provision certificates or alter existing signing keys.
