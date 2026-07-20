// Headless-browser boot smoke test for the FEBuilderGBA WebAssembly app (issue #1867).
//
// WHY THIS EXISTS: #1867 shipped a web app that returned HTTP 200 for every static asset yet never
// actually booted — Avalonia's runtime requested `_framework/avalonia.js` (404) and the page hung on
// the loading splash forever. HTTP-200 checks structurally cannot catch that class of bug; only a
// real browser that executes the wasm runtime can. This test:
//   1. serves the published AppBundle under the GitHub Pages sub-path (default `/FEBuilderGBA/`),
//      with correct MIME types and REAL 404s (no SPA fallback that would mask a missing asset);
//   2. loads it in headless Chromium (Playwright), in ONE browser process with a fresh isolated
//      context/page per viewport it exercises (see the viewport-matrix section below);
//   3. FAILS if any `avalonia.js` request returns >= 400 (the exact #1867 symptom), or if the
//      Avalonia canvas never mounts (the app didn't render);
//   4. always writes a screenshot for PR proof / debugging.
//
// Viewport coverage (#1998 follow-up):
//   - If BOTH SMOKE_VIEWPORT_WIDTH and SMOKE_VIEWPORT_HEIGHT are absent, the script runs an
//     in-process, sequential dual-viewport matrix — 600x500 ("compact") then 1920x852
//     ("acceptance") — in ONE browser process, using a fresh isolated context/page per viewport.
//     This is the default so CI (which never sets these vars) gets both the Map Editor's compact
//     scroll path and its natural desktop layout covered by a single invocation. Failures from
//     EITHER viewport are aggregated; the process exits nonzero if either run fails. The script
//     never recursively spawns itself for this.
//   - If BOTH are supplied, the script runs exactly that single viewport (no matrix). Supplying
//     only one of the two is rejected with a clear error (exit code 2).
//   - The 1920x852 ("acceptance") run — or the single explicit-viewport run — owns the exact
//     SMOKE_SCREENSHOT path and its `.before.png` (matching what `.github/workflows/pages.yml`
//     uploads today; that workflow is unchanged by this script). The compact run's screenshots,
//     and the Move Cost Editor's own before/after proof at every viewport, are written to
//     collision-free sidecar filenames derived from SMOKE_SCREENSHOT (e.g.
//     `web-boot-smoke.compact.png`, `web-boot-smoke.movecost.png`) — these sidecars are NOT
//     uploaded by the workflow; they exist purely for local/manual inspection.
//   - The FINAL screenshot at every viewport (main path) is a full-viewport capture with NO
//     content clip — it is taken after navigating to the Visual Map Editor, so it shows the
//     Map Editor split-scroller layout at exactly the run's configured viewport size (DPR 1).
//
// Env:
//   SMOKE_WWWROOT     (required) absolute path to the published `.../publish/wwwroot`.
//   SMOKE_BASE_PATH   (default `/FEBuilderGBA/`) the sub-path to serve under — mirror the deployed
//                     <base href> so base-relative resolution is exercised the same way as prod.
//   SMOKE_SCREENSHOT  (default `web-smoke.png`) screenshot output path — see "Viewport coverage"
//                     above for exactly which run owns this path vs. a derived sidecar.
//   SMOKE_TIMEOUT_MS  (default 120000) boot timeout — cold wasm + ~6.8 MB config.zip is slow.
//   SMOKE_VIEWPORT_WIDTH / SMOKE_VIEWPORT_HEIGHT  (both optional; both-or-neither) explicit single
//                     viewport override. Leave BOTH unset to get the default compact+acceptance
//                     matrix described above.
//   SMOKE_ROM         (optional) ROM fixture path, or the literal string `synthetic` to generate a
//                     license-clean FE8U header-only ROM. When present, the smoke test loads it
//                     through the E2E-only JSExport hook, opens Move Cost Editor and a
//                     newly-cataloged editor (AI Script) through the real launcher delegate, then
//                     opens the Visual Map Editor (#1998) and asserts its compact/desktop
//                     split-scroller layout at the run's viewport. Synthetic map pixels are
//                     injected ONLY when SMOKE_ROM is exactly `synthetic` (never for a real ROM —
//                     real runs always show the ROM's own authentic rendered map/palette); with
//                     synthetic pixels active, both inner-canvas axes (width AND height) are
//                     asserted to overflow their scroller's viewport. With a real ROM, the same
//                     metrics are logged (for manual/CI-log inspection) without a hard overflow
//                     assertion, since a real chapter map's on-screen size is ROM-data-dependent.

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';
import { validateMapEditorLayoutMetrics, bounded, REQUIRED_METRIC_KEYS } from './layout-metrics-validation.mjs';
import { CASES, missingKeyCases } from './layout-metrics-validation.cases.mjs';
import { parseViewportOverride } from './viewport-override.mjs';
import { CASES as VIEWPORT_CASES } from './viewport-override.cases.mjs';

