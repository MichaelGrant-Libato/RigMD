# RigMD 0.2.16: official animated logo and findings selection

## Branding

The splash now uses the existing RigMD pulse-logo artwork from Assets/rigmd-logo.ico, which is byte-identical to the installer and browser favicon icons. Its existing 256-by-256 frame was exported losslessly as PNG without redrawing, resizing or recoloring. The same PNG is used in the native WPF splash, boot HTML and React splash.

The native splash has a gentle vertical float and glow, gated by Windows client-area animation settings. Its animation is removed and its animated properties reset when the interface replaces it or the window closes. The web splash respects reduced-motion preferences. Loading remains tied to actual readiness with no minimum animation duration or fake percentage.

The native logo is embedded as a WPF resource; it is not loaded from the internet or a working-directory-dependent path.

## Findings selection

When a recheck cleared the warning, the previous Needs attention tab could remain selected with zero rows even though three other readings were available. The view now chooses a populated group when evidence changes and the active group becomes empty. A deliberately selected empty tab remains inspectable, keyboard navigation remains available, and a still-populated user selection is retained. This changes presentation only, not diagnostic statuses, thresholds, stored readings or original evidence.

## Screenshot/data review: 12 November 2026

- The supplied entries show 84.9% and 83.2% physical-memory use with warnings, followed by 78.4% without that warning. The configured RigMD memory review threshold is 82%. The transitions agree with the current check rules.
- Storage readings of 53.5-53.8% used are below the configured 80% storage-capacity review threshold. Capacity use is separate from drive health.
- GB totals are rounded separately from percentages; a percentage need not exactly equal the quotient of the displayed one-decimal GB values.
- Browser process working-set totals cover a different measure and scope from overall physical-memory use. They do not establish that browser activity caused the observed slowdown, and changes do not establish a repair.
- No warning reported for drive health is information about the reported device status, not a comprehensive health test.
- The history contains separately timestamped older and newer observations. The supplied screenshots support retained history and visible status changes, but do not independently verify sensor accuracy, every saved record, restart persistence of the latest entries, or startup speed.
- The screenshot's empty Needs attention group was a tab-selection issue; the three readings were still under Other readings. This presentation defect is corrected here.

## Verification

The frontend suite includes five new selection regressions. The focused browser runner covers logo decoding, motion and reduced motion, readiness timing, repeated failed/successful rechecks, feedback isolation and saving, cleared/unknown findings, deliberate empty-tab selection and keyboard navigation. Native verification checks the embedded logo bytes, offscreen rendering, storyboard advancement and animation cleanup. Existing broad browser, Release build, backend, type and lint checks remain applicable.

These are synthetic/offscreen tests, not a claim of installed acceptance. Preserve the previous candidate. Verify 0.2.16 on the real installed app before deployment. No commit, installation, database edit or public deployment is performed by these changes.

Windows animation preference API:
https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.clientareaanimation?view=windowsdesktop-10.0
