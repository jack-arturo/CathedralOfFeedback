#!/usr/bin/env bash
# Build the Quest APK in an isolated clone of the project, validate it, and optionally
# install + launch it on the connected headset.
#
#   scripts/build-quest.sh             build only
#   scripts/build-quest.sh --install   build, install, launch, verify the foreground activity
#
# The build runs in a copy (APFS copy-on-write, so it costs no real disk) so the working
# tree can keep changing while Unity is busy, and a batch build never fights an open Editor.
# Set CATHEDRAL_BUILD_CLONE to choose where the copy lives, ANDROID_SERIAL to pick a device.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(awk '/^m_EditorVersion:/{print $2}' "$ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
CLONE="${CATHEDRAL_BUILD_CLONE:-${TMPDIR:-/tmp}/cathedral-build-clone}"
PKG="com.autojack.cathedraloffeedback"
APK_REL="Builds/CathedralOfFeedback.apk"
STAMP="$(date +%Y%m%d-%H%M%S)"
LOG="$ROOT/Logs/build-android-$STAMP.log"
INSTALL=0
[[ "${1:-}" == "--install" ]] && INSTALL=1

[[ -x "$UNITY" ]] || { echo "Unity $VERSION not found at $UNITY" >&2; exit 2; }
mkdir -p "$CLONE" "$ROOT/Logs" "$ROOT/Builds"

# One build at a time per clone.
LOCK="$CLONE/.build.lock"
if ! mkdir "$LOCK" 2>/dev/null; then echo "Another build holds $LOCK" >&2; exit 3; fi
trap 'rmdir "$LOCK" 2>/dev/null || true' EXIT

echo "[build] syncing working tree -> $CLONE"
[[ -d "$CLONE/Library" ]] || cp -Rc "$ROOT/Library" "$CLONE/Library" 2>/dev/null || cp -R "$ROOT/Library" "$CLONE/Library"
for d in Assets Packages ProjectSettings; do
  rsync -a --delete "$ROOT/$d/" "$CLONE/$d/"
done

rm -f "$CLONE/$APK_REL"
echo "[build] Unity $VERSION batch build (log: $LOG)"
START=$(date +%s)
set +e
"$UNITY" -batchmode -quit -projectPath "$CLONE" -buildTarget Android \
  -executeMethod CathedralBuilder.BuildAndroid -logFile "$LOG"
CODE=$?
set -e
echo "[build] Unity exit $CODE after $(( $(date +%s) - START ))s"
if [[ $CODE -ne 0 ]]; then
  grep -nE "error CS|Shader error|Build result|BuildFailedException|Error building" "$LOG" | tail -20 >&2 || true
  exit 1
fi
[[ -s "$CLONE/$APK_REL" ]] || { echo "[build] no APK produced" >&2; exit 1; }
if command -v apkanalyzer >/dev/null; then
  ID="$(apkanalyzer manifest application-id "$CLONE/$APK_REL" 2>/dev/null || true)"
  [[ -z "$ID" || "$ID" == "$PKG" ]] || { echo "[build] APK package is '$ID', expected $PKG" >&2; exit 1; }
fi
cp "$CLONE/$APK_REL" "$ROOT/$APK_REL"
echo "[build] APK $(du -h "$ROOT/$APK_REL" | cut -f1) sha256 $(shasum -a 256 "$ROOT/$APK_REL" | cut -c1-16)"

[[ $INSTALL -eq 1 ]] || exit 0

DEVICES=($(adb devices | awk 'NR>1 && $2=="device"{print $1}'))
if [[ -z "${ANDROID_SERIAL:-}" ]]; then
  [[ ${#DEVICES[@]} -eq 1 ]] || { echo "[install] ${#DEVICES[@]} devices attached; set ANDROID_SERIAL" >&2; exit 4; }
  export ANDROID_SERIAL="${DEVICES[0]}"
fi
echo "[install] $ANDROID_SERIAL"
adb install -r "$ROOT/$APK_REL" | tail -1
adb shell am force-stop "$PKG"
ACTIVITY="$(adb shell cmd package resolve-activity --brief "$PKG" | tail -1 | tr -d '\r')"

# Horizon OS keeps an app's audio muted (dumpsys audio: mutedState:appOps) for its whole
# lifetime if it is launched while the headset is asleep. So: tell the proximity sensor the
# headset is worn, wake it, launch, confirm the audio player is unmuted, then give the
# sensor back. A normally launched app survives later sleep/wake cycles unmuted.
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
adb shell input keyevent KEYCODE_WAKEUP
sleep 2
adb shell am start -n "$ACTIVITY" >/dev/null
sleep 8
PID="$(adb shell pidof "$PKG" | tr -d '\r' || true)"
RESUMED="$(adb shell dumpsys activity activities | grep -E 'topResumedActivity|mResumedActivity' | head -1 | tr -d '\r' || true)"
APPUID="$(adb shell pm list packages -U "$PKG" | tr -d '\r' | sed -E 's/.*uid:([0-9]+).*/\1/')"
MUTED="$(adb shell dumpsys audio | grep -E 'AudioPlaybackConfiguration piid:' | grep "u/pid:$APPUID/" | grep AAudio | grep -oE 'mutedState:[a-zA-Z]+' | head -1 || true)"
adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null
echo "[install] pid=${PID:-none} audio ${MUTED:-no player yet} ${RESUMED:-no resumed activity}"
[[ -n "$PID" ]] || { echo "[install] app is not running" >&2; exit 5; }
[[ "$MUTED" != "mutedState:appOps" ]] || { echo "[install] the OS has muted the app's audio; relaunch it from the headset" >&2; exit 6; }
