#!/usr/bin/env python3
"""Prepare an isolated CrossOver player. Does not launch or change the live installation."""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from qa_build import DEFAULT_GAME, OUTPUT, compile_library, compiler, references, sha


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, default=DEFAULT_GAME)
    parser.add_argument('--fixture', type=Path, help='Optional synthetic fixture located in a project qa/ directory; never a player save.')
    parser.add_argument('--python', default=os.environ.get('STUDENTAGE_QA_PYTHON', sys.executable), help='Python environment containing UnityPy.')
    parser.add_argument('--without-driver', action='store_true')
    args = parser.parse_args()
    processes = subprocess.check_output(['ps', '-axo', 'pid,comm,args'], text=True)
    if any('StudentAge.exe' in line and 'winewrapper' not in line and 'exec_command' not in line and ' /bin/zsh -c ' not in line for line in processes.splitlines()):
        parser.error('A StudentAge player is running; stop it before preparing the isolated clone.')
    qa = ROOT / 'qa/runtime'
    qa.mkdir(parents=True, exist_ok=True)
    if args.fixture:
        fixture = args.fixture.resolve()
        if not fixture.is_file() or 'qa' not in fixture.parts or 'LocalLow' in fixture.parts or 'userdata' in fixture.parts:
            parser.error('--fixture must name synthetic test data under a project qa/ folder.')
        if fixture != (qa / 'fixture.save').resolve():
            shutil.copy2(fixture, qa / 'fixture.save')
        (qa / 'fixture-provenance.json').write_text(json.dumps({'source': str(fixture), 'sha256': sha(fixture), 'synthetic': True}, indent=2) + '\n')
    if not OUTPUT.is_file():
        parser.error('Build the production plugin first: python3 build.py')
    subprocess.run([args.python, '-c', 'import UnityPy'], check=True)
    original = {str(p.relative_to(args.game)): sha(p) for p in (args.game / 'StudentAge_Data/Managed').glob('*.dll')}
    original['StudentAge_Data/data.unity3d'] = sha(args.game / 'StudentAge_Data/data.unity3d')
    for name in ('isolation-verified.txt', 'native-identity-verified.json', 'prepared.json'):
        (qa / name).unlink(missing_ok=True)
    for name in ('StudentAge.exe', 'UnityPlayer.dll', 'MonoBleedingEdge', 'StudentAge_Data', 'DLC', 'winhttp.dll', 'doorstop_config.ini'):
        source, target = args.game / name, qa / name
        if source.exists() and not target.exists():
            subprocess.run(['/bin/cp', '-cR', str(source), str(target)], check=True)
    (qa / 'BepInEx').mkdir(exist_ok=True)
    if not (qa / 'BepInEx/core').exists():
        subprocess.run(['/bin/cp', '-cR', str(args.game / 'BepInEx/core'), str(qa / 'BepInEx/core')], check=True)
    plugins = qa / 'BepInEx/plugins'
    plugins.mkdir(exist_ok=True)
    (qa / 'data').mkdir(exist_ok=True)
    for p in (args.game / 'StudentAge_Data/Managed').glob('*.dll'):
        shutil.copy2(p, qa / 'StudentAge_Data/Managed' / p.name)
    # Always derive native player identity from the untouched original bundle.
    shutil.copy2(args.game / 'StudentAge_Data/data.unity3d', qa / 'StudentAge_Data/data.unity3d')
    shutil.copy2(OUTPUT, plugins / OUTPUT.name)
    config = qa / 'BepInEx/config/sa.EC2B.UnofficialPatch.cfg'
    config.parent.mkdir(parents=True, exist_ok=True)
    if not config.exists():
        config.write_text('[机制]\n多mod事件ID兼容 = true\n[更新]\n启动时自动检查更新 = false\n发现新版本后自动下载并安装 = false\n')
    (qa / '.event-compat-qa').write_text('Isolated synthetic-fixture player; never ship.\n')
    refs = references(args.game)
    compile_library([ROOT / 'tools/EventCompatibilityTests/QaIsolation.cs'], qa / 'StudentAge_Data/Managed/QaIsolation.dll', refs)
    driver = sorted((ROOT / 'tools/EventCompatibilityTests').glob('Runtime*QA.cs'))
    if driver and not args.without_driver:
        compile_library(driver, plugins / 'StudentAge.EventCompat.RuntimeQA.dll', refs + [OUTPUT], log=ROOT / 'qa/runtime-qa-build.log')
    else:
        (plugins / 'StudentAge.EventCompat.RuntimeQA.dll').unlink(missing_ok=True)
    dotnet, _ = compiler()
    subprocess.run([dotnet, 'run', '--project', str(ROOT / 'tools/EventCompatibilityTests/QaRewrite/QaRewrite.csproj'), '-p:GameDir=' + str(args.game), '--', str(qa)], check=True)
    subprocess.run([args.python, str(ROOT / 'tools/EventCompatibilityTests/rename_qa_player.py')], check=True)
    after = {name: sha(args.game / name) for name in original}
    if after != original:
        raise SystemExit('Live game changed while preparing the isolated copy.')
    (qa / 'prepared.json').write_text(json.dumps({'live_game': str(args.game), 'live_game_hashes': original, 'plugin_sha256': sha(OUTPUT), 'runtime_plugin_sha256': sha(plugins / OUTPUT.name), 'driver': bool(driver) and not args.without_driver, 'fixture': (qa / 'fixture.save').is_file()}, indent=2) + '\n')
    print('QA_COPY_READY', qa)


if __name__ == '__main__':
    main()
