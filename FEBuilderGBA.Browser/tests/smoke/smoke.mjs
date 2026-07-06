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

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';

const WWWROOT = process.env.SMOKE_WWWROOT;
const BASE_PATH = process.env.SMOKE_BASE_PATH || '/FEBuilderGBA/';
const SCREENSHOT = process.env.SMOKE_SCREENSHOT || 'web-smoke.png';
const BOOT_TIMEOUT_MS = Number(process.env.SMOKE_TIMEOUT_MS || 120000);

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

    const filePath = path.join(ROOT, path.normalize(urlPath));
    // Path-traversal guard + real 404 for anything missing (NO fallback to index.html).
    if (!filePath.startsWith(ROOT + path.sep) || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
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
const badAvaloniaResponses = [];

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const url = `http://127.0.0.1:${port}${BASE_PATH}`;
console.log(`[smoke] serving ${ROOT} at ${url} (boot timeout ${BOOT_TIMEOUT_MS} ms)`);

const browser = await chromium.launch({ args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
const page = await context.newPage();

// The #1867 tripwire: any avalonia.js response >= 400, or a hard request failure.
page.on('response', (resp) => {
  if (/avalonia\.js(\?|$)/.test(resp.url()) && resp.status() >= 400) {
    badAvaloniaResponses.push(`${resp.status()} ${resp.url()}`);
  }
});
page.on('requestfailed', (req) => {
  if (/avalonia\.js(\?|$)/.test(req.url())) {
    badAvaloniaResponses.push(`FAILED ${req.url()} (${req.failure()?.errorText ?? 'unknown'})`);
  }
});
// Log — but do NOT fail on — page errors: Skia/WebGL emit benign console.errors during startup.
page.on('pageerror', (err) => console.log(`[smoke] pageerror: ${err.message}`));
page.on('console', (msg) => {
  const t = msg.text();
  if (t.includes('[FEBuilderGBA]')) console.log(`[smoke] app-console: ${t}`);
});

try {
  await page.goto(url, { waitUntil: 'load', timeout: BOOT_TIMEOUT_MS });
  // Success signal: Avalonia mounted its Skia <canvas> (the splash lives in #out and is replaced on
  // boot, so a canvas existing proves the app actually rendered — not merely that assets loaded).
  await page.waitForSelector('canvas', { state: 'attached', timeout: BOOT_TIMEOUT_MS });
  console.log('[smoke] canvas mounted — app booted.');
} catch (e) {
  failures.push(`app did not render a canvas within ${BOOT_TIMEOUT_MS} ms: ${e.message}`);
}

try {
  await page.screenshot({ path: SCREENSHOT });
  console.log(`[smoke] screenshot -> ${SCREENSHOT}`);
} catch (e) {
  console.log(`[smoke] screenshot failed (non-fatal): ${e.message}`);
}

if (badAvaloniaResponses.length) {
  failures.push(`avalonia.js was not served OK: ${badAvaloniaResponses.join('; ')}`);
}

await browser.close();
await new Promise((resolve) => server.close(resolve));

if (failures.length) {
  console.error('[smoke] FAIL:\n - ' + failures.join('\n - '));
  process.exit(1);
}
console.log('[smoke] PASS — Avalonia canvas mounted and avalonia.js served OK.');
process.exit(0);
