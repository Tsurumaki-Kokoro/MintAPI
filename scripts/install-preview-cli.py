#!/usr/bin/env python3
"""Build the pinned preview CLI for the current platform (Python, Git, Rust and C/C++ required)."""
import hashlib
import json
import platform
import sys
from pathlib import Path
import shutil
import subprocess
import tempfile

REVISION = "e3883affa62ae00224a2e16e28755ea712b943bc"
ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "HitCircleAPI/tools/osu-preview"
architecture = {"x86_64": "x64", "amd64": "x64", "arm64": "arm64", "aarch64": "arm64"}.get(platform.machine().lower())
os_name = {"darwin": "osx", "linux": "linux", "win32": "win"}.get(sys.platform)
rid = f"{os_name}-{architecture}"
if rid not in {"win-x64", "linux-x64", "osx-x64", "osx-arm64"}:
    raise SystemExit(f"Unsupported preview publish platform: {rid}")
with tempfile.TemporaryDirectory(prefix="hitcircle-preview-build-") as temp:
    source = Path(temp) / "source"
    subprocess.run(["git", "clone", "--no-checkout", "https://github.com/2710165659/osu-beatmap-preview.git", str(source)], check=True)
    subprocess.run(["git", "checkout", "--detach", REVISION], cwd=source, check=True)
    subprocess.run(["cargo", "build", "--release", "--locked", "--package", "osu-beatmap-preview-cli"], cwd=source, check=True)
    executable = "osu-beatmap-preview-cli.exe" if __import__("os").name == "nt" else "osu-beatmap-preview-cli"
    DEST.mkdir(parents=True, exist_ok=True)
    target = DEST / executable
    shutil.copy2(source / "target/release" / executable, target)
    target.chmod(0o755)
    (DEST / "runtime-id.txt").write_text(rid + "\n")
    shutil.copy2(source / "LICENSE", DEST / "LICENSE")
    shutil.copy2(source / "docs/THIRD_PARTY_NOTICES.md", DEST / "THIRD_PARTY_NOTICES.md")
    (DEST / "build-info.json").write_text(json.dumps({"revision": REVISION, "runtimeIdentifier": rid, "sha256": hashlib.sha256(target.read_bytes()).hexdigest()}, indent=2) + "\n")
    subprocess.run([str(target), "--version"], check=True)
