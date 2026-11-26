# Память мест и Life Story — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** голубь помнит места и забывает их со временем, новизна места меняет его выбор по
характеру, а события «впервые» пишутся в хронику ровно один раз.

**Architecture:** память (`PlaceMemory`) и хроника (`LifeStory`) — часть `PigeonState` в
`Pigeon.Core`, обновляются симуляцией по уже журналируемым входам и сохраняются в снимке
формата 4. Utility AI получает множитель новизны. Хост только пишет новые записи хроники в
свой журнал и сразу сохраняет снимок.

**Tech Stack:** .NET 10, C#, xunit.v3, System.Text.Json source generator, Microsoft.Data.Sqlite.

**Spec:** `docs/superpowers/specs/2026-11-26-memory-life-story-design.md` — читать вместе с
планом; формулы и константы берутся оттуда.

## Global Constraints

- Ветка `feature/33-memory-life-story`, один PR в `main`, в описании `Closes #33`. Без строк
  об инструменте и сессии в коммитах и PR.
- Комментарии, сообщения исключений и журнала — по-русски, как в соседнем коде.
- В симуляции и ядре — никаких `Math.Exp`/`Math.Pow`/`Math.Log`: только четыре действия и
  `DeterministicMath.PowInt` с целыми показателями.
- Числа перечислений — часть формата: новые значения только в конец, существующие не менять.
- Тесты гонять в Debug: `dotnet test PigeonEngine.slnx -c Debug` (Release DLL прототипов может
  держать запущенный у владельца прототип).
- Эталонный хеш (`tests/Shared/GoldenSimulation.cs`) меняется только в задаче 6, с
  объяснением в комментарии; расхождение между платформами — ошибка, а не повод обновить.
- Константы: `VisitGain = 0.35`, `AncestorGain = 0.1`, `BaseTimeConstant = 86 400 с`,
  `ConsolidationPerVisit = 1.5`, `MaxTimeConstant = 2 592 000 с`, `Capacity = 64`,
  `FavoriteAt = 0.8`, `RivalAtMost = 0.5`, `MaxMonitorVisits = 32`, `novelty` у `explore` и
  `relocate` = `0.25`.

## Review Focus

- Перелёт, при котором цель пропадает из affordances в том же тике, что и `ActionFinished` —
  визит всё равно засчитан (задача 4, тест `Relocation_CountsAVisitEvenWhenTheTargetVanishes`).
- Восстановление из снимка и `ReplayFrom` не повторяют записи хроники и дают тот же хеш (задача 4,
  `LifeStory_SurvivesRestoreAndReplayWithoutDuplicates`).
- Огромное отсутствие (год, `TimeSpan` в тиках на грани `long`) — без переполнения, NaN и
  отрицательного знакомства (задачи 1 и 4).
- Старый снимок формата 3 из базы реального пользователя читается и голубь продолжает жизнь
  (задача 5, `Version3_IsUpgradedWithEmptyMemoryAndBornEntry`).
- Отчёт мира о завершении чужого или устаревшего намерения (другой `IntentStartedTick`) визита
  не засчитывает (задача 4).

---

### Task 1: Память мест в ядре

