#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
case "${1:-hands}" in
  hands) apk="Builds/QuestHandTracking.apk" ;;
  viewer) apk="Builds/QuestRobotViewer.apk" ;;
  *) echo "Usage: $0 [hands|viewer]" >&2; exit 2 ;;
esac

unity_adb="/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
if [[ -x "$unity_adb" ]]; then
  adb_bin="$unity_adb"
else
  adb_bin="$(command -v adb || true)"
fi
[[ -n "$adb_bin" ]] || { echo "ADB is missing. Install android-platform-tools." >&2; exit 1; }
devices="$("$adb_bin" devices | awk 'NR > 1 && $2 == "device" { print $1 }')"
count="$(printf '%s\n' "$devices" | sed '/^$/d' | wc -l | tr -d ' ')"
if [[ "$count" != 1 ]]; then
  echo "Expected one authorized Quest device; found $count. Connect it and accept USB debugging in the headset." >&2
  "$adb_bin" devices -l >&2
  exit 1
fi
"$adb_bin" -s "$devices" install -r "$apk"
