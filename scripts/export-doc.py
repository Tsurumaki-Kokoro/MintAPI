#!/usr/bin/env python3
"""Export a repository Markdown document for a standalone Release attachment."""
import argparse
from pathlib import Path
import re
from urllib.parse import quote, urlsplit

ROOT = Path(__file__).resolve().parents[1]


def export(source: Path, destination: Path, ref: str):
    source = source.resolve()

    def link(match):
        target = match[2]
        if target.startswith(('#', '/', 'http:', 'https:', 'mailto:')):
            return match[0]
        parsed = urlsplit(target)
        path = (source.parent / parsed.path).resolve().relative_to(ROOT)
        url = f'https://github.com/Tsurumaki-Kokoro/MintAPI/blob/{quote(ref, safe="")}/{quote(path.as_posix(), safe="/")}'
        if parsed.fragment:
            url += '#' + parsed.fragment
        return f'[{match[1]}]({url})'

    text = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', link, source.read_text(encoding='utf-8'))
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(text, encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--ref', required=True)
    args = parser.parse_args()
    export(args.source, args.destination, args.ref)
