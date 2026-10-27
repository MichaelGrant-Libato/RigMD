# Website account connection for RigMD Desktop

RigMD Desktop connects the existing verified website account. There is no separate AI account or desktop password form.

## User flow

1. In Desktop, choose **Connect website account** on Home or in Settings.
2. The default browser opens the RigMD website. Its existing login is used; passwords are not requested again when that browser already has a verified session.
3. Compare the displayed code with Desktop, then choose **Connect this PC** once.
4. Return to Desktop. The account email is shown and the connection is remembered for this Windows user.
5. Later launches restore and refresh the connection automatically. Gemini still follows **Use Gemini when available** / **Local-only mode**, server usage limits, and availability.
6. **Disconnect this PC** removes the local remembered credential and revokes the desktop session using local scope. It does not delete diagnostic history.

Browser profiles and the `rig-md.com` / `www.rig-md.com` website origins have separate browser sessions. The connection page offers the other official website address if needed. An expired or absent website session uses the normal website login. The desktop cannot read another browser's cookies or infer an account from a PC name.

## Handoff and storage

- An authenticated local API request creates an ephemeral RSA-3072 key and random 256-bit state. The browser receives only the public key and state, in a URL fragment.
- The website shows the authenticated account and matching code before connecting. It rechecks the selected identity at approval time.
- `/api/desktop-connect` verifies the website bearer credential with Supabase, requires a verified non-anonymous account, and consumes the existing per-user/global cloud request quota before issuing a connection. One connection consumes one cloud request permit.
- The server creates a fresh session for that same verified user using an admin-generated magic link and server-side verification. It does not copy, refresh, or log the website's refresh credential. The admin-generated and verified identities must both match the original verified user.
- The refresh credential is sealed with a random AES-256-GCM key, state as authenticated data, and RSA-OAEP SHA-256 wrapping. Neither plaintext tokens nor the Gemini/provider/admin key are returned to the browser.
- A top-level form POST sends the encrypted envelope to the fixed `http://127.0.0.1:5273/desktop-account-connect` callback. The connection page uses an origin-only referrer policy so browsers retain the website Origin on HTTPS-to-loopback form navigation. No URL path, state, or credentials are sent as a referrer.
- Only this callback permits website form navigation. It requires loopback, the exact loopback Host, and an exact official website Origin, plus a matching unexpired pending state, local private-key decryption, authenticated envelope and verified account refresh. Ordinary local `/api` and hub authentication is unchanged.
- The pending request lasts two minutes, is consumed before token exchange, and rejects replay, tampering, expired envelopes, canceled/replaced requests and substituted accounts. No CORS permission is granted to read the local callback from the website.
- Only the desktop refresh credential, project and user identity are persisted under `%LOCALAPPDATA%/RigMD/cloud-account.bin`, encrypted with Windows DPAPI CurrentUser and application-specific entropy. Passwords, access tokens and provider keys are not persisted there. The file is not packaged in the installer or stored with diagnostic history.
- Restoration revalidates the trusted project's public configuration, checks identity, rotates the refresh credential, and backs off during an outage. Revoked or substituted credentials are discarded. A failure to persist securely does not claim a remembered connection.
- Closing/reopening the app does not require another website approval while the remembered session remains valid. Server revocation, a different Windows account, or disconnecting may require reconnecting.

## Deployment order

1. Deploy the website first, including `/desktop-connect` and `/api/desktop-connect`.
2. Keep the existing trusted Supabase public configuration, server-only admin/service key, and cloud quota RPC configured. The server key must support Supabase Auth admin link generation. Email authentication must support magic-link verification for existing verified users. No provider key belongs in Vite variables or the installer.
3. After automated checks, pilot the deployed website with a private rebuilt desktop before publishing the installer. Generic preview hostnames are intentionally rejected; a separate staging environment needs an explicitly trusted origin configured on both sides. If the project enforces one active session per user, adjust that product policy before promising simultaneous website and desktop sessions.
4. Build and test the new desktop installer, then release it. Old installers do not acquire this feature from a website deployment alone.

No database migration or removal of cloud authentication is part of this change. Never bundle a shared user's session into the installer.

## Release acceptance

- Already-signed-in website: one code confirmation, no password entry in Desktop, correct account email in Desktop.
- Website signed out: normal website login returns to the pending connect page without losing its fragment; expired requests require a fresh Desktop connection.
- Restart: account restores automatically without reopening the website. Confirm rotation also survives a second restart.
- Desktop disconnect: stored credential is removed; reopening remains disconnected; website stays signed in when multi-session support is enabled.
- Website account change during approval: show the changed identity and require review before connecting.
- Cancel/expiry/tampering/replay/foreign origin: no new account connection. A cancellation that arrives after successful completion reports the actual connected state; disconnect remains explicit.
- Offline startup or failed cloud configuration: local diagnostics still work; no false Gemini attribution.
- Local-only mode: connecting does not enable Gemini or execute remediation.
- New diagnostic check after connection: inspect the saved **Explanation source**. Only `Gemini` confirms a real provider-generated explanation. A connected account alone does not prove provider success.

## Local verification

Automated server tests use synthetic Auth/quota responses, not real accounts or provider quota. Backend tests exercise identity checks, callback crypto, replay/expiry/cancel cases, credential rotation, revocation, outage recovery and real Windows DPAPI file round-tripping. Browser tests cover the existing website session, signed-out return route, actual HTTPS-origin form POST, and Desktop connection/reload at desktop and mobile widths.

A real website deployment, real account connection, installed-app restart and real Gemini response still require the release acceptance checks above. The previous uncommitted recommended-next-steps changes and existing generated `wwwroot` assets are separate; exclude generated assets from the source commit.
