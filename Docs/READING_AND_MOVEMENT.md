# Reading comfort and focused Settings

These controls support the readable desktop UI objective (NFR-20 in the existing MVP acceptance status). They address reading comfort and access to guidance; their implementation alone does not demonstrate diagnostic accuracy, repair effectiveness, or improved stakeholder comprehension.

- **Reading size:** Standard, Larger, Largest use 100%, 112.5%, and 125% of the browser's default root text size. Text and controls sized in rem grow together. Fixed-size content, including the report paper, retains its own layout; desktop zoom remains available.
- **Animations:** Follow Windows is the default. Reduce animations skips Motion animations and shortens CSS animations/transitions throughout the desktop web UI, including portal dialogs. A Windows reduced-motion preference is always respected. Native Windows dialogs and the native exit window retain their separate behavior.
- Preferences apply immediately and are saved in this Windows user's desktop WebView profile under `rigmd.display-preferences.v1`. No account, scan readings, or feedback are stored in this preference. Storage failure applies the choice for the session and shows a message. Restore defaults changes only this preference.
- The public download/login website does not activate the desktop preference provider. PDF and text export builders and report data are unchanged. Print resets root text size so app reading size does not enlarge the report.
- **App speed** opens its own screen from Settings, loads existing timing observations, and returns to Settings with keyboard focus on its entry. Settings remains selected in the sidebar. Technical details remain expandable within App speed. No JSON download is exposed in this user flow.

## Acceptance checks

1. Select each reading size; check result summaries, guidance, findings dialogs, Settings, and navigation at standard desktop resolution and a narrow window. Ensure text wraps and controls remain reachable.
2. Select Reduce animations, reopen RigMD, and verify it persists. Test Follow Windows with Windows animations disabled as well.
3. Use Tab, arrow keys, Space, and Enter to change radio choices, open App speed, refresh observations, and return to Settings. Confirm visible focus.
4. Open and export the same saved report before/after changing reading size. Verify original readings, dates, filenames, and document layout remain consistent.
5. Restore defaults. Reopen the app and verify account connection and saved checks remain available.

Record observations from the actual installed release and ask representative users to find and explain a recommended next step without coaching. Keep those results separate from developer checks and app speed measurements.
