#!/usr/bin/env python3
"""Build-host only. Produce a self-contained, platform-specific Release archive."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def run(*args, **kwargs):
    subprocess.run([str(x) for x in args], check=True, cwd=ROOT, **kwargs)


def sha256(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def download(url, target):
    if not url.startswith('https://'):
        raise ValueError('Dependency download must use HTTPS')
    with urllib.request.urlopen(url, timeout=120) as source, target.open('wb') as output:
        shutil.copyfileobj(source, output)


def package(rid, version, output, with_mysql=True):
    expected = 'win-x64' if platform.system() == 'Windows' else 'linux-x64'
    if rid != expected or platform.machine().lower() not in ('x86_64', 'amd64'):
        raise RuntimeError('Build on the target OS/architecture; native Rust libraries cannot be cross-published implicitly.')
    if rid == 'linux-x64':
        libc, release = platform.libc_ver()
        if libc != 'glibc' or tuple(int(x) for x in release.split('.')[:2]) > (2, 36):
            raise RuntimeError('Use deploy/build-linux.Dockerfile (Debian 12 baseline), not a newer host glibc.')
    config = json.loads((ROOT / 'deploy/dependencies.json').read_text())
    sibling = ROOT.parent / 'mint-osuapi'
    actual = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=sibling, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=sibling, text=True).strip()
    if actual != config['mintOsuApi']['revision'] or dirty:
        raise RuntimeError('mint-osuapi must be clean and checked out at deploy/dependencies.json revision')
    output = output.resolve()
    stage = output / f'mintapi-{version}-{rid}'
    if stage.exists():
        raise FileExistsError(stage)
    app = stage / 'app'
    app.mkdir(parents=True)
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    # Native engine and FFI are built here; operators never need Cargo or an SDK.
    run(sys.executable, ROOT / 'scripts/install-preview-cli.py')
    run('dotnet', 'publish', 'MintAPI/MintAPI.csproj', '-c', 'Release', '-r', rid, '--self-contained', 'true',
        '-o', app, f'-p:Version={version}', f'-p:SourceRevisionId={revision}',
        f'-p:BuildTimestamp={datetime.now(timezone.utc).isoformat()}', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false')
    native_name = 'rosu_pp_ffi.dll' if rid.startswith('win') else 'librosu_pp_ffi.so'
    shutil.copy2(ROOT / 'native/rosu-pp-ffi/target/release' / native_name, app)
    # Never ship development credentials, build caches, or mutable local application data.
    for name in ('appsettings.Development.json',):
        (app / name).unlink(missing_ok=True)
    for name in ('cache', 'logs', 'token_cache'):
        shutil.rmtree(app / name, ignore_errors=True)
    node = app / '.playwright/node' / ('win32_x64/node.exe' if rid.startswith('win') else 'linux-x64/node')
    env = dict(os.environ, PLAYWRIGHT_BROWSERS_PATH=str(app / 'browsers'))
    run(node, app / '.playwright/package/cli.js', 'install', '--only-shell', *(['--with-deps'] if rid == 'linux-x64' else []), 'chromium', env=env)
    launcher = 'install.ps1' if rid.startswith('win') else 'install.sh'
    shutil.copy2(ROOT / 'deploy' / launcher, stage)
    shutil.copy2(ROOT / 'LICENSE', stage)
    run(sys.executable, ROOT / 'scripts/export-doc.py', ROOT / 'docs/deployment.md', stage / 'DEPLOYMENT.md', '--ref', f'v{version}')
    shutil.copy2(ROOT / 'deploy/THIRD_PARTY_NOTICES.md', stage)
    if rid.startswith('win') and with_mysql:
        archive = stage / 'mysql.zip'
        download(config['windowsMySql']['url'], archive)
        config['windowsMySql']['sha256'] = sha256(archive)
        with zipfile.ZipFile(archive) as source:
            source.extractall(stage / 'mysql-extract')
        top = next((stage / 'mysql-extract').iterdir())
        shutil.move(str(top), app / 'deployment/mysql')
        shutil.rmtree(stage / 'mysql-extract')
        archive.unlink()
        runtime = app / 'deployment/vc_redist.x64.exe'
        download(config['windowsVcRuntime']['url'], runtime)
        config['windowsVcRuntime']['sha256'] = sha256(runtime)
    (app / 'deployment/dependencies.json').write_text(json.dumps(config, indent=2) + '\n')
    # Execute from a path outside the source tree and without database/osu! credentials.
    exe = app / ('MintAPI.exe' if rid.startswith('win') else 'MintAPI')
    run(exe, '--self-test', '--output', output / f'smoke-{rid}.png', env=env)
    manifest = {str(p.relative_to(stage)).replace('\\', '/'): sha256(p) for p in sorted(stage.rglob('*')) if p.is_file()}
    (stage / 'manifest.sha256.json').write_text(json.dumps(manifest, indent=2) + '\n')
    if rid.startswith('win'):
        result = Path(shutil.make_archive(str(stage), 'zip', root_dir=output, base_dir=stage.name))
    else:
        result = Path(str(stage) + '.tar.gz')
        with tarfile.open(result, 'w:gz') as target:
            target.add(stage, arcname=stage.name)
    result.with_name(result.name + '.sha256').write_text(f'{sha256(result)}  {result.name}\n')
    print(f'{result}: {result.stat().st_size} bytes; expanded {sum(p.stat().st_size for p in stage.rglob("*") if p.is_file())} bytes')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', required=True, choices=['linux-x64', 'win-x64'])
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    import re
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', args.version):
        parser.error('Use a semantic version, without the v prefix')
    package(args.rid, args.version, args.output)
