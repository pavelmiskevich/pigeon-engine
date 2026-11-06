# Результаты pigeon-probe

Отчёты утилиты `pigeon-probe` (MVP-0, S0.3), по которым заполнен столбец «Проверено» в
[матрице возможностей](../capability-matrix.md). В отчётах нет заголовков окон, имён приложений,
путей, имени машины и пользователя.

| Файл | Окружение |
|------|-----------|
| `windows-11-laptop.json` | Windows 11, ноутбук, один монитор |
| `windows-server-2022-ci-runner.json` | Windows Server 2022, раннер GitHub |
| `macos-26-ci-runner.json` | macOS 26, раннер GitHub (Apple Silicon) |
| `macos-27-macbook.txt` | macOS 27.2, MacBook на Apple Silicon, без разрешений; текстовый отчёт |
| `linux-headless/x11-*.json` | Ubuntu 24.04 в контейнере: Xvfb без оконного менеджера, openbox, openbox + picom |
| `linux-headless/wayland-*.json` | Ubuntu 24.04 в контейнере: Sway, Weston, Mutter и KWin в headless-режиме |

## Как снять отчёт

**Windows:**

```
dotnet run --project tools/Pigeon.Probe -c Release -- --json > probe.json
```

**macOS (Apple Silicon)** — готовая сборка лежит в артефакте `probe-macos-arm64` прогона CI:

1. Распаковать архив, открыть «Терминал» в папке с файлом `pigeon-probe`.
2. Снять карантин, который macOS ставит на скачанные файлы:
   `xattr -d com.apple.quarantine pigeon-probe && chmod +x pigeon-probe`
3. Запустить: `./pigeon-probe --json > probe.json` и прислать `probe.json`.

Утилита не создаёт окон, ничего не устанавливает и не запрашивает разрешений. Если macOS всё же
покажет запрос доступа, его можно отклонить: отказ — тоже результат.

**Linux, headless-окружения** — в контейнере, на хост ничего не ставится:

```
dotnet publish tools/Pigeon.Probe -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:InvariantGlobalization=true -o artifacts/probe/linux-x64
docker run --rm -v "$PWD/artifacts/probe/linux-x64:/probe:ro" \
  -v "$PWD/tools/probe-linux:/scripts:ro" -v "$PWD/artifacts/probe/out:/out" \
  ubuntu:24.04 bash /scripts/run-headless.sh
```

**Linux, живая сессия:** `./pigeon-probe --json`, а в Wayland-сессии ещё `./pigeon-probe --json --x11`
для XWayland.
