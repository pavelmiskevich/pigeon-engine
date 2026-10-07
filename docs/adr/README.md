# Architecture Decision Records

Каждое архитектурное решение — отдельный файл `NNNN-название.md`.

Статусы: **Proposed** (предложено, ждёт подтверждения владельца) → **Accepted** → при замене
**Superseded by NNNN**. **Pending** — решение принимается по итогам исследования.

Принятое решение не переписывается: изменение оформляется новым ADR или разделом «Уточнение»
с датой.

| ADR | Решение | Статус |
|-----|---------|--------|
| [0001](0001-one-engine-two-worlds.md) | Один Engine, два мира | Accepted |
| [0002](0002-desktop-uses-real-world.md) | Desktop использует реальный мир | Accepted, уточнено |
| [0003](0003-local-first.md) | Local-First | Accepted |
| [0004](0004-sqlite-default.md) | SQLite по умолчанию | Accepted |
| [0005](0005-postgresql.md) | PostgreSQL | Accepted, частично заменён 0019 |
| [0006](0006-redis.md) | Redis | Accepted |
| [0007](0007-kafka.md) | Kafka | Accepted |
| [0008](0008-jev.md) | Jev | Accepted, уточнено |
| [0009](0009-ai-safety.md) | AI Safety | Accepted |
| [0010](0010-pigeon-first.md) | Pigeon First | Accepted |
| [0011](0011-platform-capability-matrix.md) | Матрица возможностей и уровни деградации | Accepted |
| [0012](0012-mvp-0-technical-foundation.md) | MVP-0 до MVP-1 | Accepted |
| [0013](0013-shared-intents-world-specific-physics.md) | Общие намерения, раздельные физика и анимация | Accepted |
| [0014](0014-browser-runtime.md) | Browser runtime | Proposed |
| [0015](0015-autonomous-always-present.md) | Autonomous присутствует всегда | Proposed |
| [0016](0016-determinism-contract.md) | Контракт детерминизма | Proposed |
| [0017](0017-persistence-journal-snapshots.md) | Журнал + снимки + миграции | Proposed |
| [0018](0018-domain-vs-integration-events.md) | Доменные и интеграционные события | Proposed |
| [0019](0019-no-postgresql-on-desktop.md) | Нет PostgreSQL на Desktop | Proposed |
| [0020](0020-privacy-scope-v1.md) | Privacy scope v1 | Proposed |
| [0021](0021-desktop-rendering-stack.md) | Стек отрисовки Desktop | Pending |
| [0022](0022-migratory-pigeon.md) | Перелётный голубь: всегда в одном мире | Proposed |