// #1998 follow-up (review): a fast, dependency-free re-verification of the SAME fail-closed
// validateMapEditorLayoutMetrics() contract exercised by layout-metrics-validation.test.mjs's
// `node:test` coverage — run ONCE here, before any browser is launched. `.github/workflows/pages.yml`
// only ever invokes this script directly (never `node --test`), so without this self-check a future
// accidental change that silently weakens the fail-closed gate would have ZERO coverage on the path
// that actually gates CI/merges — no workflow file is touched to get that coverage. Both this
// self-check and the node:test suite import the SAME shared case table
// (layout-metrics-validation.cases.mjs) so the two coverage paths can never silently diverge.
function runContractSelfCheck() {
  const allCases = [...CASES, ...missingKeyCases(REQUIRED_METRIC_KEYS)];
  const selfCheckFailures = [];
  for (const c of allCases) {
    const { metrics, errors } = validateMapEditorLayoutMetrics(c.input, c.options);
    if (c.expect === 'accept') {
      if (metrics === null || errors.length > 0) {
        selfCheckFailures.push(`"${c.name}": expected ACCEPT but got metrics=${metrics === null ? 'null' : 'object'}, ` +
          `errors=${bounded(JSON.stringify(errors))}`);
      }
    } else if (metrics !== null || errors.length === 0) {
      selfCheckFailures.push(`"${c.name}": expected REJECT but got metrics=${metrics === null ? 'null' : 'object'}, ` +
        `errors=${bounded(JSON.stringify(errors))}`);
    } else if (c.errorIncludes && !errors.some((e) => e.includes(c.errorIncludes))) {
      selfCheckFailures.push(`"${c.name}": rejected as expected, but no error message mentioned "${c.errorIncludes}": ` +
        bounded(JSON.stringify(errors)));
    }
  }
  if (selfCheckFailures.length > 0) {
    console.error('[smoke] validator contract self-check FAILED — the fail-closed ' +
      'validateMapEditorLayoutMetrics() gate itself appears broken (checked before launching any browser):\n - ' +
      selfCheckFailures.join('\n - '));
    process.exit(2);
  }
  console.log(`[smoke] validator contract self-check passed (${allCases.length} cases, shared with ` +
    'layout-metrics-validation.test.mjs — see layout-metrics-validation.cases.mjs).');
}

// #1998 follow-up (review): a fast, dependency-free re-verification of the SAME
// parseViewportOverride() contract exercised by viewport-override.test.mjs's `node:test` coverage —
// run ONCE here, before any server/browser starts. A code-review finding showed a fractional
// explicit viewport value (e.g. "600.5") previously passed the finite/>0-only check and reached
// Playwright's `browser.newContext({ viewport })`, which requires integer dimensions and throws an
// UNHANDLED setup error — turning a should-be-controlled `exit 2` into a crash after startup had
// already begun. This self-check and the node:test suite import the SAME shared case table
// (viewport-override.cases.mjs), and both call the SAME parseViewportOverride() function used to
// resolve the real SMOKE_VIEWPORT_WIDTH/SMOKE_VIEWPORT_HEIGHT env values below, so the two
// coverage paths — and the parsing logic itself — can never silently diverge.
function runViewportParserSelfCheck() {
  const selfCheckFailures = [];
  for (const c of VIEWPORT_CASES) {
    const result = parseViewportOverride(c.wRaw, c.hRaw);
    if (c.expect === 'accept') {
      if (!result.ok || JSON.stringify(result.plan) !== JSON.stringify(c.expectedPlan)) {
        selfCheckFailures.push(`"${c.name}": expected ACCEPT with the expected plan but got ` +
          `ok=${result.ok}, plan=${bounded(JSON.stringify(result.ok ? result.plan : result.error))}`);
      }
    } else if (result.ok) {
      selfCheckFailures.push(`"${c.name}": expected REJECT but got ok=true, plan=${bounded(JSON.stringify(result.plan))}`);
    } else if (c.errorIncludes && !result.error.includes(c.errorIncludes)) {
      selfCheckFailures.push(`"${c.name}": rejected as expected, but the error message did not mention ` +
        `"${c.errorIncludes}": ${bounded(result.error)}`);
    }
  }
  if (selfCheckFailures.length > 0) {
    console.error('[smoke] viewport-override parser self-check FAILED — parseViewportOverride() itself ' +
      'appears broken (checked before launching any browser):\n - ' + selfCheckFailures.join('\n - '));
    process.exit(2);
  }
  console.log(`[smoke] viewport-override parser self-check passed (${VIEWPORT_CASES.length} cases, shared ` +
    'with viewport-override.test.mjs — see viewport-override.cases.mjs).');
}

const WWWROOT = process.env.SMOKE_WWWROOT;
const BASE_PATH = process.env.SMOKE_BASE_PATH || '/FEBuilderGBA/';
const SCREENSHOT = process.env.SMOKE_SCREENSHOT || 'web-smoke.png';
const BOOT_TIMEOUT_MS = Number(process.env.SMOKE_TIMEOUT_MS || 120000);
const ROM_PATH = process.env.SMOKE_ROM;
// Only the LITERAL string 'synthetic' ever triggers synthetic map-pixel injection (TestHooks.cs
// hard-refuses an inconsistent request — see runViewport()'s handling of metrics.error below).
const IS_SYNTHETIC_ROM = ROM_PATH === 'synthetic';

