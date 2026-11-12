# RigMD 0.2.17 — restrained startup presentation

Replaces the oversized splash card, slogans, floating logo and pulsing dots with the official 64px logo in a subtle circuit-board illustration, a compact RigMD wordmark, Windows diagnostic support subtitle, and one indeterminate activity line.

The native desktop, pre-JavaScript HTML and React startup screens use the same artwork, dimensions, palette and copy. The native animation honors the Windows client animation preference; HTML and React honor prefers-reduced-motion. A reduced-motion splash keeps a stationary activity segment. Animation stops when the native splash is dismissed or the window closes.

This is a presentation change. Readiness events, startup measurement boundaries, data providers, diagnostic rules, saved checks, feedback and recheck history remain unchanged. No artificial delay or progress percentage is introduced. The dependency graph is unchanged.

Validation evidence accompanies the frozen release candidate. Headless browser fixtures and offscreen native rendering do not replace installed acceptance. The user's 0.2.16 screenshot shows 3.22 seconds for the latest launch, with only 1 of 2 launches within target; that is not proof of consistent startup compliance for either release.
