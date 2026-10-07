#!/usr/bin/env python3
"""Posts a release's notes to the Telegram channel.

Usage: telegram.py <tag>              release-notes/<tag>.html plus download links
       telegram.py posts/<name>.html   any other post, as written
(TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID and GITHUB_REPOSITORY in the environment)
Posts are Telegram's HTML subset: <b>, <i>, <a>, <code>.
"""
import json
import os
import sys
import urllib.request
from pathlib import Path

arg = sys.argv[1]
root = Path(__file__).resolve().parents[2]
repo = os.environ["GITHUB_REPOSITORY"]
if arg.endswith(".html"):
    notes = root / arg
    link = f'<a href="https://github.com/{repo}/releases/latest">⬇️ Скачать Lumi</a>'
else:
    notes = root / "release-notes" / f"{arg}.html"
    link = f'<a href="https://github.com/{repo}/releases/tag/{arg}">⬇️ Скачать {arg} для Mac и Windows</a>'
if not notes.exists():
    sys.exit(f"no {notes.name}, nothing to post")

text = notes.read_text().strip() + "\n\n" + link
if len(text) > 4096:
    sys.exit(f"{notes.name} is {len(text)} characters with the link; Telegram allows 4096")

request = urllib.request.Request(
    f"https://api.telegram.org/bot{os.environ['TELEGRAM_BOT_TOKEN']}/sendMessage",
    data=json.dumps({
        "chat_id": os.environ["TELEGRAM_CHAT_ID"],
        "text": text,
        "parse_mode": "HTML",
        "link_preview_options": {"is_disabled": True},
    }).encode(),
    headers={"Content-Type": "application/json"},
)
try:
    with urllib.request.urlopen(request) as response:
        print("posted:", json.load(response)["result"]["message_id"])
except urllib.error.HTTPError as e:
    # Telegram explains the problem (bad token, bot not an admin, broken HTML) in the body.
    sys.exit(f"Telegram refused the post ({e.code}): {e.read().decode()}")