if (!WWWROOT || !fs.existsSync(WWWROOT)) {
  console.error(`[smoke] SMOKE_WWWROOT not found: ${WWWROOT}`);
  process.exit(2);
}
const ROOT = path.resolve(WWWROOT);

// Below this viewport height, the Map Editor's upper controls are expected to overflow and
// scroll (compact path); at/above it, the normal desktop layout is expected to fit without
// scrolling (acceptance path). 700 sits between the compact fixtures used in CI (e.g. 500-600)
// and the editor's own natural desktop height (800), matching MapEditorButtonReadabilityTests.
const MAP_EDITOR_COMPACT_HEIGHT_THRESHOLD = 700;
// The map canvas panel must stay usable at any viewport height (#1998 layout contract).
const MIN_USABLE_MAP_CANVAS_HEIGHT = 240;

// #1998 follow-up: resolve the viewport(s) to exercise via the pure parseViewportOverride()
// function (viewport-override.mjs) — both-or-neither of SMOKE_VIEWPORT_WIDTH /
// SMOKE_VIEWPORT_HEIGHT; absent -> default in-process compact+acceptance matrix (see file header).
function resolveViewportPlan() {
  const result = parseViewportOverride(process.env.SMOKE_VIEWPORT_WIDTH, process.env.SMOKE_VIEWPORT_HEIGHT);
  if (!result.ok) {
    console.error(`[smoke] ${result.error}`);
    process.exit(2);
  }
  return result.plan;
}
runViewportParserSelfCheck();
const VIEWPORT_PLAN = resolveViewportPlan();

// Run the validator contract self-check now — env/viewport parsing is done, but no server or
// browser has started yet.
runContractSelfCheck();

// Derives a collision-free sidecar path from a base screenshot path, e.g.
// deriveSidecar('web-boot-smoke.png', 'compact') -> 'web-boot-smoke.compact.png'.
function deriveSidecar(basePath, suffix) {
  const parsed = path.parse(basePath);
  return path.join(parsed.dir || '.', `${parsed.name}.${suffix}${parsed.ext || '.png'}`);
}

// Correct MIME types matter: a `.wasm` served as anything but application/wasm makes the streaming
// instantiation fail and the runtime never boots — i.e. a failure for the WRONG reason.
const MIME = {
  '.wasm': 'application/wasm',
  '.js': 'text/javascript',
  '.mjs': 'text/javascript',
  '.json': 'application/json',
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css',
  '.zip': 'application/zip',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8',
  '.md': 'text/markdown; charset=utf-8',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.dat': 'application/octet-stream',
  '.blat': 'application/octet-stream',
  '.pdb': 'application/octet-stream',
};

const server = http.createServer((req, res) => {
  try {
    let urlPath = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    // Strip the base-path prefix (keep a leading slash), mirroring how Pages serves under /<repo>/.
    if (BASE_PATH !== '/' && urlPath.startsWith(BASE_PATH)) {
      urlPath = '/' + urlPath.slice(BASE_PATH.length);
    } else if (BASE_PATH !== '/' && urlPath === BASE_PATH.replace(/\/$/, '')) {
      urlPath = '/';
    } else if (BASE_PATH !== '/') {
      // Anything outside the base path is not part of the app — return a real 404.
      res.statusCode = 404;
      res.end('Not Found');
      return;
    }
    if (urlPath === '/' || urlPath === '') urlPath = '/index.html';

    // Resolve against ROOT as a RELATIVE path (strip leading slashes) so separators stay consistent
    // on every OS. NOTE: path.join/resolve with a leading-slash SECOND arg is a footgun — join keeps
    // ROOT but can mix separators (breaking the traversal guard on Windows), and resolve would drop
    // ROOT entirely. Stripping to a relative path avoids both. NO SPA fallback: a missing file is a
    // real 404 — exactly what reproduces the #1867 `_framework/avalonia.js` 404.
    const rel = path.normalize(urlPath).replace(/^[/\\]+/, '');
    const filePath = path.resolve(ROOT, rel);
    // Path-traversal guard + real 404 for anything missing (NO fallback to index.html).
    if (!(filePath === ROOT || filePath.startsWith(ROOT + path.sep)) || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
      res.statusCode = 404;
      res.end('Not Found');
      return;
    }
    res.setHeader('Content-Type', MIME[path.extname(filePath).toLowerCase()] || 'application/octet-stream');
    if (rel === 'index.html' && BASE_PATH !== '/') {
      const html = fs.readFileSync(filePath, 'utf8')
        .replace('<base href="/" />', `<base href="${BASE_PATH}" />`);
      res.end(html);
      return;
    }
    fs.createReadStream(filePath).pipe(res);
  } catch (e) {
    // Do not leak exception/stack detail to the HTTP client (CodeQL js/stack-trace-exposure);
    // log server-side for debugging and return a generic message.
    console.error('[smoke] request error:', e);
    res.statusCode = 500;
    res.end('Internal Server Error');
  }
});