**Files:**
- Create: `src/Pigeon.Core/PlaceMemory.cs`
- Modify: `src/Pigeon.Core/WorldKeys.cs` (метод предков у `PlaceKey` и описание соглашения о `/`)
- Test: `tests/Pigeon.Core.Tests/PlaceMemoryTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MemoryRules(double VisitGain, double AncestorGain, long BaseTimeConstantSeconds, double ConsolidationPerVisit, long MaxTimeConstantSeconds, int Capacity, double FavoriteAt, double RivalAtMost)` со `static MemoryRules Default` (значения — Global Constraints).
  - `public readonly record struct PlaceMemoryEntry(PlaceKey Place, double Familiarity, long LastSeen, int Visits, bool Territory)` — `Familiarity` на момент `LastSeen`.
  - `public sealed class PlaceMemory`:
    - `PlaceMemory(MemoryRules? rules = null)`;
    - `static PlaceMemory FromSnapshot(IReadOnlyList<PlaceMemoryEntry> entries, MemoryRules? rules = null)` — бросает `ArgumentException` на повтор ключа, знакомство вне 0..1 или не конечное, отрицательные `LastSeen`/`Visits`, больше `Capacity` записей;
    - `double FamiliarityOf(PlaceKey place, long now)` — 0 для незнакомого;
    - `void Visit(PlaceKey place, long now, bool territory)` — полный визит места и слабый визит каждого предка (предок помечается `Territory = true`);
    - `IReadOnlyList<PlaceMemoryEntry> Entries` — по ключу, `StringComparer.Ordinal`;
    - `PlaceKey? Favorite(long now)` — территория по правилу «≥ FavoriteAt, а следующая ≤ RivalAtMost», при не меньше двух известных территориях; иначе `null`;
    - `void AddTo(StableHasher hasher)`.
  - `PlaceKey.Ancestors()` → `IEnumerable<PlaceKey>`, ближайший первым, только предки из двух и более сегментов (`desktop/M/taskbar` → `desktop/M`; `desktop/cursor` → пусто).

Алгоритм (спека, раздел «Память мест»): `τ = min(Base × PowInt(1.5, max(0, Visits − 1)), Max)`,
`keep = 1 − 1/τ`, знакомство сейчас `f × PowInt(keep, now − LastSeen)`; при `now < LastSeen`
показатель 0. Визит: `f ← f_now + G × (1 − f_now)`, `LastSeen = now`; `Visits++` только у самого
места, не у предка; `Territory` — `||` с прежним. Вытеснение — только при вставке новой записи в
полную память: удаляется запись с наименьшим знакомством сейчас (равенство — меньший ключ
ordinal), кроме места этого визита и его предков.

