# Фикстуры Jev

Записанные пары запрос/ответ OpenRouter Decisions API для контрактных тестов адаптера
`Pigeon.AI.Jev` (ТЗ §17.4). Записаны замером S0.10 (`tools/Pigeon.JevProbe`, workflow `Jev probe`),
отчёт — [docs/research/jev-measurement.md](../../../docs/research/jev-measurement.md).

- `ok-<ситуация>.json` — успешный ответ на запрос архетипа `balanced` в варианте `plain`;
- `error-*.json` — ответы с ошибкой: неизвестная модель, запрос без вопросов, неизвестный тип
  вопроса, неверный ключ.

В каждом файле `http_status`, `request` (тело запроса, без заголовков) и `response`. Поля `id` и
`user_id` из ответов убраны. `Pigeon.Architecture.Tests` проверяет, что в фикстурах нет ключей и
идентификаторов аккаунта.

Обновить: запустить workflow `Jev probe` и взять каталог `fixtures` из артефакта.
