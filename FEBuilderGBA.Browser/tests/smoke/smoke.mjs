// Headless-browser boot smoke test for the FEBuilderGBA WebAssembly app (issue #1867).
//
// WHY THIS EXISTS: #1867 shipped a web app that returned HTTP 200 for every static asset yet never
// actually booted — Avalonia's runtime requested `_framework/avalonia.js` (404) and the page hung on
// the loading splash forever. HTTP-200 checks structurally cannot catch that class of bug; only a
// real browser that executes the wasm runtime can. This test:
//   1. serves the published AppBundle under the GitHub Pages sub-path (default `/FEBuilderGBA/`),
//      with correct MIME types and REAL 404s (no SPA fallback that would mask a missing asset);
//   2. loads it in headless Chromium (Playwright);
//   3. FAILS if any `avalonia.js` request returns >= 400 (the exact #1867 symptom), or if the
//      Avalonia canvas never mounts (the app didn't render);
//   4. always writes a screenshot for PR proof / debugging.
//
// Env:
//   SMOKE_WWWROOT     (required) absolute path to the published `.../publish/wwwroot`.
//   SMOKE_BASE_PATH   (default `/FEBuilderGBA/`) the sub-path to serve under — mirror the deployed
//                     <base href> so base-relative resolution is exercised the same way as prod.
//   SMOKE_SCREENSHOT  (default `web-smoke.png`) screenshot output path.
//   SMOKE_TIMEOUT_MS  (default 120000) boot timeout — cold wasm + ~6.8 MB config.zip is slow.
//   SMOKE_VIEWPORT_WIDTH  (default 1280) browser viewport width (#1998).
//   SMOKE_VIEWPORT_HEIGHT (default 800)  browser viewport height (#1998) — below
//                         MAP_EDITOR_COMPACT_HEIGHT_THRESHOLD this exercises the Map Editor's
//                         compact (upper-controls-scroll) path; at/above it, the normal desktop
//                         (acceptance) path. Run the script twice (e.g. a compact height and the
//                         1920x852 acceptance size) to exercise both.
//   SMOKE_ROM         (optional) ROM fixture path, or `synthetic` to generate a license-clean FE8U
//                     header-only ROM. When present, the smoke test loads it through the E2E-only
//                     JSExport hook, opens Move Cost Editor through the real launcher delegate, and
//                     asserts the embeddable editor page renders (real #1873 single-view proof).
//                     It also opens the Map Editor (#1998) and asserts its compact/desktop layout
//                     split behaves correctly at the configured viewport size.

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const WWWROOT = process.env.SMOKE_WWWROOT;
const BASE_PATH = process.env.SMOKE_BASE_PATH || '/FEBuilderGBA/';
const SCREENSHOT = process.env.SMOKE_SCREENSHOT || 'web-smoke.png';
const BOOT_TIMEOUT_MS = Number(process.env.SMOKE_TIMEOUT_MS || 120000);
const ROM_PATH = process.env.SMOKE_ROM;

if (!WWWROOT || !fs.existsSync(WWWROOT)) {
  console.error(`[smoke] SMOKE_WWWROOT not found: ${WWWROOT}`);
  process.exit(2);
}
const ROOT = path.resolve(WWWROOT);
// #1998: viewport size is configurable so the same script can exercise both an "acceptance"
// (ample-height desktop) layout and a "compact" (short browser viewport) layout for the Map
// Editor split-scroller behavior — see the MAP_EDITOR_COMPACT_HEIGHT_THRESHOLD comment below.
const VIEWPORT_WIDTH = Number(process.env.SMOKE_VIEWPORT_WIDTH || 1280);
const VIEWPORT_HEIGHT = Number(process.env.SMOKE_VIEWPORT_HEIGHT || 800);
const EDITOR_CONTENT_CLIP = { x: 0, y: 80, width: VIEWPORT_WIDTH, height: VIEWPORT_HEIGHT - 80 };
// Below this viewport height, the Map Editor's upper controls are expected to overflow and
// scroll (compact path); at/above it, the normal desktop layout is expected to fit without
// scrolling (acceptance path). 700 sits between the compact fixtures used in CI (e.g. 500-600)
// and the editor's own natural desktop height (800), matching MapEditorButtonReadabilityTests.
const MAP_EDITOR_COMPACT_HEIGHT_THRESHOLD = 700;

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

