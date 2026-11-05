# Состояние проекта

Срез на 2026-11-05. Обновляется на переходах между этапами.

## MVP-0 — технический фундамент

Эпик [#11](https://github.com/pavelmiskevich/pigeon-engine/issues/11): закрыто 8 из 10 задач.

| Задача | Итог |
|--------|------|
| S0.1 каркас решения и CI (#1) | готово |
| S0.2 стек оверлея (#2) | ADR-0021: тонкое нативное окно + SkiaSharp — [замеры](../research/desktop-overlay.md) |
| S0.3 проба возможностей (#3) | **открыта**: `pigeon-probe` готов для Windows, macOS, X11, Wayland; ждём прогона на живом Mac ([инструкция](../platform/probe/README.md)) |
| S0.4 голубь на окне (#4) | **открыта**: прототип на Windows готов — [отчёт](../research/window-perch.md); нет прототипа для macOS или X11 |
| S0.5 симуляция и детерминизм (#5) | ADR-0016 |
| S0.6 хранение (#6) | ADR-0017; доработка — #17 в MVP-1 |
| S0.7 ядро в WASM (#7) | одинаковые эталонные хеши в CoreCLR и в браузере |
| S0.8 анимация (#8) | ADR-0023: растровые атласы в формате Aseprite, генератор `tools/Pigeon.SpriteBake` |
| S0.9 дистрибуция (#9) | ADR-0024: Velopack, без платной подписи кода |
| S0.10 замер Jev (#10) | [отчёт](../research/jev-measurement.md), параметры ТЗ §17–18 исправлены |

Критерии выхода из MVP-0 ([mvp-0.md](mvp-0.md)):

1. ADR 0014, 0016, 0017, 0021, анимация (0023), дистрибуция (0024) — приняты все, кроме 0014
   (Proposed: окончательный выбор рендера браузера — перед MVP-4).
2. Матрица возможностей проверена для Tier-1 и Tier-2 — кроме живого Mac (#3).
3. Бюджеты ТЗ §37 и параметры Jev ТЗ §18 подтверждены замерами.
4. Задачи MVP-1 заведены: эпик [#38](https://github.com/pavelmiskevich/pigeon-engine/issues/38).

## MVP-1 — Desktop Core (Windows, L2)

Эпик [#38](https://github.com/pavelmiskevich/pigeon-engine/issues/38), 14 задач. Начинать с задач без
зависимостей: M1.1 домен голубя (#25), затем M1.2 Utility AI (#26), M1.3 Desktop World L2 (#27),
M1.9 хранение (#33).

## Решения владельца

- Голубь — реалистичный чёрный сизарь, походка с голубиным кивком; голова, шея и тело — один силуэт.
- Художника нет: кадры из генератора или сторонние только под CC0, public domain или с атрибуцией;
  ассеты генеративных моделей не используются.
- Платной подписи кода нет; иностранные платные сервисы не используются.
- В Browser голубь летает сам (Q10). Лицензии нет, все права защищены (Q13).
- Ожидают решения: ADR-0014 (рендер браузера), ADR-0015, 0018–0020, 0022 (перелётный голубь, Q2).

## Как запустить

```
dotnet test --solution PigeonEngine.slnx
dotnet run --project tools/Pigeon.Probe                 # возможности платформы
dotnet run --project tools/Pigeon.SpriteBake -c Release # атласы голубя в assets/sprites/pigeon-black
dotnet run --project prototypes/Overlay.Win32Skia -c Release -- --sprite assets/sprites/pigeon-black
dotnet run --project prototypes/Perch.Win32 -c Release -- --sprite assets/sprites/pigeon-black
```

Голубь закрывается правым кликом по нему.