// ROM bytes are loaded ONCE and reused for every viewport run (loading is a setup concern, not a
// per-viewport concern). A missing SMOKE_ROM file is a hard setup error (exit 2), not a per-run
// failure to aggregate.
function loadRomBytesOnce() {
  if (!ROM_PATH) return null;
  if (ROM_PATH === 'synthetic') {
    const rom = Buffer.alloc(0x1000000);
    Buffer.from('BE8E01', 'ascii').copy(rom, 0xAC);
    return rom;
  }
  if (fs.existsSync(ROM_PATH)) return fs.readFileSync(ROM_PATH);
  console.error(`[smoke] SMOKE_ROM not found: ${ROM_PATH}`);
  process.exit(2);
}
const ROM_BYTES = loadRomBytesOnce();

// Runs the full boot + (optional) ROM/editor-nav smoke flow against a single viewport, in its own
// isolated browser context/page. Returns an array of failure strings, each prefixed with the
// viewport tag so multi-viewport failures are distinguishable in aggregate output.
async function runViewport(browser, vp, url) {
  const tag = `[${vp.width}x${vp.height}/${vp.tag}]`;
  const failures = [];
  const badModuleResponses = [];
  const notSupportedErrors = [];
  const EDITOR_CONTENT_CLIP = { x: 0, y: 80, width: vp.width, height: Math.max(1, vp.height - 80) };

  // Screenshot naming: the owning run (acceptance, or the single explicit-viewport run) keeps the
  // exact SMOKE_SCREENSHOT path (and its .before.png) that .github/workflows/pages.yml uploads
  // today. Any non-owning run (the default matrix's compact pass) gets a derived, collision-free
  // sidecar so it never overwrites the owning run's artifacts.
  const mainPath = vp.owning ? SCREENSHOT : deriveSidecar(SCREENSHOT, vp.tag);
  const beforePath = deriveSidecar(mainPath, 'before');
  const movecostBeforePath = deriveSidecar(mainPath, 'movecost.before');
  const movecostAfterPath = deriveSidecar(mainPath, 'movecost');
  fs.mkdirSync(path.dirname(mainPath) || '.', { recursive: true });

  // #1998 follow-up (review): track whether THIS run has already captured its final mainPath
  // screenshot via one of the normal success/known-failure call sites below. If a run instead fails
  // BEFORE ever reaching an existing screenshot call site (e.g. page.goto() itself times out), the
  // shared `finally` block below makes one best-effort full-viewport capture so the run still leaves
  // visual evidence — without ever suppressing/replacing the ORIGINAL recorded failure reason.
  let mainScreenshotWritten = false;
  // Remove any stale screenshot left over from a PRIOR invocation at this exact path first, so an
  // old (possibly successful) capture can never be mistaken for THIS run's outcome if both the
  // normal path and the fallback below fail to write a fresh one.
  try {
    if (fs.existsSync(mainPath)) fs.unlinkSync(mainPath);
  } catch (e) {
    console.log(`[smoke] ${tag} could not remove stale screenshot at ${mainPath} (non-fatal): ${bounded(e.message)}`);
  }

  const context = await browser.newContext({ viewport: { width: vp.width, height: vp.height }, deviceScaleFactor: 1 });
  const page = await context.newPage();

  // The #1867 tripwire: any Avalonia JS module (avalonia.js OR storage.js) that responds >= 400 or
  // hard-fails to load — both are boot-critical modules published to _framework/ (Copilot review, #1868).
  page.on('response', (resp) => {
    if (/(avalonia|storage)\.js(\?|$)/.test(resp.url()) && resp.status() >= 400) {
      badModuleResponses.push(`${resp.status()} ${resp.url()}`);
    }
  });
  page.on('requestfailed', (req) => {
    if (/(avalonia|storage)\.js(\?|$)/.test(req.url())) {
      badModuleResponses.push(`FAILED ${req.url()} (${req.failure()?.errorText ?? 'unknown'})`);
    }
  });
  // Log — but do NOT fail on — page errors and console.errors (Skia/WebGL emit benign ones during
  // startup). The stack + console.error lines are the fastest way to diagnose a boot crash — e.g. a
  // missing SkiaSharp native throwing SKImageInfo's type initializer (#1867).
  page.on('pageerror', (err) => {
    const text = `${err.message}${err.stack ? '\n' + err.stack : ''}`;
    if (/NotSupportedException|windowing platform|Browser doesn't support windowing platform/i.test(text)) {
      notSupportedErrors.push(text);
    }
    console.log(`[smoke] ${tag} pageerror: ${text}`);
  });
  page.on('console', (msg) => {
    const t = msg.text();
    if (/NotSupportedException|windowing platform|Browser doesn't support windowing platform/i.test(t)) {
      notSupportedErrors.push(t);
    }
    if (t.includes('[FEBuilderGBA]')) console.log(`[smoke] ${tag} app-console: ${t}`);
    else if (msg.type() === 'error') console.log(`[smoke] ${tag} console.error: ${t}`);
  });

  try {
    try {
      await page.goto(url, { waitUntil: 'load', timeout: BOOT_TIMEOUT_MS });
      // Success signal: Avalonia mounted its Skia <canvas> into #out (it APPENDS the canvas rather
      // than replacing #out's content — see the .app-splash removal below), proving the app booted.
      await page.waitForSelector('canvas', { state: 'attached', timeout: BOOT_TIMEOUT_MS });
      console.log(`[smoke] ${tag} canvas mounted — app booted.`);
      // #1869: Avalonia does NOT replace #out's content — it appends its <canvas>, leaving the HTML
      // .app-splash overlaying the app. main.js now removes .app-splash once the canvas mounts;
      // assert it's gone (bounded wait), else the loading spinner sticks over the app.
      try {
        await page.waitForFunction(() => document.querySelectorAll('#out .app-splash').length === 0, undefined, { timeout: 15000 });
        console.log(`[smoke] ${tag} .app-splash removed after boot (#1869).`);
      } catch (e) {
        failures.push(`${tag} .app-splash was NOT removed after the canvas mounted (#1869 — the loading spinner overlays the app): ${e.message}`);
      }

      // #1998: log the effective device-pixel-ratio and visualViewport scale so this run's Map
      // Editor layout metrics can be cross-checked against the actual rendering scale.
      const viewportInfo = await page.evaluate(() => ({
        devicePixelRatio: window.devicePixelRatio,
        visualViewportScale: window.visualViewport ? window.visualViewport.scale : null,
        visualViewportWidth: window.visualViewport ? window.visualViewport.width : null,
        visualViewportHeight: window.visualViewport ? window.visualViewport.height : null,
      }));
      console.log(`[smoke] ${tag} devicePixelRatio=${viewportInfo.devicePixelRatio} ` +
        `visualViewport.scale=${viewportInfo.visualViewportScale} visualViewport=${viewportInfo.visualViewportWidth}x${viewportInfo.visualViewportHeight}`);

      if (ROM_BYTES) {
        // Full-viewport "before" capture — pre-navigation state, no clip, taken right after boot
        // and before any editor is opened.
        await page.screenshot({ path: beforePath });
        console.log(`[smoke] ${tag} launcher (pre-navigation) screenshot -> ${beforePath}`);

        console.log(`[smoke] ${tag} loading ROM fixture -> ${ROM_PATH}`);
        try {
          await page.waitForFunction(() => typeof globalThis.__febTest !== 'undefined', null, { timeout: 5000 });
        } catch {
          failures.push(`${tag} globalThis.__febTest missing — publish the bundle with -p:E2E_HOOKS=true (SMOKE_ROM set but E2E hooks absent)`);
          await page.screenshot({ path: mainPath });
          mainScreenshotWritten = true;
          console.log(`[smoke] ${tag} screenshot -> ${mainPath}`);
          throw new Error('__FEB_E2E_HOOKS_MISSING__');
        }

        // #1998 follow-up: exercise the C# hook's fail-closed "wrong editor" path against the LIVE
        // app, not just source text — at this point only the launcher is showing (no Map Editor is
        // open), so TestHooks.MapEditorLayoutMetrics MUST return a JSON `error` payload rather than
        // `{}` or success-shaped defaults. This proves the hook contract itself, not merely the JS
        // gate below.
        const preNavMetricsRaw = await page.evaluate(() => globalThis.__febTest.MapEditorLayoutMetrics(false));
        const { errors: preNavErrors } = validateMapEditorLayoutMetrics(preNavMetricsRaw, { requireTitle: false });
        if (preNavErrors.length === 0) {
          failures.push(`${tag} MapEditorLayoutMetrics() called before opening the Map Editor did NOT fail closed ` +
            `(expected a rejected/error payload): ${bounded(preNavMetricsRaw)}`);
        } else {
          console.log(`[smoke] ${tag} MapEditorLayoutMetrics() fail-closed pre-navigation probe confirmed (#1998 follow-up): ${bounded(preNavErrors[0])}`);
        }

        const loaded = await page.evaluate(
          async ([s, synthetic]) => await globalThis.__febTest.LoadRomBase64(s, synthetic),
          [ROM_BYTES.toString('base64'), IS_SYNTHETIC_ROM]);
        if (loaded !== true) {
          failures.push(`${tag} LoadRomBase64 returned ${loaded}; expected true`);
        } else {
          console.log(`[smoke] ${tag} ROM loaded through E2E JSExport hook (synthetic=${IS_SYNTHETIC_ROM}).`);
        }

        // Move Cost Editor proof — its own distinct before/after sidecar pair, recomputed clip.
        await page.screenshot({ path: movecostBeforePath, clip: EDITOR_CONTENT_CLIP });
        console.log(`[smoke] ${tag} Move Cost before-screenshot -> ${movecostBeforePath}`);

        const opened = await page.evaluate(() => globalThis.__febTest.OpenEditor('MoveCost'));
        await page.waitForTimeout(1500);
        const cur = await page.evaluate(() => globalThis.__febTest.CurrentEditorTitle());
        const rendered = await page.evaluate(() => globalThis.__febTest.CurrentEditorBodyRendered());
        if (opened !== 'Move Cost Editor') {
          failures.push(`${tag} OpenEditor('MoveCost') returned "${opened}"; expected "Move Cost Editor"`);
        }
        if (cur !== 'Move Cost Editor') {
          failures.push(`${tag} CurrentEditorTitle() returned "${cur}"; expected "Move Cost Editor"`);
        }
        if (rendered !== true) {
          failures.push(`${tag} editor body did not render (Bounds/visual children empty) — CurrentEditorBodyRendered()=${rendered}`);
        }

        await page.screenshot({ path: movecostAfterPath, clip: EDITOR_CONTENT_CLIP });
        console.log(`[smoke] ${tag} Move Cost after-screenshot -> ${movecostAfterPath}`);
        const before = fs.readFileSync(movecostBeforePath);
        const after = fs.readFileSync(movecostAfterPath);
        let changedBytes = 0;
        const comparableLength = Math.min(before.length, after.length);
        for (let i = 0; i < comparableLength; i++) {
          if (before[i] !== after[i]) changedBytes++;
        }
        changedBytes += Math.abs(before.length - after.length);
        if (changedBytes < 1024) {
          failures.push(`${tag} Move Cost editor content screenshot changed only ${changedBytes} byte(s); expected a substantial body-region render delta`);
        } else if (opened === 'Move Cost Editor' && cur === 'Move Cost Editor' && rendered === true) {
          console.log(`[smoke] ${tag} Move Cost Editor opened through real launcher command path (#1888); content-region changed bytes=${changedBytes}.`);
        }

        // #1891: prove a newly-exposed catalog editor (NOT one of the old 9 hardcoded launcher
        // entries) also opens through the real single-view launcher on wasm. AI Script was
        // unreachable on the web app before the full EditorCatalog was wired in.
        const opened2 = await page.evaluate(() => globalThis.__febTest.OpenEditor('AIScript'));
        await page.waitForTimeout(1000);
        if (!opened2) {
          failures.push(`${tag} OpenEditor('AIScript') returned "${opened2}"; a newly-exposed catalog editor failed to open on wasm (#1891)`);
        } else {
          console.log(`[smoke] ${tag} newly-exposed catalog editor opened on wasm (#1891): "${opened2}".`);
        }

        // #1998: open the Map Editor through the real launcher (exact catalog key "MapEditor") and
        // assert the compact/desktop split-scroller layout behaves correctly at THIS run's viewport.
        const openedMap = await page.evaluate(() => globalThis.__febTest.OpenEditor('MapEditor'));
        // Real ROMs decompress/render actual chipset+tileset data, which can take noticeably longer
        // than the synthetic no-op path — give real-ROM runs extra settle time.
        await page.waitForTimeout(IS_SYNTHETIC_ROM ? 1000 : 2000);
        const curMap = await page.evaluate(() => globalThis.__febTest.CurrentEditorTitle());
        const renderedMap = await page.evaluate(() => globalThis.__febTest.CurrentEditorBodyRendered());
        if (openedMap !== 'Visual Map Editor') {
          failures.push(`${tag} OpenEditor('MapEditor') returned "${openedMap}"; expected "Visual Map Editor"`);
        }
        if (curMap !== 'Visual Map Editor') {
          failures.push(`${tag} CurrentEditorTitle() returned "${curMap}" after opening MapEditor; expected "Visual Map Editor"`);
        }
        if (renderedMap !== true) {
          failures.push(`${tag} Map Editor body did not render (Bounds/visual children empty) — CurrentEditorBodyRendered()=${renderedMap}`);
        }

        const metricsRaw = await page.evaluate(
          (injectSynthetic) => globalThis.__febTest.MapEditorLayoutMetrics(injectSynthetic),
          IS_SYNTHETIC_ROM);
        // #1998 follow-up: fail-closed completeness gate — a probe/runtime exception, missing
        // editor, missing named control, or non-finite/negative metric must NEVER be silently
        // treated as "no assertions to run" (the reviewed false-pass class: a real-ROM desktop run
        // with `{}` metrics logged "undefined/undefined" and exited 0). `metrics` is only non-null
        // here when EVERY required field is present, finite, and non-negative, for BOTH synthetic
        // and real-ROM modes.
        const { metrics, errors: metricsErrors } = validateMapEditorLayoutMetrics(metricsRaw);
        failures.push(...metricsErrors.map((e) => `${tag} ${e}`));
        if (metrics) {
          console.log(`[smoke] ${tag} Map Editor layout metrics: ${metricsRaw}`);
          const isCompact = vp.height < MAP_EDITOR_COMPACT_HEIGHT_THRESHOLD;
          if (metrics.mapCanvasHeight < MIN_USABLE_MAP_CANVAS_HEIGHT - 0.5) {
            failures.push(`${tag} Map canvas height (${metrics.mapCanvasHeight}) fell below the ${MIN_USABLE_MAP_CANVAS_HEIGHT}px usable minimum`);
          }
          // The compact-viewport overflow direction is safe to hard-assert universally: at a ~189px
          // upper-controls viewport, ANY realistic info/toolbar/palette/tile-editor content will
          // overflow it, regardless of which ROM/chapter is loaded.
          if (isCompact) {
            if (!(metrics.upperExtentHeight > metrics.upperViewportHeight + 0.5)) {
              failures.push(`${tag} compact viewport expected the Map Editor's upper controls to overflow and scroll ` +
                `(Extent=${metrics.upperExtentHeight}, Viewport=${metrics.upperViewportHeight})`);
            } else {
              console.log(`[smoke] ${tag} Map Editor compact-viewport upper-controls overflow confirmed (#1998).`);
            }
            // #1998 follow-up (horizontal axis): the pre-existing coverage above only proved the
            // upper controls scroller overflows/scrolls VERTICALLY. At the actual 600px-wide compact
            // smoke viewport the toolbar/info rows are also demonstrably wider than the viewport
            // (measured ~910px content vs a ~342px viewport), so this axis is hard-asserted for
            // BOTH synthetic and real-ROM runs — unlike the height axis above, toolbar/button
            // layout width does not depend on which ROM/chapter data is loaded.
            if (!(metrics.upperExtentWidth > metrics.upperViewportWidth + 0.5)) {
              failures.push(`${tag} compact viewport expected the Map Editor's upper controls to overflow and scroll ` +
                `horizontally (ExtentWidth=${metrics.upperExtentWidth}, ViewportWidth=${metrics.upperViewportWidth})`);
            } else {
              console.log(`[smoke] ${tag} Map Editor compact-viewport upper-controls horizontal overflow confirmed (#1998 follow-up).`);
            }
          } else if (IS_SYNTHETIC_ROM) {
            // The desktop "must fit without scrolling" direction is data-dependent: a real ROM's
            // populated palette/terrain lists can genuinely need MORE upper-region height than a
            // given desktop viewport provides (observed: a real FE8U chapter needed ~613px of
            // upper-controls content at 1920x852, exceeding its 541px viewport) — that is the
            // split-scroller correctly falling back to scrolling, not a layout bug. Hard-assert the
            // "fits naturally" expectation only for the synthetic ROM, whose fixture content is
            // deliberately sized to match this exact assumption; for a real ROM, log the same
            // metrics instead (see the `else` branch below).
            if (!(metrics.upperExtentHeight <= metrics.upperViewportHeight + 0.5)) {
              failures.push(`${tag} acceptance-size viewport expected the Map Editor's upper controls to fit ` +
                `without scrolling (Extent=${metrics.upperExtentHeight}, Viewport=${metrics.upperViewportHeight})`);
            } else {
              console.log(`[smoke] ${tag} Map Editor acceptance-size natural layout confirmed (#1998).`);
            }
          } else {
            console.log(`[smoke] ${tag} real-ROM acceptance-size upper-controls extent/viewport (logged, not hard-asserted): ` +
              `${metrics.upperExtentHeight}/${metrics.upperViewportHeight}`);
          }
          // Both-axis inner-canvas overflow: hard-asserted ONLY for the synthetic ROM (its map
          // image is deliberately oversized to guarantee overflow on both axes at any viewport up
          // to 1920x852). For a REAL ROM, a chapter map's on-screen size is ROM-data-dependent
          // (many FE7/FE8 maps are narrower than 1920px), so overflow is data-driven, not a layout
          // bug — the same metrics are logged above for manual/CI-log inspection instead of
          // hard-failing on content size.
          if (IS_SYNTHETIC_ROM) {
            if (!(metrics.canvasExtentWidth > metrics.canvasViewportWidth + 0.5)) {
              failures.push(`${tag} synthetic ROM expected the map canvas to overflow horizontally ` +
                `(ExtentWidth=${metrics.canvasExtentWidth}, ViewportWidth=${metrics.canvasViewportWidth})`);
            }
            if (!(metrics.canvasExtentHeight > metrics.canvasViewportHeight + 0.5)) {
              failures.push(`${tag} synthetic ROM expected the map canvas to overflow vertically ` +
                `(ExtentHeight=${metrics.canvasExtentHeight}, ViewportHeight=${metrics.canvasViewportHeight})`);
            }
            if (metrics.canvasExtentWidth > metrics.canvasViewportWidth + 0.5 && metrics.canvasExtentHeight > metrics.canvasViewportHeight + 0.5) {
              console.log(`[smoke] ${tag} synthetic ROM both-axis inner-canvas overflow confirmed (#1998 follow-up).`);
            }
          } else {
            console.log(`[smoke] ${tag} real-ROM inner-canvas extent/viewport (logged, not hard-asserted): ` +
              `width ${metrics.canvasExtentWidth}/${metrics.canvasViewportWidth}, height ${metrics.canvasExtentHeight}/${metrics.canvasViewportHeight}`);
          }
        }

        // Final screenshot: the Map Editor, full viewport, NO content clip — exactly vp.width x
        // vp.height at DPR 1.
        await page.screenshot({ path: mainPath });
        mainScreenshotWritten = true;
        console.log(`[smoke] ${tag} final full-viewport Map Editor screenshot -> ${mainPath}`);

        // #1998 follow-up (review): live regression proving stale synthetic authorization is
        // revoked on a NEW load attempt. Runs ONLY for the synthetic-ROM run, and ONLY AFTER this
        // run's own MapEditor assertions/screenshot are already captured above, so it can never
        // mutate accepted evidence. Attempts a deliberately-invalid non-synthetic reload (garbage
        // bytes — NOT the synthetic fixture) in the SAME page/context that already holds a granted
        // synthetic authorization from the successful load above; LoadRomBase64 must return false
        // (the reload itself fails/is rejected), and a subsequent synthetic-injection request MUST
        // then be rejected with a hook `error` payload — proving the earlier `true` authorization
        // did NOT survive the failed reload attempt (TestHooks.LoadRomBase64 revokes authorization
        // at the very start of every call, before decode/load/initialize/refresh, and re-grants it
        // only after the full sequence succeeds).
        if (IS_SYNTHETIC_ROM) {
          const garbageBase64 = Buffer.from('not a real GBA rom, deliberately invalid').toString('base64');
          const reloadOk = await page.evaluate(
            async (b64) => await globalThis.__febTest.LoadRomBase64(b64, false),
            garbageBase64);
          if (reloadOk !== false) {
            failures.push(`${tag} stale-authorization probe: deliberately-invalid non-synthetic reload ` +
              `unexpectedly returned ${reloadOk}; expected false`);
          } else {
            const postReloadMetricsRaw = await page.evaluate(() => globalThis.__febTest.MapEditorLayoutMetrics(true));
            const { errors: postReloadErrors } = validateMapEditorLayoutMetrics(postReloadMetricsRaw, { requireTitle: false });
            if (postReloadErrors.length === 0) {
              failures.push(`${tag} stale-authorization probe FAILED: synthetic injection was still authorized ` +
                `after a failed non-synthetic reload attempt (expected a rejected/error payload): ${bounded(postReloadMetricsRaw)}`);
            } else {
              console.log(`[smoke] ${tag} stale synthetic authorization correctly revoked after a failed reload ` +
                `(#1998 follow-up): ${bounded(postReloadErrors[0])}`);
            }
          }
        }
      } else {
        console.log(`[smoke] ${tag} SMOKE_ROM not set; boot-only smoke — taking a single full-viewport screenshot.`);
        await page.screenshot({ path: mainPath });
        mainScreenshotWritten = true;
        console.log(`[smoke] ${tag} screenshot -> ${mainPath}`);
      }
    } catch (e) {
      if (e.message !== '__FEB_E2E_HOOKS_MISSING__') {
        failures.push(`${tag} smoke flow failed: ${e.message}`);
      }
    }
  } finally {
    if (badModuleResponses.length) {
      failures.push(`${tag} Avalonia JS module(s) not served OK: ${badModuleResponses.join('; ')}`);
    }
    if (notSupportedErrors.length) {
      failures.push(`${tag} Unexpected NotSupportedException/windowing-platform error(s): ${notSupportedErrors.join('\n---\n')}`);
    }
    // #1998 follow-up (review): if no normal code path above ever captured mainPath (e.g. page.goto
    // itself timed out, or any other exception fired before the first screenshot call site above),
    // make a best-effort full-viewport capture here so this run still leaves visual failure
    // evidence. A fallback-capture failure is logged but NEVER pushed into `failures` — it must
    // never hide or replace the ORIGINAL recorded failure reason.
    if (!mainScreenshotWritten) {
      try {
        await page.screenshot({ path: mainPath });
        console.log(`[smoke] ${tag} best-effort fallback screenshot captured after failure -> ${mainPath}`);
      } catch (e) {
        console.log(`[smoke] ${tag} fallback screenshot capture FAILED (original failure evidence above still stands): ${bounded(e.message)}`);
      }
    }
    await page.close().catch(() => {});
    await context.close().catch(() => {});
  }

  return failures;
}

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const url = `http://127.0.0.1:${port}${BASE_PATH}?e2e=1`;
console.log(`[smoke] serving ${ROOT} at ${url} (boot timeout ${BOOT_TIMEOUT_MS} ms)`);
console.log(`[smoke] viewport plan: ${VIEWPORT_PLAN.map((v) => `${v.width}x${v.height}(${v.tag})`).join(', ')}`);

const allFailures = [];
const browser = await chromium.launch({ args: ['--no-sandbox'] });
try {
  for (const vp of VIEWPORT_PLAN) {
    const runFailures = await runViewport(browser, vp, url);
    allFailures.push(...runFailures);
  }
} finally {
  await browser.close().catch(() => {});
  await new Promise((resolve) => server.close(resolve));
}

if (allFailures.length) {
  console.error('[smoke] FAIL:\n - ' + allFailures.join('\n - '));
  process.exit(1);
}
console.log('[smoke] PASS — Avalonia canvas mounted and avalonia.js served OK for all configured viewport(s).');
process.exit(0);