const failures = [];
const badModuleResponses = [];
const notSupportedErrors = [];
let rom = null;

function smokeRomBytes() {
  if (!ROM_PATH) return null;
  if (ROM_PATH === 'synthetic') {
    const rom = Buffer.alloc(0x1000000);
    Buffer.from('BE8E01', 'ascii').copy(rom, 0xAC);
    return rom;
  }
  if (fs.existsSync(ROM_PATH)) return fs.readFileSync(ROM_PATH);
  failures.push(`SMOKE_ROM not found: ${ROM_PATH}`);
  return null;
}

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const url = `http://127.0.0.1:${port}${BASE_PATH}?e2e=1`;
console.log(`[smoke] serving ${ROOT} at ${url} (boot timeout ${BOOT_TIMEOUT_MS} ms)`);

const browser = await chromium.launch({ args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: VIEWPORT_WIDTH, height: VIEWPORT_HEIGHT } });
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
  console.log(`[smoke] pageerror: ${text}`);
});
page.on('console', (msg) => {
  const t = msg.text();
  if (/NotSupportedException|windowing platform|Browser doesn't support windowing platform/i.test(t)) {
    notSupportedErrors.push(t);
  }
  if (t.includes('[FEBuilderGBA]')) console.log(`[smoke] app-console: ${t}`);
  else if (msg.type() === 'error') console.log(`[smoke] console.error: ${t}`);
});

