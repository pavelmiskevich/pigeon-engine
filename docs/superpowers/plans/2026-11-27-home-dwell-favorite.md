# Обживание дома и любимый монитор (#89): план

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** дом обживается временем на нём, робкого держит обжитой дом, у него появляется любимый монитор.

**Architecture:**
- Память получает `Dwell` (слабое закрепление известной территории без визита) и проверку
  любимой с учётом видимых в мире непосещённых территорий.
- Симуляция раз в минуту обживает территорию под голубем.
- Utility AI получает множитель привязанности к дому у целей `TerritoryZone`.
- Правило «где голубь сидит» общее для симуляции и Utility AI.

**Tech Stack:** .NET 10, xUnit; тесты в Debug.

**Spec:** `docs/superpowers/specs/2026-11-27-home-dwell-favorite-design.md`

## Global Constraints

- Детерминизм (ADR-0016): только `+ − × ÷` и `DeterministicMath.PowInt`, без `Math.Pow/Exp`.
- Новых входов журнала и полей снимка нет, формат снимка остаётся 4.
- `DwellGain = 0.01`, интервал обживания 60 с; `"homeAttachment": 0.3` только у `relocate`.
- У среднего голубя (Curiosity = Boldness = 50) и у любопытного множитель привязанности ровно 1.
- Тесты: `dotnet test PigeonEngine.slnx -c Debug`.

## Review Focus

- Обживание не должно создавать запись неизвестной территории. Иначе перелёт на неё потеряет
  событие «впервые на мониторе».
- Один монитор, мир без предков (эталон): обживания нет, любимой нет, хеш не меняется.
- Перенос робкого на чужой монитор: его тянет домой.
- Восстановление из снимка посреди минуты даёт тот же хеш, что непрерывный прогон.
- Предел памяти: `Dwell` не вытесняет записи.

---

### Task 1: Память — обживание и любимая с непосещёнными территориями

**Files:** `src/Pigeon.Core/PlaceMemory.cs`, тест `tests/Pigeon.Core.Tests/PlaceMemoryTests.cs`.

**Produces:**
- `MemoryRules.DwellGain` (double, последний параметр записи, по умолчанию 0.01);
- `bool PlaceMemory.Dwell(PlaceKey territory, long now)`: true — закреплено;
- `PlaceKey? PlaceMemory.Favorite(long now, IEnumerable<PlaceKey>? unvisited = null)`.

- [ ] Тесты:
  - `Dwell_StrengthensAKnownTerritory_WithoutAVisit`: после `Visit(Monitor, 0, true)` и
    `Dwell(Monitor, 0)` знакомство = 0.35 + 0.01·0.65, `Visits` = 1, `LastSeen` = 0;
    `Dwell(Monitor, 60)` двигает `LastSeen` на 60.
  - `Dwell_IgnoresUnknownPlacesAndNonTerritories`: `Dwell` неизвестного → false, записей нет;
    `Dwell(Taskbar)` после `Visit(Taskbar, territory:false)` → false, знакомство не меняется.
  - `Favorite_CountsUnvisitedTerritoriesInView`: одна территория 0.9 + `unvisited`
    `[desktop/b]` → `desktop/a`; то же с 0.7 → null; `unvisited`, совпадающая с самой
    территорией, не считается второй.
- [ ] Реализация: `Dwell` — через `Reinforce` с `countVisit: false`, только при существующей
  записи с `Territory`. `Favorite` — каждая `unvisited`, которой нет в памяти, добавляется как
  территория со знакомством 0.
- [ ] Коммит.

### Task 2: Utility AI — привязанность к дому

**Files:** `src/Pigeon.World.Abstractions/Affordance.cs`, `src/Pigeon.AI/UtilityBrain.cs`,
`src/Pigeon.AI/Behavior/BehaviorConfig.cs`, `BehaviorConfigJson.cs`, `behavior.json`.
Тесты: `tests/Pigeon.AI.Tests/UtilityBrainTests.cs`, `BehaviorConfigTests.cs`.

**Produces:**
- `static class AffordanceTerritories`: `IEnumerable<PlaceKey> Home(IEnumerable<Affordance>)`
  — различные ближайшие предки насестов и мест отдыха;
  `IEnumerable<PlaceKey> Others(IEnumerable<Affordance>)` — ключи `TerritoryZone`;
- `IntentDefinition.HomeAttachmentWeight` (JSON `homeAttachment`);
- `UtilityBrain.HomeFactor = "home"`.

- [ ] Тесты:
  - `HomeAttachment_HoldsTheTimidAtAFamiliarHome` (теория): (Curiosity, Boldness, дом) → `home`:
    (0, 0, 1.0) → 0.7; (0, 0, 0.5) → 0.85; (50, 50, 1.0) → 1.0; (100, 100, 1.0) → 1.0.
  - без насестов в affordances фактора `home` нет;
  - конфиг: `homeAttachment` без целей и вне 0..0.9 → ошибка; `relocate` в `Default` = 0.3.
- [ ] Реализация: `HomeFamiliarity` через `AffordanceTerritories.Home`. Склонность вынести в
  общий метод с новизной. Новый ключ — в `LeadingFactors` наравне с novelty (показывается при ≥ 1.1).
- [ ] Коммит.

### Task 3: Симуляция — обживание раз в минуту, тесты двух мониторов, документы

**Files:** `src/Pigeon.Simulation/PigeonSimulation.cs`; тесты
`tests/Pigeon.Simulation.Tests/MemoryLifeTests.cs`,
`tests/Pigeon.World.Desktop.Tests/TwoMonitorLifeTests.cs`. Документы: ADR-0016,
`docs/roadmap/status.md`, спек #33 (пометка про 50 минут).

- [ ] Тесты:
  - `Dwelling_RaisesTheHomeTerritory_EveryMinute`: территория рождения после 10 минут
    знакомее, чем сразу после рождения, `Visits` не растёт;
  - `Dwelling_IsReplayedAndRestored`: хеш прогона = хеш восстановления из снимка посреди
    минуты с продолжением;
  - `TheTimidSettlesAtHome_AndGetsAFavoriteMonitor` (сутки, сиды 21 и 5): у робкого
    `first-favorite-territory`; перелётов любопытного больше, чем робкого;
  - `TheCuriousFliesOffAtOnce_TheTimidStaysAtHome`: за 3 часа робкий сам не улетает,
    любопытный улетает в первые 10 минут.
- [ ] Реализация: в `ExecuteTick` после решения, если `tick % TicksOf(60 с) == 0`, —
  `Dwell` для `AffordanceTerritories.Home(_affordances)`, затем проверка любимой.
  `Remember` и обживание проверяют любимую с `AffordanceTerritories.Others(_affordances)`.
- [ ] Эталонный хеш сверить; комментарии `behavior.json`, ADR-0016, status.md.
- [ ] Коммит.