- [ ] **Step 1: Write the failing tests** в `PlaceMemoryTests`:
  - `UnknownPlace_IsUnfamiliar` — `FamiliarityOf` = 0.
  - `Visit_RaisesFamiliarityByTheGain` — после одного визита в момент 0: `0.35`; после второго в тот же момент: `0.35 + 0.35 × 0.65`.
  - `Familiarity_DecaysWithTheTimeConstant` — один визит в 0; в момент 86 400 значение равно `0.35 × PowInt(1 − 1/86400.0, 86400)` (точно) и лежит в `0.128..0.129`.
  - `RepeatedVisits_AreForgottenMoreSlowly` — места A (1 визит) и B (3 визита подряд в момент 0, затем сравнение отношения `f(now)/f(LastSeen)` через 3 суток): у B отношение больше.
  - `TimeConstant_IsCappedAtThirtyDays` — 20 визитов; отношение через 30 суток равно `PowInt(1 − 1/2592000.0, 2592000)`.
  - `Visit_ReinforcesAncestorsWeakly` — визит `desktop/M/taskbar`: у `desktop/M` знакомство `0.1`, `Visits = 0`, `Territory = true`; у `desktop` записи нет.
  - `FullMemory_EvictsTheLeastFamiliar` — `Capacity = 3`, места с разным знакомством; новый визит вытесняет наименее знакомое, при равенстве — меньший ключ.
  - `YearOfAbsence_IsFiniteAndNonNegative` — визит в 0, запрос в `365 × 86 400`: конечно, `≥ 0`, `< 1e-6`.
  - `Favorite_NeedsAClearPreferenceAmongTwoTerritories` — одна территория 0.9 → `null`; 0.9 и 0.6 → `null`; 0.9 и 0.4 → первая.
  - `FromSnapshot_RejectsBrokenEntries` — `[Theory]` по дублю, `NaN`, `1.5`, `Visits = -1`.
  - `SameVisits_GiveTheSameHash` — две памяти с одинаковой последовательностью визитов дают одинаковый `StableHasher.Value`, другой порядок мест — другой.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.Core.Tests -c Debug --filter PlaceMemoryTests` — FAIL: типов нет.
- [ ] **Step 3: Implement** `MemoryRules`, `PlaceMemoryEntry`, `PlaceMemory`, `PlaceKey.Ancestors()` по интерфейсу и алгоритму выше; соглашение о сегментах через `/` — в XML-комментарий `PlaceKey`.
- [ ] **Step 4: Run** тот же фильтр — PASS; затем весь `Pigeon.Core.Tests` — PASS.
- [ ] **Step 5: Commit** `git commit -m "Память мест: знакомство, забывание и закрепление повторами"`

### Task 2: Хроника жизни в ядре

**Files:**
- Create: `src/Pigeon.Core/LifeStory.cs`
- Test: `tests/Pigeon.Core.Tests/LifeStoryTests.cs`

**Interfaces:**
- Produces:
  - `public enum LifeEventKind { Born = 0, FirstWalk = 1, FirstInteraction = 2, FirstSleep = 3, FirstAdventure = 4, FirstMultiMonitorTransition = 5, FirstVisitToMonitor = 6, FirstFavoriteTerritory = 7 }`.
  - `public sealed record LifeEvent(string Key, LifeEventKind Kind, PlaceKey? Place, long Tick, long LifeSeconds, int Version)`; `public const int CurrentVersion = 1` в `LifeStory`.
  - `public sealed class LifeStory`:
    - `LifeStory()` — пустая; `static LifeStory FromSnapshot(IReadOnlyList<LifeEvent> events)` — бросает `ArgumentException` на повтор ключа, неизвестный `Kind`, ключ, не соответствующий виду;
    - `static string KeyOf(LifeEventKind kind, PlaceKey? place)` — `born`, `first-walk`, `first-interaction`, `first-sleep`, `first-adventure`, `first-multi-monitor-transition`, `first-visit:<place>` (place обязателен), `first-favorite-territory`;
    - `bool Record(LifeEventKind kind, PlaceKey? place, long tick, long lifeSeconds)` — `true`, если запись добавлена; `false`, если ключ уже есть или `FirstVisitToMonitor` уже 32;
    - `bool Contains(string key)`, `IReadOnlyList<LifeEvent> Events` (в порядке добавления), `void AddTo(StableHasher hasher)`;
    - `const int MaxMonitorVisits = 32`.

- [ ] **Step 1: Write the failing tests** в `LifeStoryTests`:
  - `Record_AddsOnce_AndTheSecondTimeIsIgnored` — второй `Record(FirstSleep, …)` с другим тиком → `false`, в `Events` одна запись с первым тиком.
  - `Keys_AreStable` — `[Theory]` по каждому виду: ожидаемая строка ключа (это формат, ключи не менять).
  - `FirstVisit_IsPerMonitor_AndCapped` — два разных места — две записи; 33-я разная — `false`.
  - `FirstVisit_RequiresAPlace` — `Record(FirstVisitToMonitor, null, …)` бросает `ArgumentException`.
  - `FromSnapshot_RejectsDuplicatesAndUnknownKinds` — `[Theory]`.
  - `Events_KeepInsertionOrder_AndHashFollowsThem`.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.Core.Tests -c Debug --filter LifeStoryTests` — FAIL.
- [ ] **Step 3: Implement** по интерфейсу; `Version` новой записи — `CurrentVersion`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `git commit -m "Life Story: хроника событий «впервые» с идемпотентным ключом"`

### Task 3: Состояние голубя и снимок формата 4

**Files:**
- Modify: `src/Pigeon.Core/PigeonState.cs`, `src/Pigeon.Core/PigeonStateSnapshot.cs`
- Modify: `src/Pigeon.Simulation/SimulationSnapshot.cs` (`CurrentFormatVersion = 4`, `FromVersion1`, описание форматов)
- Modify: `src/Pigeon.Simulation/PigeonSimulation.cs` (только `Born` и вызовы `StartIntent`)
- Test: `tests/Pigeon.Core.Tests/PigeonStateTests.cs` (создать, если нет), `tests/Pigeon.Simulation.Tests/SnapshotTests.cs`

