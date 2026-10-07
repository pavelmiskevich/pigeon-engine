// Запускает опубликованный Pigeon.Wasm.Harness в headless Chrome и проверяет отчёт сверки
// эталонных хешей (MVP-0, S0.7; ADR-0016). Код выхода 0 — все хеши совпали.
//
//   node run.mjs [путь к wwwroot]
//
// Браузер — установленный в системе Chrome (playwright-core его не скачивает). Канал можно
// сменить переменной WASM_CHECK_CHANNEL (например, msedge).

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { chromium } from 'playwright-core';

const root = resolve(process.argv[2] ?? '../../artifacts/wasm-harness/wwwroot');
const channel = process.env.WASM_CHECK_CHANNEL ?? 'chrome';
const timeoutMs = 120_000;

const mimeTypes = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.wasm': 'application/wasm',
  '.dat': 'application/octet-stream',
  '.blat': 'application/octet-stream',
};

const server = createServer(async (request, response) => {
  try {
    const urlPath = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const relative = normalize(urlPath === '/' ? '/index.html' : urlPath);
    const filePath = join(root, relative);
    if (!filePath.startsWith(root + sep)) {
      response.writeHead(403).end();
      return;
    }

    if (!(await stat(filePath)).isFile()) {
      response.writeHead(404).end();
      return;
    }

    response.writeHead(200, {
      'Content-Type': mimeTypes[extname(filePath)] ?? 'application/octet-stream',
      'Cache-Control': 'no-store',
    });
    response.end(await readFile(filePath));
  } catch {
    response.writeHead(404).end();
  }
});

await new Promise((done) => server.listen(0, '127.0.0.1', done));
const url = `http://127.0.0.1:${server.address().port}/`;

const browser = await chromium.launch({ channel, headless: true });
let exitCode = 1;
try {
  const page = await browser.newPage();
  const browserLog = [];
  page.on('console', (message) => browserLog.push(`[console.${message.type()}] ${message.text()}`));
  page.on('pageerror', (error) => browserLog.push(`[pageerror] ${error.message}`));

  await page.goto(url);
  await page.waitForFunction(() => document.body.dataset.status !== 'running', null, { timeout: timeoutMs });

  const status = await page.evaluate(() => document.body.dataset.status);
  const report = await page.textContent('#result');
  const version = browser.version();

  console.log(`browser: ${channel} ${version}`);
  console.log(report.trim());
  if (status !== 'pass') {
    console.log(browserLog.join('\n'));
  }

  exitCode = status === 'pass' ? 0 : 1;
} finally {
  await browser.close();
  server.close();
}

process.exit(exitCode);
