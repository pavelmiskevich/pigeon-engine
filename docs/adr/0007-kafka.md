# ADR-0007 — Kafka

- **Статус:** Accepted
- **Дата:** 2026-09-28

## Решение

> Kafka — Online/Ecosystem event backbone, не Desktop runtime dependency.

Подключается, когда объём событий этого требует. Публикация — через transactional outbox
(ADR-0018).