**Interfaces:**
- Consumes: `PlaceMemory`, `PlaceMemoryEntry` (задача 1); `LifeStory`, `LifeEvent` (задача 2).
- Produces:
  - `PigeonStateSnapshot` — новые поля в конце, с умолчаниями: `PlaceKey? IntentPlace = null`, `long AbsenceSeconds = 0`, `IReadOnlyList<PlaceMemoryEntry>? Memory = null`, `IReadOnlyList<LifeEvent>? LifeStory = null` (`null` = пусто).
  - `PigeonState`: свойства `PlaceKey? IntentPlace`, `long AbsenceSeconds`, `PlaceMemory Memory`, `LifeStory LifeStory`; методы `void StartIntent(IntentKind intent, AffordanceId? target, PlaceKey? place, long tick, long endsTick)` (старая перегрузка удаляется), `void AddAbsence(long seconds)` (бросает на отрицательное, насыщается на `long.MaxValue`). `ToSnapshot`/`FromSnapshot`/`AddTo` включают новое.
  - `PigeonSimulation.Born` записывает в хронику `Born` с тиком 0 и `LifeSeconds` 0; `FromVersion1` — тоже.

- [ ] **Step 1: Write the failing tests:**
  - `PigeonStateTests.Snapshot_RoundTripsMemoryLifeStoryAndAbsence` — состояние с визитом, записью хроники, `AddAbsence(3600)` и намерением с местом → `FromSnapshot(ToSnapshot())` даёт тот же хеш и те же значения.
  - `PigeonStateTests.AddAbsence_RejectsNegative_AndSaturates`.
  - `SnapshotTests.NewPigeon_HasTheBornEntry` — `new PigeonSimulation(1, Desktop).Pigeon.LifeStory.Events` = одна запись `Born`, тик 0.
  - Существующие тесты `SnapshotTests` с `CurrentFormatVersion` — ожидание 4.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.Core.Tests tests/Pigeon.Simulation.Tests -c Debug` — FAIL компиляции/утверждений.
- [ ] **Step 3: Implement.** В `PigeonSimulation.Decide` передать в `StartIntent` `PlaceKey` выбранной цели из `_affordances` (поиск по `AffordanceId`; без цели — `null`).
- [ ] **Step 4: Run** те же проекты — PASS, кроме `DeterminismTests.GoldenRun_*` (хеш меняется; обновляется в задаче 6). Отметить в сообщении коммита.
- [ ] **Step 5: Commit** `git commit -m "Состояние голубя: память, хроника и время отсутствия в снимке формата 4"`

### Task 4: Симуляция ведёт память и хронику

**Files:**
- Modify: `src/Pigeon.Simulation/PigeonSimulation.cs`
- Modify: `src/Pigeon.AI/Behavior/BehaviorConfig.cs`, `BehaviorConfigJson.cs`, `behavior.json` (только флаг `visitsTarget`)
- Test: `tests/Pigeon.Simulation.Tests/MemoryLifeTests.cs` (создать)

**Interfaces:**
- Consumes: задачи 1–3.
- Produces:
  - `IntentDefinition` — в конце `bool VisitsTarget = false`; JSON `"visitsTarget": true` у `explore`, `observe`, `seekAttention`, `followTarget`, `play`, `relocate`.
  - `PigeonSimulation.LifeSeconds` → `long` = `Tick × Step.Ticks / TimeSpan.TicksPerSecond + Pigeon.AbsenceSeconds` (насыщение).
  - `PigeonSimulation.LifeStory` → `IReadOnlyList<LifeEvent>` (= `Pigeon.LifeStory.Events`).

Правила (спека, таблица Life Story и «Когда визит засчитывается»):
- `AbsenceEnded` → `Pigeon.AddAbsence(Duration.Ticks / TimeSpan.TicksPerSecond)` (полное отсутствие, без 8-часового предела).
- `ActionFinished` с совпавшими намерением и `IntentStartedTick`, `Outcome == Completed`, у намерения `VisitsTarget` и есть `IntentPlace` → `Memory.Visit(place, LifeSeconds, territory: намерение == Relocate)`; `Explore` → `FirstAdventure`; `Relocate` → `FirstMultiMonitorTransition` и `FirstVisitToMonitor(place)`, если такого ключа ещё нет. После любого визита — `Memory.Favorite(now)` → `FirstFavoriteTerritory`.
- `AffordancesObserved` при пустой памяти → визит каждого `PerchSpot`/`RestSpot` из наблюдения (место рождения).
- `UserPoked`/`UserPetted`/`UserPickedUp` → `FirstInteraction`.
- Начало `Sleep` → `FirstSleep`. Решение с причиной `Completed` после `Wander`, длившегося не меньше `MinDuration` прогулки → `FirstWalk`.
- `LifeSeconds` для записи — текущее значение, `Tick` — текущий тик.

- [ ] **Step 1: Write the failing tests** в `MemoryLifeTests` (мир — фейковый `IAffordanceSource` со списком; отчёты мира подаются как `ActionFinished` по текущему намерению):
  - `Relocation_CountsAVisitEvenWhenTheTargetVanishes` — голубь выбрал `relocate` к `desktop/B`; в одном тике подать `ActionFinished(Relocate, started, Completed)` и мир без `desktop/B`; итог: знакомство `desktop/B` > 0, в хронике `first-multi-monitor-transition` и `first-visit:desktop/B`.
  - `FailedOrStaleReports_DoNotCountAVisit` — `Failed` и `Completed` с чужим `IntentStartedTick` → памяти о цели нет.
  - `Flee_DoesNotCountAVisit`.
  - `FirstObservation_RemembersTheBirthPlace` — первое наблюдение с `desktop/A/taskbar` → знакомство `0.35`, у `desktop/A` — `0.1`.
  - `Absence_IsCountedInFull` — `AbsenceEnded(14 суток)` → `LifeSeconds` вырос на `1 209 600`; знакомство места, виденного до этого, упало по формуле задачи 1.
  - `FirstEvents_AreRecordedOnce` — два тычка, два сна → по одной записи `first-interaction`, `first-sleep`.
  - `LifeStory_SurvivesRestoreAndReplayWithoutDuplicates` — живой прогон с событиями; `Restore(CreateSnapshot())` и `ReplayFrom(snapshot, tail, tick)` дают тот же `ComputeStateHash`, что и живой, и ту же хронику без повторов.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.Simulation.Tests -c Debug --filter MemoryLifeTests` — FAIL.
