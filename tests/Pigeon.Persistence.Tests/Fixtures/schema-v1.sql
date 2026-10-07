-- База схемы версии 1 в том виде, как её создавала бы первая версия приложения.
-- Миграции обязаны поднимать её до текущей версии без потери данных.
CREATE TABLE snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    pet_id TEXT NOT NULL,
    tick INTEGER NOT NULL,
    format_version INTEGER NOT NULL,
    payload TEXT NOT NULL
);
CREATE TABLE journal (
    pet_id TEXT NOT NULL,
    position INTEGER NOT NULL,
    tick INTEGER NOT NULL,
    type TEXT NOT NULL,
    PRIMARY KEY (pet_id, position)
) WITHOUT ROWID;

INSERT INTO journal (pet_id, position, tick, type) VALUES
    ('6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11', 0, 12, 'user-poked'),
    ('6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11', 1, 25, 'user-poked');

INSERT INTO snapshots (pet_id, tick, format_version, payload) VALUES
    ('6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11', 10, 1,
     '{"formatVersion":1,"seed":1,"stepTicks":1000000,"maxTicksPerAdvance":50,"decisionIntervalTicks":10,"tick":10,"accumulatedTicks":0,"fatigue":0.02,"intent":0,"intentStartedTick":0,"randomStreams":[{"name":"ai.decision","state":1234567890123,"increment":4242424243}],"journalLength":0,"pending":[]}'),
    ('6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11', 30, 1,
     '{"formatVersion":1,"seed":1,"stepTicks":1000000,"maxTicksPerAdvance":50,"decisionIntervalTicks":10,"tick":30,"accumulatedTicks":500000,"fatigue":0.16,"intent":0,"intentStartedTick":0,"randomStreams":[{"name":"ai.decision","state":9876543210987,"increment":4242424243}],"journalLength":2,"pending":[]}');

PRAGMA user_version = 1;
