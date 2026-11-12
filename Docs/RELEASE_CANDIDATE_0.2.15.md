# RigMD 0.2.15: startup and feedback correction

## Reported defects

The installed 0.2.14 window could show a white web surface before the React splash was ready. A saved-check page could accumulate repeated Your experience forms after a failed recheck.

The duplicate-form defect was reproduced on the frozen 0.2.14 frontend with synthetic data: four failed rechecks increased the feedback heading count from one to five. The feedback and recheck-history components had the same sibling key. When the conditional error banner changed the sibling layout, React reconciliation could leave an old form behind. Distinct component/session keys fix the identity collision; feedback is not hidden or deleted.

## Changes

- A native dark splash appears while WebView and the local service initialize.
- The matching HTML boot card appears before the application JavaScript executes.
- The React splash has a friendly monitor illustration, gentle decorative motion and a reduced-motion alternative.
- WPF and WebView compositor backgrounds are set before initialization to match the page, rather than starting with the default white surface.
- The splash exits when Home essentials finish or fail, with no artificial minimum duration, fake percentage or completed diagnostic steps.
- Existing launch-to-Home measurement includes the splash exit. A failed startup remains incomplete; the five-second target is unchanged.
- Feedback and recheck history have independent, session-specific component keys. Unsaved feedback survives result updates; switching saved checks resets the form to that check's saved feedback.

## Verification and reproduction

Frontend unit suite, TypeScript, ESLint, production build, Desktop Release build and full backend suite were run. Browser verification includes the prior 17 scenarios and 9 focused startup/feedback scenarios. Synthetic checks do not modify the installed application or real user records and do not constitute installed acceptance.

The standalone focused browser regression is frontend/tests/browser/startup-feedback.mjs. Run it from the frontend directory with a current production frontend build and an installed Playwright module:

    $env:RIGMD_UI_BUILD = 'ABSOLUTE_PATH_TO_BUILT_FRONTEND'
    $env:RIGMD_UI_EVIDENCE = 'ABSOLUTE_PATH_TO_NEW_EVIDENCE_FOLDER'
    $env:RIGMD_PLAYWRIGHT_MODULE = 'ABSOLUTE_PATH_TO_PLAYWRIGHT_INDEX_MJS'
    node .\tests\browser\startup-feedback.mjs

If Playwright resolves as a package in the test environment, RIGMD_PLAYWRIGHT_MODULE may be omitted. It is a test dependency, not an application runtime dependency; this fix adds no package.

Focused cases: boot HTML before JavaScript; reduced motion and pending Home; readiness after splash exit; repeated failed rechecks with a retained note and exactly one form; successful rechecks after errors; a single feedback POST; isolation and reload across saved checks; normal animation/mobile layout; incomplete startup on Home failure.

## Remaining installed acceptance

Install this candidate separately from the preserved 0.2.14 candidate. Verify the initial native splash, handoff to Home, repeated rechecks, feedback persistence and measured startup on the actual PC. Existing files/databases are not rebuilt or cleared by this fix. The installer remains unsigned. No installation, commit, push or public deployment was performed.

Early compositor background behavior follows the WebView2 controller-options API:
https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controlleroptions.defaultbackgroundcolor
