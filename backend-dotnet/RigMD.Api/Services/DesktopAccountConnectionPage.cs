using System.Security.Cryptography;
using System.Text;

namespace RigMD.Api.Services;

internal static class DesktopAccountConnectionPage
{
    private const string StyleSource = """
        :root { color-scheme: dark; font-family: "Segoe UI", system-ui, sans-serif; background: #0b1219; color: #f3f7fb; }
        * { box-sizing: border-box; }
        body { margin: 0; min-height: 100vh; min-height: 100svh; display: grid; place-items: center; padding: 40px 24px; background: radial-gradient(ellipse at 50% 20%, #103139 0, transparent 58%), #0b1219; }
        main { width: 100%; max-width: 520px; }
        .brand { display: flex; align-items: center; justify-content: center; gap: 12px; margin-bottom: 32px; }
        .brand-icon { width: 40px; height: 40px; display: grid; place-items: center; background: #102b35; border: 1px solid #205567; border-radius: 10px; color: #4de2f5; }
        .brand-name { font-size: 23px; font-weight: 700; letter-spacing: -.6px; }
        .brand-caption { display: block; margin-top: 2px; color: #90a7ba; font-size: 10px; letter-spacing: 1.7px; text-transform: uppercase; }
        .card { overflow: hidden; border: 1px solid #294052; border-radius: 24px; background: #14212d; box-shadow: 0 24px 72px #0005; animation: arrive .45s ease-out both; }
        .content { padding: 40px 36px 32px; text-align: center; }
        .status-icon { display: grid; place-items: center; width: 88px; height: 88px; margin: 0 auto 24px; border: 1px solid #218c80; border-radius: 50%; background: #123b3c; color: #4ce6bd; box-shadow: 0 0 0 9px #26d5b509; }
        .status-icon svg { width: 40px; height: 40px; }
        .check { stroke-dasharray: 32; stroke-dashoffset: 32; animation: draw .4s .2s ease-out forwards; }
        .eyebrow { margin: 0 0 12px; color: #4ce6bd; font-size: 11px; font-weight: 700; letter-spacing: 1.8px; text-transform: uppercase; }
        h1 { margin: 0; font-size: clamp(26px, 6vw, 32px); line-height: 1.2; letter-spacing: -.8px; }
        .description { max-width: 360px; margin: 16px auto 28px; color: #b4c4d2; font-size: 16px; line-height: 1.65; }
        .next { padding: 20px; text-align: left; border: 1px solid #245163; border-radius: 14px; background: #102b36; }
        .next-title { display: flex; align-items: center; gap: 10px; color: #66e6f7; font-size: 15px; font-weight: 600; }
        .next p { margin: 9px 0 0 30px; color: #b4c4d2; font-size: 14px; line-height: 1.6; }
        .footer { margin: 0; padding: 18px 24px; border-top: 1px solid #293d4d; text-align: center; background: #101b25; color: #94aabd; font-size: 13px; line-height: 1.5; }
        .retry .status-icon { color: #ffd36b; background: #352e20; border-color: #8e7137; box-shadow: 0 0 0 9px #ffd36b08; }
        .retry .eyebrow { color: #ffd36b; }
        svg { width: 24px; height: 24px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; flex-shrink: 0; }
        .next svg { width: 20px; height: 20px; }
        @keyframes arrive { from { opacity: 0; transform: translateY(12px); } to { opacity: 1; transform: translateY(0); } }
        @keyframes draw { to { stroke-dashoffset: 0; } }
        @media (max-width: 480px) { body { padding: 28px 16px; } .brand { margin-bottom: 24px; } .content { padding: 32px 24px 24px; } .next { padding: 16px; } }
        @media (prefers-reduced-motion: reduce) { .card, .check { animation: none; } .check { stroke-dashoffset: 0; } }
        """;

    private static readonly string Styles = StyleSource.ReplaceLineEndings("\n");

    // Permit only this stylesheet; no scripts, external assets, or forms are needed.
    internal static string ContentSecurityPolicy { get; } =
        $"default-src 'none'; style-src 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Styles)))}'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    internal static string Render(bool connected)
    {
        var title = connected ? "RigMD is connected" : "Connection not completed";
        var description = connected
            ? "Your website account is now connected to RigMD Desktop on this PC."
            : "We couldn't complete this account connection. Return to the app to try again.";
        var next = connected ? "Return to RigMD Desktop" : "Start a fresh connection";
        var instruction = connected
            ? "Switch back to the RigMD app to continue. You can close this browser tab."
            : "Open RigMD Desktop, then choose Connect website account again.";
        var icon = connected
            ? "<path class=\"check\" d=\"m7 16 6 6 12-13\"/>"
            : "<path d=\"M16 8v10m0 6h.01\"/>";
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="referrer" content="no-referrer">
              <title>{{title}}</title>
              <style>{{Styles}}</style>
            </head>
            <body>
              <main aria-labelledby="page-title">
                <div class="brand" aria-label="RigMD Diagnostic Support">
                  <span class="brand-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2 12h4l3-8 5 16 3-8h5"/></svg></span>
                  <div><span class="brand-name">RigMD</span><span class="brand-caption">Diagnostic support</span></div>
                </div>
                <section class="card {{(connected ? "connected" : "retry")}}" aria-labelledby="page-title">
                  <div class="content">
                    <div class="status-icon"><svg viewBox="0 0 32 32" aria-hidden="true">{{icon}}</svg></div>
                    <p class="eyebrow">{{(connected ? "Connection complete" : "Let's try again")}}</p>
                    <h1 id="page-title">{{title}}</h1>
                    <p class="description">{{description}}</p>
                    <div class="next">
                      <div class="next-title"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="13" rx="2"/><path d="M12 16v5m-4 0h8"/></svg>{{next}}</div>
                      <p>{{instruction}}</p>
                    </div>
                  </div>
                  <p class="footer">{{(connected ? "You're all set. Continue in the desktop app." : "Keep RigMD open while connecting your account.")}}</p>
                </section>
              </main>
            </body>
            </html>
            """;
    }
}
