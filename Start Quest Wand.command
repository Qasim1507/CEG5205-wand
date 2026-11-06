#!/bin/zsh
cd "${0:A:h}" || exit 1
if [[ ! -x .venv/bin/python ]]; then
    echo 'Double-click Setup Quest Wand.command first (see README.md).'
    read '?Press Enter to close.'
    exit 1
fi
.venv/bin/python -u python/quest_wand.py "$@"
wand_run_status=$?
read '?Stopped. Press Enter to close.'
exit $wand_run_status
