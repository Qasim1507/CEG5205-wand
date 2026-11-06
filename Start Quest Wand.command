#!/bin/zsh
cd "${0:A:h}" || exit 1
if [[ ! -x .venv/bin/python ]]; then
    echo 'Create .venv and install python/requirements.txt first (see LAPTOP_QUEST.md).'
    read '?Press Enter to close.'
    exit 1
fi
.venv/bin/python -u python/quest_wand.py "$@"
read '?Stopped. Press Enter to close.'