try {
  await page.goto(url, { waitUntil: 'load', timeout: BOOT_TIMEOUT_MS });
  // Success signal: Avalonia mounted its Skia <canvas> into #out (it APPENDS the canvas rather than
  // replacing #out's content — see the .app-splash removal below), proving the app actually rendered.
  await page.waitForSelector('canvas', { state: 'attached', timeout: BOOT_TIMEOUT_MS });
  console.log('[smoke] canvas mounted — app booted.');
  // #1869: Avalonia does NOT replace #out's content — it appends its <canvas>, leaving the HTML
  // .app-splash overlaying the app. main.js now removes .app-splash once the canvas mounts; assert
  // it's gone (give the MutationObserver a beat), else the loading spinner sticks over the app.
  // Wait (bounded) for the splash to be removed rather than a fixed sleep — resolves as soon as it's
  // gone, and fails fast if it never is. #1869.
  try {
    await page.waitForFunction(() => document.querySelectorAll('#out .app-splash').length === 0, undefined, { timeout: 15000 });
    console.log('[smoke] .app-splash removed after boot (#1869).');
  } catch (e) {
    failures.push(`.app-splash was NOT removed after the canvas mounted (#1869 — the loading spinner overlays the app): ${e.message}`);
  }

  // #1998: log the effective device-pixel-ratio and visualViewport scale so a CI run's Map Editor
  // layout metrics can be cross-checked against the actual rendering scale at this viewport size.
  const viewportInfo = await page.evaluate(() => ({
    devicePixelRatio: window.devicePixelRatio,
    visualViewportScale: window.visualViewport ? window.visualViewport.scale : null,
    visualViewportWidth: window.visualViewport ? window.visualViewport.width : null,
    visualViewportHeight: window.visualViewport ? window.visualViewport.height : null,
  }));
  console.log(`[smoke] viewport=${VIEWPORT_WIDTH}x${VIEWPORT_HEIGHT} devicePixelRatio=${viewportInfo.devicePixelRatio} ` +
    `visualViewport.scale=${viewportInfo.visualViewportScale} visualViewport=${viewportInfo.visualViewportWidth}x${viewportInfo.visualViewportHeight}`);

  rom = smokeRomBytes();
  if (ROM_PATH && !rom) {
    failures.push('SMOKE_ROM was set but no ROM bytes were available; fix SMOKE_ROM (use a valid path or "synthetic") before running the editor-nav smoke.');
  }
  if (rom) {
    try {
      console.log(`[smoke] loading ROM fixture -> ${ROM_PATH}`);
      try {
        await page.waitForFunction(() => typeof globalThis.__febTest !== 'undefined', null, { timeout: 5000 });
      } catch {
        failures.push('globalThis.__febTest missing — publish the bundle with -p:E2E_HOOKS=true (SMOKE_ROM set but E2E hooks absent)');
        await page.screenshot({ path: SCREENSHOT });
        console.log(`[smoke] screenshot -> ${SCREENSHOT}`);
        throw new Error('__FEB_E2E_HOOKS_MISSING__');
      }

      const loaded = await page.evaluate(async (s) => await globalThis.__febTest.LoadRomBase64(s), rom.toString('base64'));
      if (loaded !== true) {
        failures.push(`LoadRomBase64 returned ${loaded}; expected true`);
      } else {
        console.log('[smoke] ROM loaded through E2E JSExport hook.');
      }

      const parsed = path.parse(SCREENSHOT);
      const beforeScreenshot = path.join(parsed.dir || '.', `${parsed.name}.before${parsed.ext || '.png'}`);
      fs.mkdirSync(path.dirname(beforeScreenshot), { recursive: true });
      fs.mkdirSync(path.dirname(SCREENSHOT), { recursive: true });
      await page.screenshot({ path: beforeScreenshot, clip: EDITOR_CONTENT_CLIP });
      console.log(`[smoke] launcher content screenshot -> ${beforeScreenshot}`);

      const opened = await page.evaluate(() => globalThis.__febTest.OpenEditor('MoveCost'));
      await page.waitForTimeout(1500);
      const cur = await page.evaluate(() => globalThis.__febTest.CurrentEditorTitle());
      const rendered = await page.evaluate(() => globalThis.__febTest.CurrentEditorBodyRendered());
      if (opened !== 'Move Cost Editor') {
        failures.push(`OpenEditor('MoveCost') returned "${opened}"; expected "Move Cost Editor"`);
      }
      if (cur !== 'Move Cost Editor') {
        failures.push(`CurrentEditorTitle() returned "${cur}"; expected "Move Cost Editor"`);
      }
      if (rendered !== true) {
        failures.push(`editor body did not render (Bounds/visual children empty) — CurrentEditorBodyRendered()=${rendered}`);
      }

      await page.screenshot({ path: SCREENSHOT, clip: EDITOR_CONTENT_CLIP });
      console.log(`[smoke] editor content screenshot -> ${SCREENSHOT}`);
      const before = fs.readFileSync(beforeScreenshot);
      const after = fs.readFileSync(SCREENSHOT);
      let changedBytes = 0;
      const comparableLength = Math.min(before.length, after.length);
      for (let i = 0; i < comparableLength; i++) {
        if (before[i] !== after[i]) changedBytes++;
      }
      changedBytes += Math.abs(before.length - after.length);
      if (changedBytes < 1024) {
        failures.push(`editor content screenshot changed only ${changedBytes} byte(s); expected a substantial body-region render delta`);
      }
      if (opened === 'Move Cost Editor' && cur === 'Move Cost Editor' && rendered === true) {
        console.log('[smoke] Move Cost Editor opened through real launcher command path (#1888).');
        console.log(`[smoke] editor body rendered; content-region changed bytes=${changedBytes}.`);
      }

      // #1891: prove a newly-exposed catalog editor (NOT one of the old 9 hardcoded launcher
      // entries) also opens through the real single-view launcher on wasm. AI Script was
      // unreachable on the web app before the full EditorCatalog was wired in.
      const opened2 = await page.evaluate(() => globalThis.__febTest.OpenEditor('AIScript'));
      await page.waitForTimeout(1000);
      if (!opened2) {
        failures.push(`OpenEditor('AIScript') returned "${opened2}"; a newly-exposed catalog editor failed to open on wasm (#1891)`);
      } else {
        console.log(`[smoke] newly-exposed catalog editor opened on wasm (#1891): "${opened2}".`);
      }

      // #1998: open the Map Editor through the real launcher (exact catalog key "MapEditor") and
      // assert the compact/desktop split-scroller layout behaves correctly at THIS run's viewport.
      const openedMap = await page.evaluate(() => globalThis.__febTest.OpenEditor('MapEditor'));
      await page.waitForTimeout(1000);
      const curMap = await page.evaluate(() => globalThis.__febTest.CurrentEditorTitle());
      const renderedMap = await page.evaluate(() => globalThis.__febTest.CurrentEditorBodyRendered());
      if (openedMap !== 'Visual Map Editor') {
        failures.push(`OpenEditor('MapEditor') returned "${openedMap}"; expected "Visual Map Editor"`);
      }
      if (curMap !== 'Visual Map Editor') {
        failures.push(`CurrentEditorTitle() returned "${curMap}" after opening MapEditor; expected "Visual Map Editor"`);
      }
      if (renderedMap !== true) {
        failures.push(`Map Editor body did not render (Bounds/visual children empty) — CurrentEditorBodyRendered()=${renderedMap}`);
      }

      const metricsRaw = await page.evaluate(() => globalThis.__febTest.MapEditorLayoutMetrics());
      let metrics = null;
      try {
        metrics = JSON.parse(metricsRaw);
      } catch (e) {
        failures.push(`MapEditorLayoutMetrics() returned non-JSON: ${metricsRaw}`);
      }
      if (metrics) {
        console.log(`[smoke] Map Editor layout metrics @ ${VIEWPORT_WIDTH}x${VIEWPORT_HEIGHT}: ${metricsRaw}`);
        const isCompact = VIEWPORT_HEIGHT < MAP_EDITOR_COMPACT_HEIGHT_THRESHOLD;
        const MIN_USABLE_MAP_CANVAS_HEIGHT = 240;
        if (metrics.mapCanvasHeight < MIN_USABLE_MAP_CANVAS_HEIGHT - 0.5) {
          failures.push(`Map canvas height (${metrics.mapCanvasHeight}) fell below the ${MIN_USABLE_MAP_CANVAS_HEIGHT}px usable minimum at viewport ${VIEWPORT_WIDTH}x${VIEWPORT_HEIGHT}`);
        }
        if (isCompact) {
          if (!(metrics.upperExtentHeight > metrics.upperViewportHeight + 0.5)) {
            failures.push(`Compact viewport (${VIEWPORT_WIDTH}x${VIEWPORT_HEIGHT}) expected the Map Editor's upper controls to overflow and scroll ` +
              `(Extent=${metrics.upperExtentHeight}, Viewport=${metrics.upperViewportHeight})`);
          } else {
            console.log('[smoke] Map Editor compact-viewport upper-controls overflow confirmed (#1998).');
          }
        } else {
          if (!(metrics.upperExtentHeight <= metrics.upperViewportHeight + 0.5)) {
            failures.push(`Acceptance-size viewport (${VIEWPORT_WIDTH}x${VIEWPORT_HEIGHT}) expected the Map Editor's upper controls to fit ` +
              `without scrolling (Extent=${metrics.upperExtentHeight}, Viewport=${metrics.upperViewportHeight})`);
          } else {
            console.log('[smoke] Map Editor acceptance-size natural layout confirmed (#1998).');
          }
        }
      }
    } catch (e) {
      if (e.message !== '__FEB_E2E_HOOKS_MISSING__') {
        failures.push(`Move Cost Editor wasm proof failed with SMOKE_ROM=${ROM_PATH}: ${e.message}`);
      }
    }
  } else {
    console.log(`[smoke] SMOKE_ROM not set or missing; skipping Move Cost Editor single-view proof.`);
  }
} catch (e) {
  failures.push(`app did not render a canvas within ${BOOT_TIMEOUT_MS} ms: ${e.message}`);
}

if (!rom) {
  try {
    fs.mkdirSync(path.dirname(SCREENSHOT), { recursive: true });
    await page.screenshot({ path: SCREENSHOT });
    console.log(`[smoke] screenshot -> ${SCREENSHOT}`);
  } catch (e) {
    console.log(`[smoke] screenshot failed (non-fatal): ${e.message}`);
  }
}

if (badModuleResponses.length) {
  failures.push(`Avalonia JS module(s) not served OK: ${badModuleResponses.join('; ')}`);
}
if (notSupportedErrors.length) {
  failures.push(`Unexpected NotSupportedException/windowing-platform error(s): ${notSupportedErrors.join('\n---\n')}`);
}

await browser.close();
await new Promise((resolve) => server.close(resolve));

if (failures.length) {
  console.error('[smoke] FAIL:\n - ' + failures.join('\n - '));
  process.exit(1);
}
console.log('[smoke] PASS — Avalonia canvas mounted and avalonia.js served OK.');
process.exit(0);
