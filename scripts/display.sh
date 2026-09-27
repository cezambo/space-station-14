#!/usr/bin/env bash
# Start the virtual display stack for headless playtests (idempotent):
# Xvfb :99 + fluxbox + x11vnc :5900 + noVNC http://localhost:6080/vnc.html
export DISPLAY=:99
pgrep -x Xvfb >/dev/null || { Xvfb :99 -screen 0 1280x800x24 +extension GLX -nolisten tcp >/tmp/xvfb.log 2>&1 & sleep 2; }
pgrep -x fluxbox >/dev/null || { fluxbox >/tmp/fluxbox.log 2>&1 & sleep 1; }
pgrep -x x11vnc >/dev/null || x11vnc -display :99 -forever -shared -nopw -rfbport 5900 -bg -o /tmp/x11vnc.log >/dev/null 2>&1
pgrep -f 'websockify.*6080' >/dev/null || { websockify --web /usr/share/novnc 6080 localhost:5900 >/tmp/novnc.log 2>&1 & }
sleep 1
pgrep -xa Xvfb >/dev/null && echo "display :99 up; noVNC on http://localhost:6080/vnc.html"
