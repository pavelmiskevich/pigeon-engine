// Размер публикации WASM-харнесса: исходные файлы и их gzip/brotli-версии, которые создаёт
// dotnet publish. Печатает Markdown для сводки CI — ориентир для бюджета B1 (browser-runtime.md §8).
//
//   node publish-size.mjs <путь к wwwroot>

import { readdir, stat } from 'node:fs/promises';
import { join } from 'node:path';

const root = process.argv[2];
if (!root) {
  console.error('Укажите путь к wwwroot.');
  process.exit(2);
}

async function* walk(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      yield* walk(path);
    } else {
      yield path;
    }
  }
}

const totals = { raw: 0, gzip: 0, brotli: 0 };
for await (const path of walk(root)) {
  const { size } = await stat(path);
  if (path.endsWith('.br')) {
    totals.brotli += size;
  } else if (path.endsWith('.gz')) {
    totals.gzip += size;
  } else {
    totals.raw += size;
  }
}

const kib = (bytes) => `${Math.round(bytes / 1024)} KiB`;
console.log('### Размер публикации ядра в WASM');
console.log('');
console.log('| Исходный | gzip | brotli |');
console.log('|---------:|-----:|-------:|');
console.log(`| ${kib(totals.raw)} | ${kib(totals.gzip)} | ${kib(totals.brotli)} |`);
