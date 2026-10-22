#!/usr/bin/env bash
# Record a full Root match with the Spinners of Mercy plugin as evidence (evidence video 2, next to the
# Sherlock-Holmes Vagabond video). Start this BEFORE launching Root, play, then press Ctrl+C here when the match ends.
#
# Writes ~/ret-work/root/recordings/<date-time>/ (raw footage and logs stay out of the repository):
#   video.mp4        the screen, with desktop audio (gpu-screen-recorder; KDE asks once which screen to share)
#   timeline.tsv     every plugin log line as it happened, stamped with seconds since recording start, so moments in
#                    the video (Recruit, Shelter, Skitter, VP) can be found by time
#   LogOutput.log    BepInEx log of the match (copied at the end)
#   Player.log       Root's own log (copied at the end)
#   manifest.txt     plugin version and sha256, BepInEx version, start/end times, event counts, errors
#
# GAME=sherlock records the Sherlock Holmes: The Awakened mods instead (RetVagabond strike, RetS1Probe speed): the log
# is UE4SS.log, the timeline keeps [RetVagabond] / [RetS1Probe] lines, and the manifest fingerprints the Lua scripts.
#
# Needs: gpu-screen-recorder (pacman package, cachyos-extra). Without it the script still writes the timeline, logs
# and manifest, and tells you to record with Spectacle (Shift+Print in KDE, or `spectacle -R s`) by hand.
set -uo pipefail

GAME="${GAME:-root}"
if [ "$GAME" = sherlock ]; then
    W="$HOME/.local/share/Steam/steamapps/common/Sherlock Holmes The Awakened/SH3RE/Binaries/Win64"
    BEPINEX_LOG="${BEPINEX_LOG:-$W/UE4SS.log}"      # (name kept: the mod log the timeline follows)
    PLAYER_LOG=""
    DLL="$W/Mods/RetVagabond/Scripts/main.lua"
    TAGS='\[RetVagabond\]|\[RetS1Probe\]'
    OUT="${RET_REC_DIR:-$HOME/ret-work/sherlock-holmes-the-awakened/recordings}/$(date +%Y-%m-%d_%H-%M-%S)"
else
    ROOT_DIR="$HOME/.local/share/Steam/steamapps/common/Root"
    BEPINEX_LOG="${BEPINEX_LOG:-$ROOT_DIR/BepInEx/LogOutput.log}"
    PLAYER_LOG="$HOME/.local/share/Steam/steamapps/compatdata/965580/pfx/drive_c/users/steamuser/AppData/LocalLow/Dire Wolf Digital/Root/Player.log"
    DLL="$ROOT_DIR/BepInEx/plugins/RetSpinnersOfMercy.dll"
    TAGS='RET Spinners of Mercy'
    OUT="${RET_REC_DIR:-$HOME/ret-work/root/recordings}/$(date +%Y-%m-%d_%H-%M-%S)"
fi
FPS="${FPS:-60}"

mkdir -p "$OUT"
START=$(date +%s.%N)
echo "Recording into $OUT"
[ -f "$DLL" ] || echo "warning: the Spinners plugin is not installed ($DLL)"

# 1. Screen. Portal capture works on KDE Wayland. 2026-10-22: the first recording captured the Steam WINDOW for 13 minutes
#    (a window was picked in KDE's share dialog and remembered), so the choice is asked every time and must be the
#    whole screen.
REC_PID=""
if command -v gpu-screen-recorder >/dev/null; then
    gpu-screen-recorder -w portal -restore-portal-session no -f "$FPS" -k h264 -a default_output \
        -o "$OUT/video.mp4" >"$OUT/recorder.log" 2>&1 &
    REC_PID=$!
    echo "Screen recording started (gpu-screen-recorder, ${FPS} fps)."
    echo ">>> In KDE's share dialog choose the whole SCREEN (the monitor, e.g. eDP-1), NOT a window like Steam. <<<"
else
    echo "gpu-screen-recorder is not installed: record the screen yourself with Spectacle (Shift+Print, or"
    echo "'spectacle -R s'), and save the video into $OUT. Logs and the timeline are still recorded here."
fi

