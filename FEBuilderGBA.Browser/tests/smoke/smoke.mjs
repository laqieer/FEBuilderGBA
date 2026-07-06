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
//   SMOKE_ROM         (optional) ROM fixture path. When present, the smoke test loads it through
//                     the single-view Open ROM flow, opens Move Cost Editor, and asserts the
//                     embeddable editor page renders (real #1873 single-view proof).

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
    fs.createReadStream(filePath).pipe(res);
  } catch (e) {
    res.statusCode = 500;
    res.end(String(e));
  }
});

const failures = [];
const badModuleResponses = [];
const notSupportedErrors = [];

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const url = `http://127.0.0.1:${port}${BASE_PATH}`;
console.log(`[smoke] serving ${ROOT} at ${url} (boot timeout ${BOOT_TIMEOUT_MS} ms)`);

const browser = await chromium.launch({ args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
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

  if (ROM_PATH && fs.existsSync(ROM_PATH)) {
    try {
      console.log(`[smoke] loading ROM fixture -> ${ROM_PATH}`);
      const fileChooserPromise = page.waitForEvent('filechooser', { timeout: 15000 });
      await page.getByRole('button', { name: /Open ROM/i }).click();
      const chooser = await fileChooserPromise;
      await chooser.setFiles(ROM_PATH);

      await page.getByText(/Loaded:/i).waitFor({ state: 'visible', timeout: BOOT_TIMEOUT_MS });
      console.log('[smoke] ROM loaded through single-view shell.');

      await page.getByRole('button', { name: /Move Cost Editor/i }).click();
      await page.locator('[data-automation-id="MoveCostEditor_Class_List"], [automation-id="MoveCostEditor_Class_List"], :text("Terrain Move Costs")')
        .first()
        .waitFor({ state: 'visible', timeout: BOOT_TIMEOUT_MS });
      console.log('[smoke] Move Cost Editor rendered in single-view host (#1873).');
    } catch (e) {
      failures.push(`Move Cost Editor single-view proof failed with SMOKE_ROM=${ROM_PATH}: ${e.message}`);
    }
  } else {
    console.log(`[smoke] SMOKE_ROM not set or missing; skipping Move Cost Editor single-view proof.`);
  }
} catch (e) {
  failures.push(`app did not render a canvas within ${BOOT_TIMEOUT_MS} ms: ${e.message}`);
}

try {
  await page.screenshot({ path: SCREENSHOT });
  console.log(`[smoke] screenshot -> ${SCREENSHOT}`);
} catch (e) {
  console.log(`[smoke] screenshot failed (non-fatal): ${e.message}`);
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
