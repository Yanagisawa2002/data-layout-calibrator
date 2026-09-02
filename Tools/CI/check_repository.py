#!/usr/bin/env python3
"""Dependency-free static checks for tracked repository metadata.

This intentionally does not import or execute benchmark/runtime code. It checks
that tracked JSON is parseable and that local inline Markdown links resolve to
tracked files or directories. URL fragments are deliberately left to renderers.
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath
from urllib.parse import unquote, urlsplit


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
INLINE_LINK = re.compile(r"!?\[[^\]]*\]\((?P<target><[^>]+>|[^)\s]+)(?:\s+['\"(].*?[\"')])?\)")
IGNORED_SCHEMES = {"data", "http", "https", "mailto"}


def tracked_paths() -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=REPOSITORY_ROOT,
        check=True,
        capture_output=True,
    )
    return [entry.decode("utf-8") for entry in result.stdout.split(b"\0") if entry]


def check_json(paths: list[str]) -> list[str]:
    errors: list[str] = []
    for relative in paths:
        if not relative.lower().endswith((".json", ".asmdef")):
            continue
        path = REPOSITORY_ROOT / Path(relative)
        try:
            with path.open("r", encoding="utf-8-sig") as stream:
                json.load(stream)
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            errors.append(f"{relative}: invalid JSON: {error}")
    return errors


def normalized_target(source: str, raw_target: str) -> str | None:
    target = raw_target[1:-1] if raw_target.startswith("<") else raw_target
    split = urlsplit(target)
    if split.scheme.lower() in IGNORED_SCHEMES or target.startswith("#"):
        return None
    if split.scheme or split.netloc:
        return None

    decoded = unquote(split.path).replace("\\", "/")
    if not decoded:
        return None

    base = PurePosixPath() if decoded.startswith("/") else PurePosixPath(source).parent
    parts: list[str] = []
    for part in (base / decoded.lstrip("/")).parts:
        if part in ("", "."):
            continue
        if part == "..":
            if parts:
                parts.pop()
            else:
                return ""
        else:
            parts.append(part)
    return PurePosixPath(*parts).as_posix()


def check_markdown(paths: list[str]) -> list[str]:
    errors: list[str] = []
    tracked = set(paths)
    tracked_directories = {
        PurePosixPath(path).parent.as_posix()
        for path in tracked
    }

    for relative in paths:
        if not relative.lower().endswith(".md"):
            continue
        path = REPOSITORY_ROOT / Path(relative)
        try:
            text = path.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeError) as error:
            errors.append(f"{relative}: cannot read Markdown: {error}")
            continue

        for match in INLINE_LINK.finditer(text):
            raw_target = match.group("target")
            target = normalized_target(relative, raw_target)
            if target is None:
                continue
            line = text.count("\n", 0, match.start()) + 1
            if not target or (target not in tracked and target not in tracked_directories):
                errors.append(
                    f"{relative}:{line}: local link does not resolve to a tracked path: {raw_target}"
                )
    return errors


def main() -> int:
    paths = tracked_paths()
    errors = check_json(paths) + check_markdown(paths)
    if errors:
        print("Repository static checks failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    json_count = sum(path.lower().endswith((".json", ".asmdef")) for path in paths)
    markdown_count = sum(path.lower().endswith(".md") for path in paths)
    print(f"Validated {json_count} JSON/asmdef files and local links in {markdown_count} Markdown files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
