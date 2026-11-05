#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "$0")" && pwd)"
editor="/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app"
[[ -d "$editor" ]] || { echo "Unity 6000.6.4f1 is not installed." >&2; exit 1; }
open -a "$editor" --args -projectPath "$project_dir"