- [ ] **Step 3: Implement** правила выше; флаг `visitsTarget` — через `IntentDto` и `IntentDefinition`, тест `BehaviorConfigTests` на разбор флага.
- [ ] **Step 4: Run** `Pigeon.Simulation.Tests` и `Pigeon.AI.Tests` — PASS (кроме эталонного хеша).
- [ ] **Step 5: Commit** `git commit -m "Симуляция ведёт память мест и хронику по подтверждениям мира"`

### Task 5: Новизна в Utility AI

**Files:**
- Modify: `src/Pigeon.AI/UtilityBrain.cs`, `src/Pigeon.AI/Behavior/BehaviorConfig.cs`, `BehaviorConfigJson.cs`, `behavior.json`
- Modify: `src/Pigeon.Simulation/PigeonSimulation.cs` (передать `LifeSeconds` в `Decide`)
- Test: `tests/Pigeon.AI.Tests/UtilityBrainTests.cs`, `tests/Pigeon.AI.Tests/BehaviorConfigTests.cs`

**Interfaces:**
- Consumes: `PigeonState.Memory`, `PlaceMemory.FamiliarityOf` (задачи 1, 3).
- Produces:
  - `IntentDefinition` — в конце `double NoveltyWeight = 0.0`; JSON `"novelty": 0.25` у `explore` и `relocate`. `Problems()`: вес вне `0..0.9` или ненулевой у намерения без цели — ошибка.
  - `UtilityBrain.Score(PigeonState pigeon, IReadOnlyList<Affordance> affordances, IntentCooldowns cooldowns, long tick, long lifeSeconds, bool withMomentum)` и `Decide(…, long tick, long lifeSeconds, DecisionTrigger trigger, IRandomSource random)` — новый параметр `lifeSeconds` после `tick`; все вызовы в тестах обновить.
  - `public const string NoveltyFactor = "novelty"`; фактор добавляется только при `NoveltyWeight != 0`; `LeadingFactors` показывает его вместе с личностью и настроением при значении `≥ 1.1`; `ConstantFactor`-фильтр не трогать.

