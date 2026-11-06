#!/bin/zsh
cd "${0:A:h}" || exit 1
wand_python_executable=""
for candidate in python3 python3.14 python3.13 python3.12; do
    if command -v "$candidate" >/dev/null && "$candidate" -c 'import sys; sys.exit(sys.version_info < (3, 12))' 2>/dev/null; then
        wand_python_executable="$candidate"
        break
    fi
done
if [[ -z "$wand_python_executable" ]]; then
    echo 'Install Python 3.12, 3.13 or 3.14 from https://www.python.org/downloads/ then run this again.'
    read '?Press Enter to close.'
    exit 1
fi
"$wand_python_executable" setup_wand.py "$@"
setup_status=$?
read '?Press Enter to close.'
exit $setup_status