# 2. Timeline. tail -F follows the log by name, so it keeps working when Root starts and rewrites LogOutput.log.
: >"$OUT/timeline.tsv"
tail -F -n0 "$BEPINEX_LOG" 2>/dev/null | while IFS= read -r line; do
    if printf '%s' "$line" | grep -qE "$TAGS"; then
        now=$(date +%s.%N)
        printf '%s\t%s\n' "$(awk "BEGIN{printf \"%.1f\", $now-$START}")" "${line#*] }" >>"$OUT/timeline.tsv"
    fi
done &
TAIL_PID=$!

finish() {
    trap - INT TERM
    echo
    echo "Stopping..."
    if [ -n "$REC_PID" ] && kill -0 "$REC_PID" 2>/dev/null; then
        kill -INT "$REC_PID"            # gpu-screen-recorder finalises the file on SIGINT
        wait "$REC_PID" 2>/dev/null
    fi
    pkill -P "$TAIL_PID" 2>/dev/null; kill "$TAIL_PID" 2>/dev/null
    END=$(date +%s.%N)
    cp -f "$BEPINEX_LOG" "$OUT/$(basename "$BEPINEX_LOG")" 2>/dev/null
    [ -n "$PLAYER_LOG" ] && cp -f "$PLAYER_LOG" "$OUT/Player.log" 2>/dev/null
    if [ "$GAME" = sherlock ]; then
        {
            echo "Sherlock Holmes: The Awakened, RET mods recorded session"
            echo "start:            $(date -d "@${START%.*}" '+%Y-%m-%d %H:%M:%S')"
            echo "length (s):       $(awk "BEGIN{printf \"%.0f\", $END-$START}")"
            echo "RetVagabond sha256: $(sha256sum "$DLL" 2>/dev/null | cut -d' ' -f1)"
            echo "video:            $( [ -s "$OUT/video.mp4" ] && echo "video.mp4 ($(du -h "$OUT/video.mp4" | cut -f1))" || echo none)"
            echo "mod log lines:    $(wc -l <"$OUT/timeline.tsv")"
            echo "  RetVagabond     $(grep -c 'RetVagabond' "$OUT/timeline.tsv")"
            echo "  RetS1Probe      $(grep -c 'RetS1Probe' "$OUT/timeline.tsv")"
            echo "errors (UE4SS):   $(grep -ciE 'error|exception' "$OUT/UE4SS.log" 2>/dev/null)"
        } >"$OUT/manifest.txt"
        cat "$OUT/manifest.txt"; echo; echo "Saved in $OUT"; exit 0
    fi

    local t="$OUT/timeline.tsv"
    {
        echo "Spinners of Mercy recorded match"
        echo "start:            $(date -d "@${START%.*}" '+%Y-%m-%d %H:%M:%S')"
        echo "end:              $(date -d "@${END%.*}" '+%Y-%m-%d %H:%M:%S')"
        echo "length (s):       $(awk "BEGIN{printf \"%.0f\", $END-$START}")"
        echo "plugin version:   $(grep -m1 -oE 'RET Spinners of Mercy\] [0-9]+\.[0-9]+\.[0-9]+' "$OUT/LogOutput.log" 2>/dev/null | awk '{print $NF}')"
        echo "plugin sha256:    $(sha256sum "$DLL" 2>/dev/null | cut -d' ' -f1)"
        echo "BepInEx:          $(grep -m1 -oE 'BepInEx [0-9][^ ]*' "$OUT/LogOutput.log" 2>/dev/null)"
        echo "video:            $( [ -s "$OUT/video.mp4" ] && echo "video.mp4 ($(du -h "$OUT/video.mp4" | cut -f1))" || echo "none (recorded by hand?)")"
        echo
        echo "events in the timeline:"
        echo "  setup moves      $(grep -c 'Spinners move: ' "$t")"
        echo "  Recruit          $(grep -cE '^[0-9.]+	Recruit: [A-Z][a-z]+ \(clicked' "$t")"
        echo "  Gather           $(grep -c 'Gather: drawing' "$t")"
        echo "  Establish        $(grep -cE 'Establish: [A-Z][a-z]+ infirmary' "$t")"
        echo "  Skitter moves    $(grep -c 'Skitter: moved' "$t")"
        echo "  Shelter          $(grep -cE 'Shelter: [A-Z][a-z]+ infirmary in clearing' "$t") (VP $(grep -oE 'Shelter: .* \+[0-9] VP' "$t" | grep -oE '\+[0-9]' | tr -d + | awk '{s+=$1} END{print s+0}'))"
        echo "  Evening draws    $(grep -c 'Evening draw: ' "$t")"
        echo "  webs rule fired  $(grep -c 'Webs rule:' "$t")"
        echo
        echo "errors:"
        echo "  plugin errors    $(grep -c '^\[Error' "$OUT/LogOutput.log" 2>/dev/null)"
        echo "  game ended by an exception: $(grep -c 'Ending Game Due To' "$OUT/Player.log" 2>/dev/null)"
    } >"$OUT/manifest.txt"
    cat "$OUT/manifest.txt"
    echo
    echo "Saved in $OUT"
    exit 0
}
trap finish INT TERM

echo "Now launch the game ($GAME; Root offline), play, and press Ctrl+C here when you are done."
wait "$TAIL_PID"
