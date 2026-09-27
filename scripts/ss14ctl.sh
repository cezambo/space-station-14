#!/usr/bin/env bash
# Helpers to drive the SS14 client on a virtual X display (Xvfb) for automated playtests.
# Usage: source scripts/ss14ctl.sh   (expects DISPLAY, the game client window, and LOG)
#   ss_con "<console command>"      run a console command (opens/closes the ~ console)
#   ss_read "<vv path>"             run vvread and print the fresh result (waits for new output)
#   ss_focus                        return keyboard focus to the game viewport
#   ss_walk <w|a|s|d> <seconds>     hold a movement key
#   ss_shot <file.png>              screenshot the whole display

export DISPLAY="${DISPLAY:-:99}"
LOG="${LOG:-/tmp/ss14-launch.log}"
SS_WIN="${SS_WIN:-$(xdotool search --name 'Space Station 14' 2>/dev/null | head -1)}"

# Keyboard focus only. Do NOT send Escape (toggles the game menu) or click (interacts with the world).
# Never click y>=770: the fluxbox toolbar lives there and clicking it iconifies the game window.
ss_focus() {
    xdotool windowmap --sync "$SS_WIN" 2>/dev/null
    xdotool windowraise "$SS_WIN" 2>/dev/null; xdotool windowfocus --sync "$SS_WIN" 2>/dev/null
    sleep 0.1
}

ss_con() {
    ss_focus
    xdotool key grave; sleep 0.9
    xdotool type --delay 25 "$1"; xdotool key Return; sleep 0.6
    xdotool key grave; sleep 0.4
    ss_focus
}

# Prints the first output line after the most recent "> vvread <path>" echo, waiting until it appears.
ss_read() {
    local path="$1" before after i
    before=$(grep -ac "CON: > vvread $path\$" "$LOG")
    ss_con "vvread $path"
    for i in $(seq 1 40); do
        after=$(grep -ac "CON: > vvread $path\$" "$LOG")
        if [ "$after" -gt "$before" ]; then
            local line
            line=$(grep -anA1 "CON: > vvread $path\$" "$LOG" | tail -1)
            case "$line" in *"CON: > vvread"*) ;; *) echo "${line#*CON: }"; return 0;; esac
        fi
        sleep 0.25
    done
    echo "<timeout>"
    return 1
}

ss_walk() { xdotool keydown "$1"; sleep "$2"; xdotool keyup "$1"; }

ss_shot() { import -window root "$1"; }
