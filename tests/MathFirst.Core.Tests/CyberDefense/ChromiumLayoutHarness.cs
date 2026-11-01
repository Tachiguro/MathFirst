namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/// <summary>
/// Loads production Razor markup and production CSS into a local headless Chromium and returns
/// measured geometry. Each scenario runs in its own same-origin iframe sized to the requested CSS
/// viewport, so media queries and vh units resolve exactly as on a phone of that size.
/// Razor CSS isolation is emulated by rewriting each scoped stylesheet with a scope attribute and
/// stamping that attribute on the owning component's rendered subtree.
/// Synthetic markup only; no network, no learner data, temporary profile directory per run.
/// </summary>
internal static class ChromiumLayoutHarness
{
    public const string BrowserPathVariable = "MATHFIRST_CHROMIUM_PATH";

    public sealed record ScopedStyle(string ScopeAttribute, string RootSelector, string Css);

    public sealed record FocusRequest(string Selector, bool PreventScroll);

    public sealed record Scenario(
        string Id,
        string Probe,
        int Width,
        int Height,
        string Language,
        string Theme,
        bool LargeText,
        string BodyMarkup,
        FocusRequest? Focus);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));

    public static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([GetRepositoryRoot(), .. segments]));

    public static IReadOnlyList<string> GlobalStyles() =>
    [
        // Mirrors the inline <style> and stylesheet order of src/MathFirst.App/wwwroot/index.html.
        "html,body,#app{margin:0;padding:0;width:100%;height:100%;min-height:100%;background-color:#176b4d;}",
        ReadRepositoryFile("src", "MathFirst.App", "wwwroot", "lib", "bootstrap", "dist", "css", "bootstrap.min.css"),
        ReadRepositoryFile("src", "MathFirst.App", "wwwroot", "app.css")
    ];

    public static IReadOnlyList<ScopedStyle> ScopedStyles() =>
    [
        new("b-mf-hud", ".cyber-defense-hud", ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor.css")),
        new("b-mf-upgrade", ".upgrade-terminal-dialog", ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "UpgradeTerminal.razor.css")),
        new("b-mf-ceremony", ".combat-ceremony-overlay", ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseCeremony.razor.css")),
        // Issue #123: each component renders a backdrop and a dialog as siblings; both carry the component scope.
        new("b-mf-spotlight", ".upgrade-spotlight-backdrop, .upgrade-spotlight-overlay", ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "UpgradeSpotlight.razor.css")),
        new("b-mf-unlock", ".operation-unlock-backdrop, .operation-unlock-dialog", ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "OperationUnlockAnnouncement.razor.css")),
        // Issue #136: the tutorial renders its backdrop and overlay as siblings; both carry the component scope.
        new("b-mf-tutorial", ".tutorial-backdrop, .tutorial-overlay", ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseTutorial.razor.css"))
    ];

    public static string? FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable(BrowserPathVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            "/usr/bin/google-chrome",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser"
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<JsonElement[]> RunAsync(IReadOnlyList<Scenario> scenarios, bool reducedMotion, string workDirectory)
    {
        var browser = FindBrowser();
        Assert.True(browser is not null,
            $"ENVIRONMENT_BLOCKED: no local Chromium/Chrome/Edge executable found. Set {BrowserPathVariable} to a Chromium-based browser.");

        Directory.CreateDirectory(workDirectory);
        var pagePath = Path.Combine(workDirectory, "harness.html");
        await File.WriteAllTextAsync(pagePath, BuildHarnessPage(scenarios), new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo(browser!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // The dumped DOM is UTF-8; the console code page would garble measured Cyrillic text.
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "--headless=new",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-extensions",
            "--disable-background-networking",
            "--disable-sync",
            "--disable-component-update",
            "--hide-scrollbars",
            "--force-device-scale-factor=1",
            $"--user-data-dir={Path.Combine(workDirectory, "profile")}",
            "--window-size=1280,1024",
            "--virtual-time-budget=60000",
            "--dump-dom"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (reducedMotion)
        {
            startInfo.ArgumentList.Add("--force-prefers-reduced-motion");
        }
        startInfo.ArgumentList.Add(new Uri(pagePath).AbsoluteUri);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(150)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Chromium layout harness timed out.");
        }

        var output = await stdout;
        var match = Regex.Match(output, @"<pre id=""mf-harness-result"">([\s\S]*?)</pre>");
        Assert.True(match.Success && match.Groups[1].Value.Length > 0,
            $"Chromium harness produced no result (exit {process.ExitCode}). stderr: {Truncate(await stderr, 2000)}");

        using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
        var root = document.RootElement.Clone();
        Assert.True(root.GetProperty("done").GetBoolean(), "Chromium harness did not finish: " + root.GetProperty("error").GetString());
        var results = root.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(scenarios.Count, results.Length);
        return results;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static string BuildHarnessPage(IReadOnlyList<Scenario> scenarios)
    {
        var payload = JsonSerializer.Serialize(new
        {
            globalStyles = GlobalStyles(),
            scopedStyles = ScopedStyles().Select(s => new { attr = s.ScopeAttribute, root = s.RootSelector, css = s.Css }),
            scenarios = scenarios.Select(s => new
            {
                id = s.Id,
                probe = s.Probe,
                width = s.Width,
                height = s.Height,
                lang = s.Language,
                theme = s.Theme,
                largeText = s.LargeText,
                body = s.BodyMarkup,
                focus = s.Focus is null ? null : new { selector = s.Focus.Selector, preventScroll = s.Focus.PreventScroll }
            })
        });

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{margin:0}iframe{border:0;display:block}</style></head><body>"
            + "<pre id=\"mf-harness-result\"></pre><script>const MF_PAYLOAD = " + payload + ";\n" + HarnessScript + "</script></body></html>";
    }

    private const string HarnessScript = """
        const out = document.getElementById('mf-harness-result');
        const state = { done: false, error: null, results: [] };
        const publish = () => { out.textContent = JSON.stringify(state); };

        function splitTopLevel(text) {
            const parts = []; let depth = 0, start = 0;
            for (let i = 0; i < text.length; i++) {
                const ch = text[i];
                if (ch === '(' || ch === '[') depth++;
                else if (ch === ')' || ch === ']') depth--;
                else if (ch === ',' && depth === 0) { parts.push(text.slice(start, i)); start = i + 1; }
            }
            parts.push(text.slice(start));
            return parts;
        }

        // Razor CSS isolation appends the scope attribute to the last compound selector, before pseudo-classes.
        function scopeSelector(selector, attr) {
            return splitTopLevel(selector).map(raw => {
                const sel = raw.trim(); let depth = 0, start = 0;
                for (let i = 0; i < sel.length; i++) {
                    const ch = sel[i];
                    if (ch === '(' || ch === '[') depth++;
                    else if (ch === ')' || ch === ']') depth--;
                    else if (depth === 0 && (ch === ' ' || ch === '>' || ch === '+' || ch === '~')) start = i + 1;
                }
                const head = sel.slice(0, start), last = sel.slice(start);
                let insert = last.length; depth = 0;
                for (let i = 0; i < last.length; i++) {
                    const ch = last[i];
                    if (ch === '(' || ch === '[') depth++;
                    else if (ch === ')' || ch === ']') depth--;
                    else if (depth === 0 && ch === ':') { insert = i; break; }
                }
                return head + last.slice(0, insert) + '[' + attr + ']' + last.slice(insert);
            }).join(', ');
        }

        function scopeRules(rules, attr) {
            for (const rule of rules) {
                if (rule.selectorText !== undefined) rule.selectorText = scopeSelector(rule.selectorText, attr);
                else if (rule.cssRules && !(rule.type === CSSRule.KEYFRAMES_RULE)) scopeRules(rule.cssRules, attr);
            }
        }

        function loadFrame(scenario) {
            return new Promise((resolve, reject) => {
                const frame = document.createElement('iframe');
                frame.style.width = scenario.width + 'px';
                frame.style.height = scenario.height + 'px';
                frame.onload = () => resolve(frame);
                frame.onerror = reject;
                frame.srcdoc = '<!DOCTYPE html><html lang="' + scenario.lang + '"><head><meta charset="utf-8">'
                    + '<meta name="viewport" content="width=device-width, initial-scale=1.0"></head><body>'
                    + '<div class="status-bar-safe-area"></div><div id="app"><div class="app-theme-root" data-theme="' + scenario.theme + '">'
                    + '<div class="app-layout">' + scenario.body + '</div></div></div></body></html>';
                document.body.appendChild(frame);
            });
        }

        function applyStyles(d, scenario) {
            for (const css of MF_PAYLOAD.globalStyles) {
                const style = d.createElement('style'); style.textContent = css; d.head.appendChild(style);
            }
            for (const scoped of MF_PAYLOAD.scopedStyles) {
                const style = d.createElement('style'); style.textContent = scoped.css; d.head.appendChild(style);
                scopeRules(style.sheet.cssRules, scoped.attr);
                for (const root of d.querySelectorAll(scoped.root)) {
                    root.setAttribute(scoped.attr, '');
                    for (const el of root.querySelectorAll('*')) el.setAttribute(scoped.attr, '');
                }
            }
            if (scenario.largeText) {
                const style = d.createElement('style'); style.textContent = 'html{font-size:130% !important;}'; d.head.appendChild(style);
            }
        }

        function rectOf(el) {
            if (!el) return null;
            const r = el.getBoundingClientRect();
            return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, width: r.width, height: r.height };
        }

        function visibleFraction(w, el) {
            if (!el) return 0;
            const r = el.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0) return 0;
            let top = Math.max(r.top, 0), left = Math.max(r.left, 0);
            let bottom = Math.min(r.bottom, w.innerHeight), right = Math.min(r.right, w.innerWidth);
            for (let a = el.parentElement; a; a = a.parentElement) {
                const cs = w.getComputedStyle(a);
                if (cs.overflowX !== 'visible' || cs.overflowY !== 'visible') {
                    const ar = a.getBoundingClientRect();
                    top = Math.max(top, ar.top + a.clientTop);
                    left = Math.max(left, ar.left + a.clientLeft);
                    bottom = Math.min(bottom, ar.top + a.clientTop + a.clientHeight);
                    right = Math.min(right, ar.left + a.clientLeft + a.clientWidth);
                }
            }
            return (Math.max(0, bottom - top) * Math.max(0, right - left)) / (r.width * r.height);
        }

        function hits(d, el) {
            if (!el) return false;
            const r = el.getBoundingClientRect();
            const hit = d.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            return !!hit && (hit === el || el.contains(hit));
        }

        function scrolledElements(d) {
            const list = [];
            for (const el of [d.documentElement, d.body, ...d.body.querySelectorAll('*')]) {
                if (el.scrollTop !== 0 || el.scrollLeft !== 0) list.push({ cls: (el.className && el.className.baseVal === undefined ? el.className : el.tagName) + '', top: el.scrollTop, left: el.scrollLeft });
            }
            return list;
        }

        function applyFocus(d, scenario) {
            if (!scenario.focus) return null;
            const target = d.querySelector(scenario.focus.selector);
            const before = scrolledElements(d);
            if (target) target.focus({ preventScroll: scenario.focus.preventScroll });
            return {
                selector: scenario.focus.selector,
                preventScroll: scenario.focus.preventScroll,
                found: !!target,
                active: !!target && d.activeElement === target,
                scrolledBefore: before,
                scrolledAfter: scrolledElements(d),
                focusedVisible: visibleFraction(d.defaultView, target)
            };
        }

        function terminalProbe(d, w) {
            const q = s => d.querySelector(s);
            const dialog = q('.upgrade-terminal-dialog'), content = q('.upgrade-terminal-content');
            const cards = q('.upgrade-cards-container'), close = q('.upgrade-close-btn');
            const cardList = [...d.querySelectorAll('.upgrade-card')];
            const first = cardList[0], last = cardList[cardList.length - 1];
            const pinned = () => ({
                title: visibleFraction(w, q('#upgrade-terminal-title')),
                subtitle: visibleFraction(w, q('#upgrade-terminal-subtitle')),
                level: visibleFraction(w, d.querySelectorAll('.meta-stat-pill')[0]),
                sp: visibleFraction(w, q('.meta-stat-sp')),
                close: visibleFraction(w, close),
                closeHit: hits(d, close)
            });
            const advice = q('.upgrade-guided-advice-card');
            const result = {
                present: !!dialog && !!content && !!cards && !!close && cardList.length > 0,
                cardCount: cardList.length,
                guidedAdviceAtOpen: advice ? visibleFraction(w, advice) : null,
                atOpen: pinned(),
                rects: { dialog: rectOf(dialog), content: rectOf(content), cards: rectOf(cards), close: rectOf(close) },
                contentDisplay: content ? w.getComputedStyle(content).display : null,
                contentScrollTop: content ? content.scrollTop : null,
                dialogScrollTop: dialog ? dialog.scrollTop : null,
                horizontalOverflow: !!dialog && (dialog.scrollWidth > dialog.clientWidth + 1 || content.scrollWidth > content.clientWidth + 1),
                cards: cards ? { overflowY: w.getComputedStyle(cards).overflowY, scrollHeight: cards.scrollHeight, clientHeight: cards.clientHeight } : null,
                contentOverflow: content ? { overflowY: w.getComputedStyle(content).overflowY, scrollHeight: content.scrollHeight, clientHeight: content.clientHeight } : null
            };
            if (!result.present) return result;

            // Only user-scrollable containers may be scrolled here; overflow:hidden would hide a trap.
            const userScrollable = el => ['auto', 'scroll'].includes(w.getComputedStyle(el).overflowY);
            const contentScrollable = userScrollable(content);
            const reveal = el => {
                if (visibleFraction(w, el) >= 0.99 || !contentScrollable) return;
                const r = el.getBoundingClientRect(), c = content.getBoundingClientRect();
                if (r.bottom > c.top + content.clientTop + content.clientHeight) content.scrollTop += r.bottom - (c.top + content.clientTop + content.clientHeight) + 8;
                else if (r.top < c.top) content.scrollTop -= c.top - r.top + 8;
            };
            const reset = () => { cards.scrollTop = 0; if (contentScrollable) content.scrollTop = 0; };

            reset();
            result.firstCardHeaderAtStart = visibleFraction(w, first.querySelector('.upgrade-card-header'));
            if (userScrollable(cards)) cards.scrollTop = cards.scrollHeight;
            result.cardsMaxScrollTop = cards.scrollTop;
            result.afterScroll = pinned();
            result.closeRectAfterScroll = rectOf(close);
            const lastCostRow = last.querySelector('.upgrade-cost-row');
            reveal(lastCostRow);
            result.lastCardCostRowAtEnd = visibleFraction(w, lastCostRow);

            reset();
            reveal(close);
            result.closeReachable = { visible: visibleFraction(w, close), hit: hits(d, close) };

            const gotIt = q('.guided-advice-skip-btn');
            if (gotIt) {
                reset();
                reveal(gotIt);
                const r = gotIt.getBoundingClientRect();
                result.guidedAdviceAction = { visible: visibleFraction(w, gotIt), hit: hits(d, gotIt), width: r.width, height: r.height };
            }

            result.purchaseButtons = [...d.querySelectorAll('.upgrade-card-purchase-btn')].map(btn => {
                reset();
                const delta = btn.getBoundingClientRect().top - cards.getBoundingClientRect().top - 8;
                if (userScrollable(cards)) cards.scrollTop = Math.max(0, delta);
                reveal(btn);
                return { cls: btn.className, visible: visibleFraction(w, btn), hit: hits(d, btn) };
            });
            reset();
            result.scrolledOutsideCards = scrolledElements(d).filter(e => !e.cls.includes('upgrade-cards-container'));
            return result;
        }

        // Issue #122: active-gameplay HUD geometry. Clipping is measured against ancestor overflow only, because the
        // page itself may scroll vertically on short viewports.
        function clippedByAncestors(w, el) {
            if (!el) return 0;
            const r = el.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0) return 0;
            let top = r.top, left = r.left, bottom = r.bottom, right = r.right;
            // Only hidden/clip ancestors make content unreachable; auto/scroll containers can be scrolled.
            const cuts = v => v === 'hidden' || v === 'clip';
            for (let a = el.parentElement; a; a = a.parentElement) {
                const cs = w.getComputedStyle(a);
                if (cuts(cs.overflowX) || cuts(cs.overflowY)) {
                    const ar = a.getBoundingClientRect();
                    top = Math.max(top, ar.top + a.clientTop);
                    left = Math.max(left, ar.left + a.clientLeft);
                    bottom = Math.min(bottom, ar.top + a.clientTop + a.clientHeight);
                    right = Math.min(right, ar.left + a.clientLeft + a.clientWidth);
                }
            }
            return (Math.max(0, bottom - top) * Math.max(0, right - left)) / (r.width * r.height);
        }

        function hudProbe(d, w) {
            const q = s => d.querySelector(s);
            const selectors = ['.mathfirst-header', '.mathfirst-title', '.upgrade-nav-btn', '.pause-practice-btn', '.header-sp-badge',
                '.player-shield-panel', '.player-hp-bar', '.player-hp-value', '.player-xp-cluster', '.player-level-label', '.player-xp-bar',
                '.player-xp-value', '.shield-status-column', '.tactical-radar-widget', '.training-top-region', '.practice-layout',
                '.expression-row', '.answer-input', '.numeric-keypad', '.operation-progress-hud'];
            const rects = {}, clip = {}, hit = {}, textOverflow = {};
            for (const sel of selectors) {
                const el = q(sel);
                rects[sel] = rectOf(el);
                if (!el) continue;
                clip[sel] = clippedByAncestors(w, el);
                hit[sel] = hits(d, el);
                textOverflow[sel] = el.scrollWidth > el.clientWidth + 1;
            }
            const keypad = [...d.querySelectorAll('.numeric-keypad-button')].map(rectOf);
            const operations = [...d.querySelectorAll('.operation-progress-entry')].map(el => ({ rect: rectOf(el), clip: clippedByAncestors(w, el), overflow: el.scrollWidth > el.clientWidth + 1 }));
            const overlap = (a, b) => !!a && !!b && a.left < b.right - 0.5 && b.left < a.right - 0.5 && a.top < b.bottom - 0.5 && b.top < a.bottom - 0.5;
            const pairs = [['.player-hp-bar', '.player-xp-bar'], ['.player-hp-value', '.player-level-label'], ['.player-hp-value', '.player-xp-value'],
                ['.player-hp-bar', '.player-level-label'], ['.player-xp-bar', '.player-level-label'], ['.player-xp-bar', '.player-xp-value'],
                ['.player-level-label', '.player-xp-value'], ['.upgrade-nav-btn', '.pause-practice-btn'], ['.mathfirst-title', '.upgrade-nav-btn'],
                ['.mathfirst-title', '.pause-practice-btn'], ['.player-xp-cluster', '.tactical-radar-widget'], ['.shield-status-column', '.tactical-radar-widget']];
            const overlaps = pairs.filter(([a, b]) => overlap(rects[a], rects[b])).map(([a, b]) => a + ' x ' + b);
            const doc = d.documentElement;
            return {
                rects, clip, hit, textOverflow, keypad, operations, overlaps,
                horizontalOverflow: doc.scrollWidth > w.innerWidth + 1 || d.body.scrollWidth > w.innerWidth + 1,
                scrollWidth: doc.scrollWidth, scrollHeight: doc.scrollHeight, viewportHeight: w.innerHeight, viewportWidth: w.innerWidth,
                header: { fontPx: q('.upgrade-nav-btn') ? parseFloat(w.getComputedStyle(q('.upgrade-nav-btn')).fontSize) : null }
            };
        }

        // Issue #134: opponent hierarchy, numeric HP, artwork room, player panel and the protected math geometry.
        function combatProbe(d, w) {
            const q = s => d.querySelector(s);
            const selectors = ['.enemy-battle-scene', '.scene-top-bar', '.scene-enemy-name', '.scene-sector-tag', '.scene-wave-tag',
                '.scene-stage', '.scene-intel', '.scene-enemy-desc', '.enemy-hp-cluster', '.enemy-hp-bar', '.enemy-hp-value',
                '.scene-artwork-wrapper', '.scene-opponent-image', '.player-shield-panel', '.player-hp-bar', '.player-hp-value',
                '.player-xp-cluster', '.shield-badges-row', '.shield-recharge-bar', '.tactical-radar-widget', '.training-top-region', '.practice-layout',
                '.expression-row', '.answer-input', '.numeric-keypad', '.mathfirst-header', '.operation-progress-hud'];
            const rects = {}, clip = {}, textOverflow = {}, text = {}, lines = {};
            for (const sel of selectors) {
                const el = q(sel);
                rects[sel] = rectOf(el);
                if (!el) continue;
                clip[sel] = clippedByAncestors(w, el);
                textOverflow[sel] = el.scrollWidth > el.clientWidth + 1;
                text[sel] = (el.textContent || '').replace(/\s+/g, ' ').trim();
            }
            for (const sel of ['.scene-enemy-name', '.scene-enemy-desc']) {
                const el = q(sel);
                if (!el) continue;
                const cs = w.getComputedStyle(el);
                let lh = parseFloat(cs.lineHeight);
                if (!isFinite(lh)) lh = parseFloat(cs.fontSize) * 1.2;
                lines[sel] = { clientHeight: el.clientHeight, scrollHeight: el.scrollHeight, lineHeight: lh, fontPx: parseFloat(cs.fontSize) };
            }
            const keypad = [...d.querySelectorAll('.numeric-keypad-button')].map(rectOf);
            const overlap = (a, b) => !!a && !!b && a.left < b.right - 0.5 && b.left < a.right - 0.5 && a.top < b.bottom - 0.5 && b.top < a.bottom - 0.5;
            const pairs = [['.scene-opponent-image', '.scene-enemy-desc'], ['.scene-opponent-image', '.enemy-hp-value'], ['.scene-opponent-image', '.enemy-hp-bar'],
                ['.scene-opponent-image', '.scene-enemy-name'], ['.scene-enemy-name', '.scene-sector-tag'], ['.scene-enemy-name', '.scene-wave-tag'],
                ['.scene-sector-tag', '.scene-wave-tag'], ['.scene-enemy-desc', '.enemy-hp-cluster'], ['.enemy-hp-bar', '.enemy-hp-value'],
                ['.player-hp-value', '.tactical-radar-widget'], ['.player-xp-cluster', '.tactical-radar-widget']];
            const overlaps = pairs.map(([a, b]) => {
                const ra = rects[a], rb = rects[b];
                if (!overlap(ra, rb)) return null;
                const dx = Math.min(ra.right, rb.right) - Math.max(ra.left, rb.left), dy = Math.min(ra.bottom, rb.bottom) - Math.max(ra.top, rb.top);
                return { pair: a + ' x ' + b, depth: Math.min(dx, dy) };
            }).filter(x => x);
            // WCAG contrast of the top-bar text against the bar's own background (text alpha composited over it).
            const rgba = s => { const m = (s.match(/[\d.]+/g) || [0, 0, 0, 0]).map(Number); return { r: m[0], g: m[1], b: m[2], a: m.length > 3 ? m[3] : 1 }; };
            const lum = c => { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
            const contrast = (el, bgEl) => {
                if (!el || !bgEl) return null;
                const bg = rgba(w.getComputedStyle(bgEl).backgroundColor);
                if (bg.a < 1) return null;
                const fg = rgba(w.getComputedStyle(el).color);
                const mix = { r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a) };
                const a = lum(mix), b = lum(bg);
                return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
            };
            const bar = q('.scene-top-bar');
            const contrasts = {
                name: contrast(q('.scene-enemy-name'), bar),
                sector: contrast(q('.scene-sector-tag'), bar),
                wave: contrast(q('.scene-wave-tag .boss-wave-text') || q('.scene-wave-tag'), bar)
            };
            const doc = d.documentElement;
            return {
                rects, clip, textOverflow, text, lines, keypad, overlaps, contrasts,
                horizontalOverflow: doc.scrollWidth > w.innerWidth + 1 || d.body.scrollWidth > w.innerWidth + 1,
                scrollWidth: doc.scrollWidth, viewportHeight: w.innerHeight, viewportWidth: w.innerWidth,
                rootFontPx: parseFloat(w.getComputedStyle(doc).fontSize)
            };
        }

        // Issue #134: replays a sequence of real HUD renders against the running radar arc the way Blazor's diff does:
        // an element with the same segment (render key) is patched in place, a new segment replaces the element. Time
        // passing is emulated by advancing each running animation; a paused animation does not advance.
        function radarProbe(d, w) {
            const arcOf = () => d.querySelector('.radar-timing-arc');
            const progressOf = () => {
                const arc = arcOf();
                if (!arc) return { present: false };
                const anims = arc.getAnimations();
                const timing = anims.length ? anims[0].effect.getComputedTiming() : null;
                return { present: true, animations: anims.length, playState: anims.length ? anims[0].playState : null,
                    progress: timing ? timing.progress : null, segment: arc.getAttribute('data-radar-segment') };
            };
            const steps = [...d.querySelectorAll('template.mf-radar-step')];
            const result = { initial: progressOf(), steps: [] };
            for (const step of steps) {
                const advance = Number(step.getAttribute('data-advance-ms'));
                const arc = arcOf();
                if (arc) for (const a of arc.getAnimations()) if (a.playState === 'running') a.currentTime = (a.currentTime || 0) + advance;
                const next = step.content.querySelector('.radar-timing-arc');
                let action = 'none';
                if (arc && next && arc.getAttribute('data-radar-segment') === next.getAttribute('data-radar-segment')) {
                    for (const name of arc.getAttributeNames()) if (name !== 'b-mf-hud' && !next.hasAttribute(name)) arc.removeAttribute(name);
                    for (const name of next.getAttributeNames()) if (arc.getAttribute(name) !== next.getAttribute(name)) arc.setAttribute(name, next.getAttribute(name));
                    action = 'patch';
                } else if (arc && next) {
                    const clone = next.cloneNode(true); clone.setAttribute('b-mf-hud', ''); arc.replaceWith(clone); action = 'replace';
                } else if (arc) {
                    arc.remove(); action = 'remove';
                } else if (next) {
                    const clone = next.cloneNode(true); clone.setAttribute('b-mf-hud', ''); d.querySelector('.radar-svg').appendChild(clone); action = 'insert';
                }
                void d.body.offsetHeight;
                result.steps.push({ action, ...progressOf() });
            }
            return result;
        }

        // Issue #136: the player status rows (Level/XP, shields, HP), their icons, meters and values, and the protected
        // math geometry. Clipping is measured against ancestor overflow only, as in the hud probe.
        function statusProbe(d, w) {
            const q = s => d.querySelector(s);
            const selectors = ['.player-shield-panel', '.shield-status-column', '.player-xp-cluster', '.player-status-heading', '.player-hp-cluster',
                '.xp-progress-icon', '.player-level-label', '.player-xp-bar', '.player-xp-value', '.shield-badges-row', '.shield-badge',
                '.shield-count-text', '.shield-recharge-bar', '.shield-recharge-value', '.player-hp-icon', '.player-hp-bar', '.player-hp-value',
                '.tactical-radar-widget', '.training-top-region', '.practice-layout', '.expression-row', '.answer-input', '.numeric-keypad',
                '.operation-progress-hud', '.mathfirst-header'];
            const rects = {}, clip = {}, textOverflow = {}, text = {}, fontPx = {};
            for (const sel of selectors) {
                const el = q(sel);
                rects[sel] = rectOf(el);
                if (!el) continue;
                clip[sel] = clippedByAncestors(w, el);
                textOverflow[sel] = el.scrollWidth > el.clientWidth + 1;
                text[sel] = (el.textContent || '').replace(/\s+/g, ' ').trim();
                fontPx[sel] = parseFloat(w.getComputedStyle(el).fontSize);
            }
            const keypad = [...d.querySelectorAll('.numeric-keypad-button')].map(rectOf);
            const doc = d.documentElement;
            return {
                rects, clip, textOverflow, text, fontPx, keypad,
                horizontalOverflow: doc.scrollWidth > w.innerWidth + 1 || d.body.scrollWidth > w.innerWidth + 1,
                scrollWidth: doc.scrollWidth, viewportHeight: w.innerHeight, viewportWidth: w.innerWidth
            };
        }

        // Issue #136: a tutorial over the paused game. The gameplay field must be rendered behind it as visible, inert
        // spotlight context; the tutorial is the only dialog and nothing of the practice surface can be hit.
        function tutorialProbe(d, w) {
            const q = s => d.querySelector(s);
            const overlay = q('.tutorial-overlay'), card = q('.tutorial-card'), heading = q('#tutorial-step-title');
            const holder = q('[data-spotlight-active]');
            const active = holder ? holder.getAttribute('data-spotlight-active') : null;
            const target = active ? q('[data-spotlight-id="' + active + '"]') : null;
            const field = {};
            for (const sel of ['.cyber-defense-hud', '.enemy-battle-scene', '.player-shield-panel', '.expression-row', '.answer-input', '.numeric-keypad']) {
                const el = q(sel);
                if (!el) { field[sel] = null; continue; }
                const cs = w.getComputedStyle(el), r = el.getBoundingClientRect();
                field[sel] = {
                    rect: rectOf(el), visible: visibleFraction(w, el), inert: !!el.closest('[inert]'),
                    displayed: cs.display !== 'none' && cs.visibility !== 'hidden' && parseFloat(cs.opacity) > 0 && r.width > 0 && r.height > 0
                };
            }
            const doc = d.documentElement;
            return {
                present: !!overlay && !!card && !!heading,
                dialogCount: d.querySelectorAll('[role="dialog"]').length,
                menuPresent: !!q('.game-menu-overlay'),
                activeTarget: active,
                target: target ? { rect: rectOf(target), visible: visibleFraction(w, target), zIndex: w.getComputedStyle(target).zIndex } : null,
                headingVisible: visibleFraction(w, heading),
                field,
                practiceSurfaceHit: [...d.querySelectorAll('.numeric-keypad-button, .answer-input, .pause-practice-btn')].some(el => hits(d, el)),
                horizontalOverflow: doc.scrollWidth > w.innerWidth + 1 || d.body.scrollWidth > w.innerWidth + 1
            };
        }

        function homeProbe(d, w) {
            const q = s => d.querySelector(s);
            const dialog = q('.upgrade-terminal-dialog');
            const selectors = ['.mathfirst-header', '.training-top-region', '.practice-gate-overlay', '.expression-row', '.answer-input', '.numeric-keypad', '.numeric-keypad-button', '.pause-practice-btn', '.upgrade-nav-btn', '.pause-upgrade-btn'];
            const background = {};
            for (const sel of selectors) {
                const el = q(sel);
                if (!el) continue;
                const r = el.getBoundingClientRect();
                const hit = r.width > 0 && r.height > 0 ? d.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2) : null;
                background[sel] = {
                    rect: rectOf(el),
                    hitSelf: !!hit && (hit === el || el.contains(hit)),
                    hitInsideTerminal: !!hit && !!dialog && dialog.contains(hit),
                    inert: !!el.closest('[inert]')
                };
            }
            return { terminalOpen: !!dialog, background, terminal: dialog ? terminalProbe(d, w) : null };
        }

        function ceremonyProbe(d, w) {
            const q = s => d.querySelector(s);
            const overlay = q('.combat-ceremony-overlay'), card = q('.combat-ceremony-card');
            const heading = q('#combat-ceremony-title'), button = q('.ceremony-continue');
            const result = {
                present: !!overlay && !!card && !!heading,
                hasButton: !!button,
                dialogRole: overlay ? overlay.getAttribute('role') : null,
                animationName: card ? w.getComputedStyle(card).animationName : null,
                rects: { overlay: rectOf(overlay), card: rectOf(card), button: rectOf(button) }
            };
            if (!result.present) return result;
            overlay.scrollTop = 0;
            const overlayStyle = w.getComputedStyle(overlay);
            result.overlay = { overflowY: overlayStyle.overflowY, scrollHeight: overlay.scrollHeight, clientHeight: overlay.clientHeight };
            result.horizontalOverflow = overlay.scrollWidth > overlay.clientWidth + 1 || card.scrollWidth > card.clientWidth + 1;
            result.headingAtTop = visibleFraction(w, heading);
            if (button) {
                result.buttonAtOpen = visibleFraction(w, button);
                overlay.scrollTop = overlay.scrollHeight;
                result.buttonAfterScroll = visibleFraction(w, button);
                result.buttonHit = hits(d, button);
                const b = button.getBoundingClientRect(), c = card.getBoundingClientRect();
                result.buttonInsideCard = b.left >= c.left - 0.5 && b.right <= c.right + 0.5 && b.bottom <= c.bottom + 0.5;
                result.buttonFontPx = parseFloat(w.getComputedStyle(button).fontSize);
                overlay.scrollTop = 0;
            }
            return result;
        }

        // Issue #125: the cyber main menu, which is the ManualPause gate overlay. Each action is brought into view the
        // way a user would (scrollIntoView) and must then be a full, uncovered touch target. Only user-scrollable
        // containers (overflow auto/scroll, or the document) may have moved: a scrolled overflow:hidden box is a trap.
        function menuProbe(d, w) {
            const q = s => d.querySelector(s);
            const overlay = q('.game-menu-overlay'), panel = q('.game-menu-panel'), heading = q('#practice-gate-title');
            const actions = [...d.querySelectorAll('.game-menu-panel .practice-overlay-action, .game-menu-panel .game-menu-action')];
            const doc = d.documentElement;
            const result = {
                present: !!overlay && !!panel && !!heading && actions.length > 0,
                actionCount: actions.length,
                animationName: panel ? w.getComputedStyle(panel).animationName : null,
                practiceSurfacePresent: !!q('.numeric-keypad') || !!q('.answer-input'),
                headerMenuPresent: !!q('.mathfirst-header .pause-practice-btn'),
                progressExpanded: !!q('#game-menu-progress') && !q('#game-menu-progress').hidden,
                horizontalOverflow: doc.scrollWidth > w.innerWidth + 1 || d.body.scrollWidth > w.innerWidth + 1,
                scrollWidth: doc.scrollWidth
            };
            if (!result.present) return result;
            const reset = () => { for (const el of [d.scrollingElement, ...d.body.querySelectorAll('*')]) { if (el.scrollTop) el.scrollTop = 0; } };
            const traps = () => [d.documentElement, d.body, ...d.body.querySelectorAll('*')]
                .filter(el => el !== d.scrollingElement && (el.scrollTop !== 0 || el.scrollLeft !== 0) &&
                    !['auto', 'scroll'].includes(w.getComputedStyle(el).overflowY))
                .map(el => (el.className && el.className.baseVal === undefined ? el.className : el.tagName) + '');
            const reach = el => {
                reset();
                el.scrollIntoView({ block: 'center', inline: 'nearest' });
                const r = el.getBoundingClientRect();
                return {
                    cls: el.className + '', text: el.textContent.trim(), width: r.width, height: r.height, left: r.left, right: r.right,
                    visible: visibleFraction(w, el), hit: hits(d, el), clip: clippedByAncestors(w, el),
                    textOverflow: el.scrollWidth > el.clientWidth + 1, traps: traps()
                };
            };
            reset();
            result.headingAtOpen = visibleFraction(w, heading);
            const p = panel.getBoundingClientRect();
            result.panel = { left: p.left, right: p.right, width: p.width };
            result.actions = actions.map(reach);
            const tail = q('#game-menu-progress:not([hidden]) .game-menu-player-level') || q('#game-menu-progress:not([hidden]) .ready-progress-entry:last-child');
            result.progressTail = tail ? reach(tail) : null;
            reset();
            return result;
        }

        // Issue #123: the operation-unlock announcement and the Upgrade spotlight. Every acknowledgement action is brought
        // into view as a user would and must be a full, uncovered touch target. The spotlighted header Upgrade control stays
        // visible and tappable above the dimmed page, while the practice surface underneath cannot be hit.
        function presentationProbe(d, w) {
            const q = s => d.querySelector(s);
            const dialog = q('.operation-unlock-dialog') || q('.upgrade-spotlight-overlay');
            const card = q('.operation-unlock-content') || q('.upgrade-spotlight-card');
            const heading = q('#operation-unlock-title') || q('#upgrade-spotlight-title');
            const actions = [...d.querySelectorAll('.operation-unlock-continue-btn, .upgrade-spotlight-btn')];
            const doc = d.documentElement;
            const result = {
                present: !!dialog && !!card && !!heading && actions.length > 0,
                kind: q('.operation-unlock-dialog') ? 'unlock' : 'spotlight',
                dialogCount: d.querySelectorAll('[role="dialog"]').length,
                headerInert: !!q('.mathfirst-header[inert]'),
                mainInert: !!q('main.training-card[inert]'),
                horizontalOverflow: doc.scrollWidth > w.innerWidth + 1 || d.body.scrollWidth > w.innerWidth + 1 ||
                    (!!dialog && dialog.scrollWidth > dialog.clientWidth + 1),
                scrollWidth: doc.scrollWidth
            };
            if (!result.present) return result;
            const reset = () => { for (const el of [d.scrollingElement, ...d.body.querySelectorAll('*')]) { if (el.scrollTop) el.scrollTop = 0; } };
            const traps = () => [d.documentElement, d.body, ...d.body.querySelectorAll('*')]
                .filter(el => el !== d.scrollingElement && (el.scrollTop !== 0 || el.scrollLeft !== 0) &&
                    !['auto', 'scroll'].includes(w.getComputedStyle(el).overflowY))
                .map(el => (el.className && el.className.baseVal === undefined ? el.className : el.tagName) + '');
            const reach = el => {
                reset();
                el.scrollIntoView({ block: 'center', inline: 'nearest' });
                const r = el.getBoundingClientRect();
                return {
                    cls: el.className + '', text: el.textContent.trim(), width: r.width, height: r.height,
                    visible: visibleFraction(w, el), hit: hits(d, el), clip: clippedByAncestors(w, el),
                    textOverflow: el.scrollWidth > el.clientWidth + 1, traps: traps()
                };
            };
            reset();
            result.headingAtOpen = visibleFraction(w, heading);
            const c = card.getBoundingClientRect();
            result.card = { left: c.left, right: c.right, top: c.top, bottom: c.bottom };
            const target = q('[data-spotlight-active="upgrade-header"] [data-spotlight-id="upgrade-header"]');
            if (target) {
                const t = target.getBoundingClientRect();
                result.spotlightTarget = {
                    visible: visibleFraction(w, target), hit: hits(d, target), disabled: !!target.disabled, width: t.width, height: t.height,
                    overlapsCard: t.left < c.right - 0.5 && c.left < t.right - 0.5 && t.top < c.bottom - 0.5 && c.top < t.bottom - 0.5
                };
            } else {
                result.spotlightTarget = null;
            }
            result.practiceSurfaceHit = [...d.querySelectorAll('.numeric-keypad-button, .answer-input, .pause-practice-btn')].some(el => hits(d, el));
            result.actions = actions.map(reach);
            reset();
            return result;
        }

        (async () => {
            try {
                for (const scenario of MF_PAYLOAD.scenarios) {
                    const frame = await loadFrame(scenario);
                    const d = frame.contentDocument, w = frame.contentWindow;
                    applyStyles(d, scenario);
                    void d.body.offsetHeight;
                    const focus = applyFocus(d, scenario);
                    const probe = scenario.probe === 'hud' ? hudProbe(d, w) : scenario.probe === 'home' ? homeProbe(d, w)
                        : scenario.probe === 'ceremony' ? ceremonyProbe(d, w) : scenario.probe === 'menu' ? menuProbe(d, w)
                        : scenario.probe === 'presentation' ? presentationProbe(d, w) : scenario.probe === 'combat' ? combatProbe(d, w)
                        : scenario.probe === 'radar' ? radarProbe(d, w) : scenario.probe === 'status' ? statusProbe(d, w)
                        : scenario.probe === 'tutorial' ? tutorialProbe(d, w) : terminalProbe(d, w);
                    state.results.push({
                        id: scenario.id,
                        viewport: { width: w.innerWidth, height: w.innerHeight },
                        reducedMotion: w.matchMedia('(prefers-reduced-motion: reduce)').matches,
                        focus,
                        probe
                    });
                    frame.remove();
                }
                state.done = true;
            } catch (e) {
                state.error = String(e && e.stack || e);
            }
            publish();
        })();
        """;
}
