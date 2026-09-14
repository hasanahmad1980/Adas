"""Counts DLSS 5 result issues (filed via .github/ISSUE_TEMPLATE/community-result.yml) into community/results.json.

Each GitHub account counts once per game + route + GPU (its newest report wins). Issues closed as
"not planned" (spam/invalid) and issues without the community-result label are ignored.
"""
import json
import re
import sys

ROUTE = re.compile(r"^[A-Za-z][A-Za-z0-9]{2,40}(\+[A-Za-z]{3,30})?$")
GPU = re.compile(r"^(RTX|GTX) A?\d{3,4}( (Ti|SUPER|Laptop|Ti SUPER))*$", re.IGNORECASE)


def load_pages(path):
    # gh --paginate writes one JSON array per page back to back; decode them one at a time.
    text = open(path, encoding="utf-8").read()
    decoder, items, index = json.JSONDecoder(), [], 0
    while True:
        while index < len(text) and text[index].isspace():
            index += 1
        if index >= len(text):
            return items
        page, index = decoder.raw_decode(text, index)
        items.extend(page)


def fields(body):
    out = {}
    for match in re.finditer(r"^###\s+(.+?)\s*\n+(.*?)(?=^###\s|\Z)", body or "", re.M | re.S):
        value = match.group(2).strip()
        out[match.group(1).strip().lower()] = "" if value == "_No response_" else value
    return out


def game_key(name):
    return "".join(ch for ch in name.lower() if ch.isalnum())


def main(src, dst):
    latest = {}
    for issue in load_pages(src):
        if "pull_request" in issue:
            continue
        if not any(label.get("name") == "community-result" for label in issue.get("labels", [])):
            continue
        if issue.get("state_reason") == "not_planned":
            continue
        f = fields(issue.get("body"))
        game = f.get("game", "")[:120]
        route = f.get("route", "")
        result = f.get("result", "")
        gpu = f.get("gpu", "")
        key = game_key(game)
        if not key or not ROUTE.match(route) or result not in ("It worked", "It didn't work"):
            continue
        if gpu and not GPU.match(gpu):
            gpu = ""
        user = (issue.get("user") or {}).get("login", "")
        ident = (user.lower(), key, route, gpu.lower())
        stamp = issue.get("updated_at") or issue.get("created_at") or ""
        if ident not in latest or stamp > latest[ident][0]:
            latest[ident] = (stamp, game, route, gpu, result == "It worked")

    games = {}
    for _, game, route, gpu, worked in latest.values():
        key = game_key(game)
        rows = games.setdefault(key, {})
        row = rows.setdefault((route, gpu.upper() if gpu else ""), {"route": route, "gpu": gpu, "worked": 0, "failed": 0})
        row["worked" if worked else "failed"] += 1

    output = {
        "version": 1,
        "games": {k: sorted(v.values(), key=lambda r: (r["route"], r["gpu"])) for k, v in sorted(games.items())},
    }
    with open(dst, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(output, handle, indent=1, ensure_ascii=False)
        handle.write("\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
