# RigMD 0.2.14 candidate

This is a local candidate, not a declaration that installation, public deployment, or final user validation has passed.

## Recheck evidence

Every new recheck is saved as an individual immutable `resolution_observation_v1` answer row in the existing SQLite database. Original diagnostic evidence remains separate. The latest-resolution fields are retained for existing screens and authorization decisions. The history insert and latest update share a transaction; an older finishing recheck cannot replace a newer latest observation. Independent writers are serialized by the database transaction. Read-only list/recurrence projections omit observation payloads; the saved-check detail exposes the complete history with bounded initial rendering and an explicit show-more control.

The latest surviving follow-up from an older build is preserved once before its fields change, including before an action marks the check as needing another recheck. Earlier overwritten observations cannot be recovered. A saved pre-action snapshot can preserve its known readings and timestamp without inventing a missing outcome. The UI identifies legacy limitations and unreadable entries. Original results, unknown readings, and action receipts do not become verified repairs.

## Startup

- Full pages, reports, history charts, and diagnostic workflows load on demand. A page-load error is contained with a route back to Home.
- WebView preparation overlaps the API startup. Navigation waits for local HTML/CSS readiness and the per-launch credential protections remain in place.
- Recovery queries for Windows restart identity run in the background. The execution gate is blocked before the listener starts and remains blocked until recovery succeeds. Unknown restart identity, interrupted operations, and journal errors retain their blocks. Read-only Home does not wait for WMI recovery. The recovery banner polls while blocked and clears after recovery.
- Schema startup converts the old unique remediation index only when needed, instead of dropping and recreating the index on every launch.
- The startup clock still covers process start through Home essential readings and splash dismissal. Showing the window earlier does not complete the timing. Phase durations can overlap. The five-second target is unchanged and requires installed measurements; smaller bundles and unit tests alone do not establish a pass.

## Release provenance

Desktop, API, frontend package/lockfile, release policy, and installer identify 0.2.14. Build the installer from the intended reviewed source. It generates current frontend assets, isolated published inputs, installer SHA256, publisher-signature status, source revision/dirty status, and a content fingerprint with source-file hashes, including untracked source. A change to source during publishing aborts before compilation of the installer. This fingerprint does not replace source review or a future source commit.

The release validator includes all backend tests by default. `-IncludeNativeJunctionTest` remains accepted for compatibility; `-SkipNativeJunctionTest` is an explicit documented exclusion, and it cannot be combined with inclusion. Installed acceptance remains recorded as incomplete until separately executed.

## Installed acceptance still required

Retain the previous installer and database backup. Test clean installation, upgrade/history preservation, repeated rechecks across restart, unavailable readings, the recovery gate, normal and canceled Windows actions on disposable test material, report preview/PDF, and repeated end-to-end startup. Complete deployment sign-in, verification, authorized download, and downloaded-artifact checks on the intended site. Do not replay an uncertain action. No automated test or source change supplies participant validation results or adviser approval of changed criteria.

No commit, deployment, installation, certificate purchase, or real cleanup/uninstall is performed by these source changes.
