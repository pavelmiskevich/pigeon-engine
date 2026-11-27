# Режим тишины (#31): план

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** голубь затихает в ручной и автоматической тишине и просыпается на том же месте.

**Architecture:**
- Тишина — вход журнала `QuietChanged`, состояние симуляции (хеш, снимок).
- Utility AI в тишине берёт только тихие намерения.
- Хост ведёт `QuietMode` (ручная тишина и полный экран) и подаёт вход только при смене состояния.

**Tech Stack:** .NET 10, xUnit, Win32 (`SHQueryUserNotificationState`); тесты в Debug.

**Spec:** `docs/superpowers/specs/2026-11-27-quiet-mode-design.md`

## Global Constraints

- Детерминизм (ADR-0016): тишина меняет симуляцию только через журнал.
- Формат снимка остаётся 4: `Quiet` — необязательное поле, по умолчанию `false`.
- Имя входа в хранилище — `quiet-changed`, данные — `1` или `0`.
- Тихие намерения — `rest`, `sleep`, `groom`; запасной вариант в тишине — `Rest`.
- Пробуждение — arousal +0,1.
- Полный экран: QUNS 2, 3, 4, 7.
- Сообщения пользователю и журнал хоста — по-русски, без ключей мест.

## Review Focus

- Повторный `QuietChanged` с тем же значением не пересматривает решение и не меняет хеш, кроме
  длины журнала.
- Тишина во время перелёта или переноски: голубь не застревает в воздухе после тишины.
- Снимок посреди тишины → восстановление → голубь всё ещё тих.
- Старый снимок без поля `Quiet` читается.
- Ручная тишина поверх автоматической: конец полного экрана не будит голубя раньше срока.

---

### Task 1: Тишина в Utility AI

**Files:** `src/Pigeon.AI/Behavior/BehaviorConfig.cs`, `BehaviorConfigJson.cs`, `behavior.json`,
`src/Pigeon.AI/UtilityBrain.cs`; тесты `tests/Pigeon.AI.Tests/*`.

**Produces:**
- `IntentDefinition.AllowedInQuiet` (JSON `quiet`);
- `UtilityBrain.Score(..., bool withMomentum, bool quiet = false)`;
- `UtilityBrain.Decide(..., IRandomSource random, bool quiet = false)`.

- [ ] Тесты:
  - `DefaultConfig_QuietIntentsAreRestSleepAndGroom`;
  - `Quiet_LeavesOnlyQuietIntents`: в тишине среди вариантов только `Rest`, `Sleep`, `Groom`;
  - `Quiet_WithNothingAvailable_Rests`: все тихие на кулдауне → выбор `Rest`.
- [ ] Реализация, коммит.

### Task 2: Тишина в симуляции и хранилище

**Files:** `src/Pigeon.Simulation/InputEvents.cs`, `PigeonSimulation.cs`, `SimulationSnapshot.cs`;
`src/Pigeon.Persistence.Sqlite/Serialization/*`; тесты `tests/Pigeon.Simulation.Tests/QuietTests.cs`,
`tests/Pigeon.Persistence.Tests/*`.

**Produces:** `QuietChanged(bool Quiet)`, `PigeonSimulation.IsQuiet`, `SimulationSnapshot.Quiet`.

- [ ] Тесты:
  - `Quiet_MakesThePigeonSettle`: после `QuietChanged(true)` намерение тихое, и за час ни одного
    нетихого;
  - `QuietEnd_WakesThePigeonUp`: arousal выше на 0,1, решение пересмотрено;
  - `RepeatedQuietState_ChangesNothing`;
  - `Quiet_SurvivesSnapshotAndReplay`;
  - хранилище: вход `quiet-changed` туда и обратно; снимок с `Quiet` туда и обратно; JSON без
    поля — не тихо.
- [ ] Реализация, коммит.

### Task 3: Хост — QuietMode, полный экран, меню, документы

**Files:** `src/Pigeon.Desktop/QuietMode.cs` (новый), `DesktopHost.cs`,
`src/Pigeon.Desktop.Windows/FullscreenState.cs` (новый); тест
`tests/Pigeon.Desktop.Tests/QuietModeTests.cs`; ADR-0016/0017, `docs/roadmap/status.md`.

**Produces:**
- `QuietMode`: `bool IsQuiet`, `bool QuietFor(TimeSpan duration, TimeSpan now)`,
  `bool Update(TimeSpan now, bool fullscreen)` — `true`, если состояние сменилось;
- `static TimeSpan QuietMode.UntilMorning(DateTime localNow)`;
- `FullscreenState.IsQuiet(int state)`, `FullscreenState.Read()`.

- [ ] Тесты: ручная тишина кончается по сроку; полный экран включает и выключает; ручная поверх
  полного экрана держится до срока; повтор без изменений — `false`; `UntilMorning` для 10:00,
  23:00, 02:00 и ровно 06:00; `FullscreenState.IsQuiet` для 1–7.
- [ ] Хост: опрос на обновлении экрана, меню, `QuietChanged` при смене, строка в журнале хоста,
  `hidden` по тишине.
- [ ] Живой запуск и замер (`--measure`), документы, коммит.
