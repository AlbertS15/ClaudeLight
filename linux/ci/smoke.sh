#!/bin/bash
# Starts Lumi on a virtual X display, opens the bar through `lumi --toggle`, types a search, and screenshots each step.
set -uo pipefail
cd "$(dirname "$0")/../.."
mkdir -p dist/screenshots
APP=dist/linux/lumi

"$APP" > dist/screenshots/lumi.log 2>&1 &
PID=$!
sleep 10
import -window root dist/screenshots/1-welcome.png

"$APP" --toggle
sleep 3
xdotool type --delay 80 "text"
sleep 3
import -window root dist/screenshots/2-bar-search.png

xdotool key Escape Escape
sleep 1
"$APP" --toggle
sleep 2
xdotool type --delay 60 "how many centimeters in an inch?"
sleep 2
import -window root dist/screenshots/3-bar-question.png

if kill -0 $PID 2>/dev/null; then
  echo "Lumi is still running"
  kill $PID
else
  echo "Lumi exited early:"
  cat dist/screenshots/lumi.log
  exit 1
fi
cat dist/screenshots/lumi.log
