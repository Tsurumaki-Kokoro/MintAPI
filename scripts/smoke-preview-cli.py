#!/usr/bin/env python3
"""Offline PNG/GIF/MP4 smoke checks for all rulesets. Usage: python3 scripts/smoke-preview-cli.py CLI OUTPUT_DIR"""
import io
import json
from pathlib import Path
import struct
import subprocess
import sys
import wave
import zipfile

cli = Path(sys.argv[1]).resolve()
root = Path(sys.argv[2]).resolve()
root.mkdir(parents=True, exist_ok=True)
audio = io.BytesIO()
with wave.open(audio, "wb") as output:
    output.setnchannels(2)
    output.setsampwidth(2)
    output.setframerate(44100)
    # Audible sine wave allows checking the encoded audio without a network download.
    import math
    output.writeframes(b"".join(struct.pack("<hh", int(2000 * math.sin(2 * math.pi * 440 * i / 44100)), int(2000 * math.sin(2 * math.pi * 440 * i / 44100))) for i in range(44100 * 5)))
for mode in range(4):
    text = f"""osu file format v14

[General]
AudioFilename: tone.wav
PreviewTime: 1000
Mode: {mode}

[Metadata]
Title:HitCircle preview smoke
Artist:Test
Creator:Codex
Version:Mode {mode}
BeatmapID:{900000 + mode}
BeatmapSetID:90000

[Difficulty]
HPDrainRate:5
CircleSize:4
OverallDifficulty:5
ApproachRate:5
SliderMultiplier:1.4
SliderTickRate:1

[TimingPoints]
0,500,4,1,0,100,1,0

[HitObjects]
64,192,1000,1,0,0:0:0:0:
192,192,1500,1,0,0:0:0:0:
320,192,2000,1,0,0:0:0:0:
448,192,2500,1,0,0:0:0:0:
"""
    osu = root / f"mode {mode}.osu"
    osu.write_text(text)
    osz = root / f"mode {mode}.osz"
    with zipfile.ZipFile(osz, "w") as archive:
        archive.writestr("map.osu", text)
        archive.writestr("tone.wav", audio.getvalue())
    for fmt in ("png", "gif", "mp4"):
        args = [str(cli), f"--input-file={osz if fmt == 'mp4' else osu}", f"--bid={900000 + mode}", f"--fmt={fmt}", f"--output-dir={root / 'outputs'}", "--no-log", "--scale=0.5"]
        if fmt != "png":
            args.extend(["--time-points=preview", "--duration-time=2", "--fps=10"])
        result = subprocess.run(args, check=True, capture_output=True, text=True, timeout=120)
        path = Path(json.loads(result.stdout)["preview-img"])
        data = path.read_bytes()
        assert len(data) > 100
        assert data.startswith(b"\x89PNG") if fmt == "png" else data.startswith(b"GIF") if fmt == "gif" else data[4:8] == b"ftyp"
        print(f"mode={mode} {fmt}: {len(data)} bytes {path}")
