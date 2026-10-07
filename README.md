# pigeon-engine

Pigeon — живой цифровой голубь. Один Pigeon Engine, два мира: реальный рабочий стол
пользователя (Desktop Pet, local-first) и процедурный город в браузере (Browser Game).

Проект на стадии MVP-0 — технического фундамента. Документация — в [docs/](docs/README.md):

- [техническая спецификация](docs/spec/PIGEON_Technical_Specification.md);
- [матрица возможностей платформ](docs/platform/capability-matrix.md);
- [план MVP-0](docs/roadmap/mvp-0.md);
- [архитектурные решения (ADR)](docs/adr/README.md).

## Сборка

Нужен .NET SDK 10.

```sh
dotnet build PigeonEngine.slnx
dotnet test --solution PigeonEngine.slnx
```

## Структура

| Каталог | Содержание |
|---------|-----------|
| `src/` | Движок, миры, хранение, хост Desktop и платформенные адаптеры |
| `tools/` | Утилиты, в том числе `pigeon-probe` |
| `tests/` | Тесты; `Pigeon.Architecture.Tests` проверяет правила зависимостей ядра |
| `build/` | Общие файлы сборки: запрещённые API для ядра и генератора мира |
| `docs/` | Спецификация, ADR, план работ, исследования |

Ядро — проекты со свойством `PigeonCore=true`: `Common`, `Core`, `World.Abstractions`, `AI`,
`Simulation`, `World.Desktop`, `World.City`, `World.City.Generation`. Оно не зависит от ОС, UI,
сети, БД и DI-контейнера, собирается как trim- и AOT-safe, а время и случайность получает
только через абстракции (ТЗ §3.2–3.3). Нарушение ломает сборку или архитектурные тесты.

Проекты, которые относятся к более поздним этапам (`Pigeon.AI.Jev`, `Pigeon.AI.Llm`,
`Pigeon.Persistence.PostgreSql`, `Pigeon.Web.Client`, `Pigeon.Server`, `web/`), создаются вместе
с первой задачей, которой они нужны. Тестовые проекты — вместе с первым кодом, который они
проверяют.

## Лицензия

Лицензия не предоставляется. Все права защищены.
