#!/usr/bin/env python3
"""Build the plugin with the installed game's Mono/Unity references; never install it."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_GAME = Path(os.environ.get('STUDENTAGE_GAME_DIR', str(Path.home() / 'Library/Application Support/CrossOver/Bottles/Steam/drive_c/Program Files (x86)/Steam/steamapps/common/StudentAge')))
OUTPUT = ROOT / 'dist/merged/EC2BUnofficialPatch.dll'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def compiler():
    dotnet = shutil.which('dotnet') or '/usr/local/share/dotnet/dotnet'
    sdks = subprocess.check_output([dotnet, '--list-sdks'], text=True).strip().splitlines()
    if not sdks:
        raise SystemExit('A .NET SDK is required to run the Roslyn compiler.')
    sdk = sdks[-1]
    csc = Path(sdk.split('[', 1)[1].rstrip(']')) / sdk.split()[0] / 'Roslyn/bincore/csc.dll'
    return dotnet, csc


def references(game):
    managed = game / 'StudentAge_Data/Managed'
    refs = sorted(managed.glob('*.dll')) + [game / 'BepInEx/core' / name for name in ('BepInEx.dll', '0Harmony.dll')]
    required = [managed / 'Assembly-CSharp.dll', managed / 'mscorlib.dll'] + refs
    missing = [str(p) for p in required if not p.is_file()]
    if missing:
        raise SystemExit('Missing game references: ' + ', '.join(missing))
    return refs


def compile_library(sources, output, refs, resources=(), log=None):
    dotnet, csc = compiler()
    output.parent.mkdir(parents=True, exist_ok=True)
    command = [dotnet, str(csc), '-nologo', '-target:library', '-nostdlib+', '-langversion:latest', '-optimize+', '-deterministic+', '-pathmap:' + str(ROOT) + '=/_/', '-out:' + str(output)]
    command += ['-r:' + str(p) for p in refs]
    command += ['-resource:' + str(p) + ',' + name for p, name in resources]
    command += [str(p) for p in sources]
    result = subprocess.run(command, capture_output=True, text=True)
    combined = result.stdout + result.stderr
    if log:
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text(combined)
    if combined:
        print(combined, end='')
    if result.returncode:
        raise SystemExit(result.returncode)
