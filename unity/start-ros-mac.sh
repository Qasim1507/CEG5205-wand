#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

usage() {
  echo "Usage: $0 {2|3|4Sim|stop|status|logs}"
  exit 2
}

stage="${1:-3}"
image="unity-robotics:desktop-target-noetic-mac"
container=""
launch=""
case "$stage" in
  2) container="unity-pnp-part2"; launch="part_2.launch" ;;
  3) container="unity-pick-and-place-ros"; launch="part_3.launch" ;;
  4Sim) container="unity-pnp-part4-sim"; launch="part_4_sim.launch" ;;
  stop|status|logs) ;;
  *) usage ;;
esac

command -v colima >/dev/null || { echo "Install Colima: brew install colima docker" >&2; exit 1; }
command -v docker >/dev/null || { echo "Install Docker CLI: brew install docker" >&2; exit 1; }

if ! colima status >/dev/null 2>&1; then
  if [[ "$stage" == stop || "$stage" == status || "$stage" == logs ]]; then
    echo "Colima is stopped."
    exit 0
  fi
  colima start --cpus 4 --memory 8 --disk 40 --vm-type vz --vz-rosetta
fi

dock() { docker --context colima "$@"; }
names=(unity-pnp-part2 unity-pick-and-place-ros unity-pnp-part4-sim)

case "$stage" in
  stop)
    for name in "${names[@]}"; do dock stop "$name" >/dev/null 2>&1 || true; done
    echo "ROS containers stopped."
    exit 0 ;;
  status)
    dock ps -a --filter 'name=unity-' --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
    exit 0 ;;
  logs)
    running="$(dock ps --filter 'publish=10000' --format '{{.Names}}' | head -n 1)"
    [[ -n "$running" ]] || { echo "No ROS stage is running." >&2; exit 1; }
    dock logs --tail 100 -f "$running"
    exit 0 ;;
esac

if ! dock image inspect "$image" >/dev/null 2>&1; then
  dock build --platform linux/amd64 -t "$image" -f docker/Dockerfile.noetic .
fi

for name in "${names[@]}"; do
  [[ "$name" == "$container" ]] || dock stop "$name" >/dev/null 2>&1 || true
done

existing="$(dock ps -a --filter "name=^/${container}$" --format '{{.Names}}')"
if [[ -n "$existing" ]]; then
  current_image="$(dock container inspect --format '{{.Image}}' "$container")"
  wanted_image="$(dock image inspect --format '{{.Id}}' "$image")"
  if [[ "$current_image" != "$wanted_image" ]]; then
    dock rm -f "$container" >/dev/null
    existing=""
  fi
fi

if [[ -n "$existing" ]]; then
  dock start "$container" >/dev/null
else
  dock run -d --platform linux/amd64 --name "$container" -p 127.0.0.1:10000:10000 \
    "$image" roslaunch niryo_moveit "$launch" >/dev/null
fi

echo "ROS Part $stage is starting at 127.0.0.1:10000."
echo "Logs: $0 logs"