Формула (спека, «Новизна в Utility AI»):
`novelty = 1 − FamiliarityOf(target.PlaceKey, lifeSeconds)`,
`lean = clamp(0.5·Centered(Curiosity) + 0.5·Centered(Boldness), −1, 1)`,
`factor = 1 + w × (2·novelty − 1) × lean`; множится вместе с личностью и настроением.

- [ ] **Step 1: Write the failing tests:**
  - `Novelty_PullsTheCuriousToTheUnknownTerritory` — личность Curiosity 100, Boldness 100, остальное 50; две `TerritoryZone` с одинаковыми оценками, одна знакома на 0.9; у незнакомой оценка `relocate` больше, отношение оценок = `1.25 / (1 + 0.25 × (2·0.1 − 1))`.
  - `Novelty_KeepsTheTimidAtHome` — Curiosity 0, Boldness 0: знакомая оценена выше.
  - `Novelty_DoesNotTouchTheAveragePigeon` — `Personality.Average`: оценки равны, фактор `novelty` = 1.
  - `IntentsWithoutNoveltyWeight_HaveNoNoveltyFactor` — у `observe` фактора нет.
  - `BehaviorConfigTests.NoveltyWeight_IsValidated` — `[Theory]` `-0.1`, `0.95`, вес у намерения без цели.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.AI.Tests -c Debug` — FAIL.
- [ ] **Step 3: Implement**; в `PigeonSimulation.Decide` передать `LifeSeconds`; комментарий в шапке `behavior.json` — описать `novelty` и `visitsTarget`.
- [ ] **Step 4: Run** `Pigeon.AI.Tests`, `Pigeon.Simulation.Tests` — PASS (кроме эталонного хеша).
- [ ] **Step 5: Commit** `git commit -m "Utility AI: новизна места по характеру голубя"`

### Task 6: Хранилище формата 4, эталон, два монитора

**Files:**
- Modify: `src/Pigeon.Persistence.Sqlite/Serialization/PersistenceDtos.cs`, `SnapshotSerializer.cs`
- Modify: `tests/Shared/GoldenSimulation.cs` (новый `ExpectedHash` + объяснение)
- Test: `tests/Pigeon.Persistence.Tests/SnapshotSerializerTests.cs` (или существующий файл тестов сериализатора), `tests/Pigeon.World.Desktop.Tests/TwoMonitorLifeTests.cs`

**Interfaces:**
- Consumes: задачи 1–5.
- Produces: `SnapshotDto` — в конце `string? IntentPlace`, `long? AbsenceSeconds`, `IReadOnlyList<PlaceMemoryDto>? Memory`, `IReadOnlyList<LifeEventDto>? LifeStory`; `PlaceMemoryDto(string Place, double Familiarity, long LastSeen, int Visits, bool Territory)`, `LifeEventDto(string Key, int Kind, string? Place, long Tick, long LifeSeconds, int Version)`.

Перевод: формат 2 и 3 → 4 с пустой памятью, `AbsenceSeconds = 0`, `IntentPlace = null`, хроника
`[Born, тик 0, LifeSeconds 0, Version 1]`. Формат 4 без хроники — `InvalidDataException`. Ошибки
`FromSnapshot` (задачи 1, 2) из сериализатора выходят как `InvalidDataException`.

- [ ] **Step 1: Write the failing tests:**
  - `Version4_RoundTripsMemoryAndLifeStory`.
  - `Version3_IsUpgradedWithEmptyMemoryAndBornEntry` — JSON снимка формата 3 (взять из сериализации на `main` до задачи 3 и положить строкой в тест) → `FormatVersion` 4, память пуста, хроника `[born]`, `PigeonSimulation.Restore` работает.
  - `UnknownLifeEventKind_IsAFormatError`.
  - `TwoMonitorLifeTests.Pigeon_RemembersBothMonitors_AndTheStoryOfTheFirstFlight` — существующий `Host`, seed 21, 6 часов: в хронике `first-multi-monitor-transition`, `first-visit:desktop/external` или `first-visit:desktop/laptop` (тот, куда долетел); знакомство обоих мониторов > 0.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.Persistence.Tests tests/Pigeon.World.Desktop.Tests -c Debug` — FAIL.
