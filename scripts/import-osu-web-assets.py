#!/usr/bin/env python3
"""Import web image assets from a local ppy/osu-web Git checkout."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

EXTENSIONS = {'.svg', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.avif', '.ico', '.apng'}
REPOSITORY = 'https://github.com/ppy/osu-web'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('checkout', type=Path)
    args = parser.parse_args()
    source = args.checkout.resolve()
    target = Path(__file__).resolve().parents[1] / 'MintAPI/wwwroot/assets/osu-web'
    commit = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
    if subprocess.check_output(['git', '-C', str(source), 'status', '--porcelain'], text=True).strip():
        parser.error('Source checkout must be clean so the recorded commit matches the imported files.')
    files = sorted(p for p in source.rglob('*') if p.is_file() and '.git' not in p.parts and p.suffix.lower() in EXTENSIONS)
    if not files:
        parser.error('No supported image files found.')
    entries = []
    target.mkdir(parents=True, exist_ok=True)
    for path in files:
        relative = path.relative_to(source).as_posix()
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)
        entries.append({'path': relative, 'url': '/assets/osu-web/' + relative,
                        'format': path.suffix.lower()[1:], 'bytes': path.stat().st_size,
                        'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                        'sourceUrl': f'{REPOSITORY}/blob/{commit}/{relative}'})
    manifest_path = target / 'manifest.json'
    if manifest_path.exists():
        previous = json.loads(manifest_path.read_text())
        for entry in previous['assets']:
            relative = Path(entry['path'])
            if relative.is_absolute() or '..' in relative.parts:
                raise ValueError('Invalid path in previous manifest')
            if entry['path'] not in {item['path'] for item in entries}:
                (target / relative).unlink(missing_ok=True)
    shutil.copyfile(source / 'LICENCE', target / 'LICENCE')
    manifest = {'repository': REPOSITORY, 'commit': commit, 'assetCount': len(entries),
                'totalBytes': sum(e['bytes'] for e in entries),
                'formats': dict(sorted(Counter(e['format'] for e in entries).items())), 'assets': entries}
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({k: v for k, v in manifest.items() if k != 'assets'}, indent=2))


if __name__ == '__main__':
    main()
