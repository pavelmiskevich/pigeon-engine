#!/usr/bin/env bash
# Прогон pigeon-probe в headless-окружениях Linux (MVP-0, S0.3): X11 (Xvfb без оконного
# менеджера, с openbox, с openbox и композитором picom) и Wayland-композиторы в headless-режиме
# (Sway/wlroots, Weston, Mutter/GNOME, KWin/KDE). Проверка на уровне API и протоколов, без
# физического экрана.
#
# Запуск в контейнере, ничего не ставит на хост:
#   dotnet publish tools/Pigeon.Probe -c Release -r linux-x64 --self-contained \
#     -p:PublishSingleFile=true -p:InvariantGlobalization=true -o artifacts/probe/linux-x64
#   docker run --rm -v "$PWD/artifacts/probe/linux-x64:/probe:ro" \
#     -v "$PWD/tools/probe-linux:/scripts:ro" -v "$PWD/artifacts/probe/out:/out" \
#     ubuntu:24.04 bash /scripts/run-headless.sh
#
# Результат — по одному JSON на окружение в /out. В JSON нет имени машины и пользователя.
set -u

export DEBIAN_FRONTEND=noninteractive
# Тома с Windows-хоста приходят без бита исполнения.
cp /probe/pigeon-probe /tmp/pigeon-probe && chmod +x /tmp/pigeon-probe
PROBE=/tmp/pigeon-probe
OUT=/out
mkdir -p "$OUT"
export XDG_RUNTIME_DIR=/tmp/xdg
mkdir -p -m 700 "$XDG_RUNTIME_DIR"

echo "Установка окружений..."
apt-get update -qq
apt-get install -y -qq --no-install-recommends \
  xvfb openbox picom x11-utils libxss1 libxrandr2 libxext6 \
  libwayland-client0 sway weston mutter kwin-wayland dbus dbus-x11 \
  libcap2-bin ca-certificates > /dev/null 2>&1 || echo "часть пакетов не установилась"

# У kwin_wayland есть file capabilities (cap_sys_nice); в непривилегированном контейнере
# такой бинарник не запускается, поэтому снимаем их в копии внутри контейнера.
setcap -r "$(command -v kwin_wayland)" 2> /dev/null

run() {
  local name=$1
  shift
  "$PROBE" --json "$@" > "$OUT/$name.json" 2> "$OUT/$name.err"
  local level
  level=$(grep -o '"level": "L[0-3]"' "$OUT/$name.json" | head -1)
  echo "$name: ${level:-ошибка, см. $name.err}"
}

wait_socket() {
  for _ in $(seq 1 50); do
    [ -S "$XDG_RUNTIME_DIR/$1" ] && return 0
    sleep 0.2
  done
  return 1
}

stop_all() {
  pkill -f "Xvfb|openbox|picom|sway|weston|mutter|kwin_wayland" 2> /dev/null
  sleep 1
}

# --- X11 ---
Xvfb :1 -screen 0 1920x1080x24 > /dev/null 2>&1 &
sleep 1
export DISPLAY=:1
run x11-xvfb-no-wm
openbox > /dev/null 2>&1 &
sleep 1
run x11-openbox
picom --backend xrender > /dev/null 2>&1 &
sleep 1
run x11-openbox-picom
stop_all
unset DISPLAY

# --- Wayland: wlroots ---
WLR_BACKENDS=headless WLR_LIBINPUT_NO_DEVICES=1 WAYLAND_DISPLAY= sway -c /dev/null > "$OUT/sway.log" 2>&1 &
if wait_socket wayland-1; then
  WAYLAND_DISPLAY=wayland-1 XDG_CURRENT_DESKTOP=sway run wayland-sway
else
  echo "wayland-sway: композитор не запустился, см. sway.log"
fi
stop_all

# --- Wayland: Weston ---
weston --backend=headless --socket=wayland-weston > "$OUT/weston.log" 2>&1 &
if wait_socket wayland-weston; then
  WAYLAND_DISPLAY=wayland-weston XDG_CURRENT_DESKTOP=weston run wayland-weston
else
  echo "wayland-weston: композитор не запустился, см. weston.log"
fi
stop_all

# --- Wayland: Mutter (GNOME) ---
dbus-run-session -- mutter --headless --wayland --no-x11 --wayland-display=wayland-mutter \
  --virtual-monitor 1920x1080 > "$OUT/mutter.log" 2>&1 &
if wait_socket wayland-mutter; then
  WAYLAND_DISPLAY=wayland-mutter XDG_CURRENT_DESKTOP=GNOME run wayland-mutter
else
  echo "wayland-mutter: композитор не запустился, см. mutter.log"
fi
stop_all

# --- Wayland: KWin (KDE) ---
dbus-run-session -- kwin_wayland --virtual --socket wayland-kwin --width 1920 --height 1080 \
  > "$OUT/kwin.log" 2>&1 &
if wait_socket wayland-kwin; then
  WAYLAND_DISPLAY=wayland-kwin XDG_CURRENT_DESKTOP=KDE run wayland-kwin
else
  echo "wayland-kwin: композитор не запустился, см. kwin.log"
fi
stop_all

echo "Готово: $OUT"