- [ ] **Step 3: Implement** DTO и перевод; затем прогнать `DeterminismTests`, взять новый хеш, записать в `GoldenSimulation.ExpectedHash` и дописать в `<remarks>` строку: что изменилось (память, хроника, новизна, полное отсутствие).
- [ ] **Step 4: Run** весь `dotnet test PigeonEngine.slnx -c Debug` — PASS. Хеш одинаков на всех платформах проверит CI (включая `core-in-wasm`).
- [ ] **Step 5: Commit** `git commit -m "Снимок формата 4 в хранилище, новый эталон симуляции"`

### Task 7: Хост, документация

**Files:**
- Modify: `src/Pigeon.Desktop/PetSession.cs`
- Modify: `docs/adr/0017-persistence-journal-snapshots.md` (раздел «Память и Life Story в снимке»), `docs/roadmap/status.md` (M1.9)
- Test: `tests/Pigeon.Desktop.Tests/PetSessionTests.cs`

**Interfaces:**
- Consumes: `PigeonSimulation.LifeStory` (задача 4).
- Produces: в `PetSession.Tick()` — новые записи хроники с последней виденной позиции пишутся
  строкой `INFO В хронике: <фраза>.` и вызывают `SaveNow()`. Начальная позиция — длина хроники
  при открытии (восстановленные записи не пишутся). Фразы: Born — «родился»; FirstWalk —
  «впервые прогулялся»; FirstInteraction — «впервые встретил внимание человека»; FirstSleep —
  «впервые уснул»; FirstAdventure — «впервые отправился исследовать»; FirstMultiMonitorTransition —
  «впервые перелетел на другой монитор»; FirstVisitToMonitor — «впервые побывал на этом мониторе»;
  FirstFavoriteTerritory — «у него появился любимый монитор». Ключ и место в журнал не пишутся.

- [ ] **Step 1: Write the failing tests:**
  - `NewLifeEvent_IsLogged_AndSavedAtOnce` — открыть, подать `UserPoked`, прогнать тик, `Tick()`, `FlushAsync()`; в журнале `INFO В хронике: впервые встретил внимание человека.`; новая сессия после аварии (`CloseAsync(false)`) видит запись в хронике.
  - `RestoredStory_IsNotLoggedAgain` — повторное открытие не пишет строк «В хронике».
  - `LifeEventLines_HaveNoPlaceKeys` — после `FirstVisitToMonitor` в журнале нет `desktop/`.
- [ ] **Step 2: Run** `dotnet test tests/Pigeon.Desktop.Tests -c Debug --filter PetSessionTests` — FAIL.
- [ ] **Step 3: Implement**; дописать ADR-0017 (почему память и хроника в снимке, а не в таблицах; «ровно один раз» — в линии состояния) и строку статуса M1.9.
- [ ] **Step 4: Run** весь `dotnet test PigeonEngine.slnx -c Debug` — PASS; живой запуск `dotnet run --project src/Pigeon.Desktop -c Release -- --data <scratchpad>` на минуту: в журнале хоста «Родился новый голубь.» (запись `Born` есть с рождения и строкой
  «В хронике» не дублируется), после прогулки — «В хронике: впервые прогулялся.».
- [ ] **Step 5: Commit** `git commit -m "Хост пишет хронику голубя в журнал и сохраняет её сразу"`; push, PR `Closes #33`.
