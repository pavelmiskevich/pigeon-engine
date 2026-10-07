# ADR-0014 — Browser runtime: ядро в .NET WASM + рендер на TypeScript

- **Статус:** Proposed (ограничения на ядро действуют с MVP-1; выбор рендера — после прототипа B1)
- **Дата:** 2026-09-30

## Контекст

Варианты разобраны в [browser-runtime.md](../research/browser-runtime.md): .NET WASM + TS-рендер,
тонкий клиент, Unity, Godot (исключён: нет веб-экспорта C#), C#-движки с веб-таргетом.

## Решение

- Ядро (Core, AI, Simulation, World.City, генератор) исполняется в браузере на .NET WebAssembly
  (`wasmbrowser`, JSImport/JSExport) и на сервере — одни и те же сборки.
- Рендер — TypeScript за слоем `RenderBridge`, основной кандидат Babylon.js (WebGPU с откатом на
  WebGL2).
- Сервер ASP.NET Core авторитетен для общего состояния; свой голубь предсказывается на клиенте.

**Ограничения на ядро с MVP-1:** trim/AOT-safe; без файловой системы, сети, потоков и
`DateTime.Now` напрямую; без DI-контейнера внутри; генератор на целочисленной арифметике;
CI-джоба «ядро в WASM» с проверкой хешей детерминизма.

## Последствия

- Точки выхода: AOT, Web Worker, перенос NPC на сервер; замена рендера через `RenderBridge`;
  пересмотр Unity после 6.8.
- Модель управления в Browser решена (Q10): голубь автономен.

## Проверка B0 (S0.7)

Ограничение на ядро подтверждено; выбор рендера остаётся за прототипом B1.

- `Pigeon.Common`, `Pigeon.Core`, `Pigeon.AI`, `Pigeon.Simulation` и заглушка генератора
  `Pigeon.World.City.Generation` собираются и публикуются под `browser-wasm` с полным trimming,
  без предупреждений trim-анализа.
- Харнесс `tests/Pigeon.Wasm.Harness` построен на `Microsoft.NET.Sdk.WebAssembly` (JSExport, без
  Blazor). Шаблон `wasmbrowser` требует workload, а SDK — нет: интерпретатор без AOT и нативного
  релинка собирается обычным SDK .NET 10. Поэтому харнесс входит в общее решение и собирается на
  всех платформах CI.
- В headless Chrome харнесс выполняет те же эталонные сценарии, что .NET-тесты
  (`tests/Shared/GoldenSimulation.cs`, `tests/Shared/GoldenCity.cs`), и сверяет хеши. Джоба CI
  `core-in-wasm` падает при любом расхождении.
- Размер публикации (интерпретатор, без AOT): ~4.3 МБ исходных файлов, ~1.7 МБ gzip,
  **~1.4 МБ brotli**. Из них ~0.95 МБ brotli — рантайм `dotnet.native.wasm`, ~0.3 МБ —
  `System.Private.CoreLib`. Бюджет B1 (≤ 10 МБ сжатого) пока выполняется с большим запасом; рендер
  и ассеты города добавятся сверху.
