#!/usr/bin/env python3
"""Run only the prepared isolated player, without stopping any existing game process."""
from pathlib import Path
import argparse
import datetime
import json
import os
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / 'qa/runtime'
sys.path.insert(0, str(ROOT))
from qa_build import OUTPUT, sha


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--timeout', type=int, default=300)
    parser.add_argument('--width', type=int, default=1920)
    parser.add_argument('--height', type=int, default=1080)
    args = parser.parse_args()
    assert (QA / 'isolation-verified.txt').is_file(), 'Run tools/prepare_qa.py first.'
    native = json.loads((QA / 'native-identity-verified.json').read_text())
    assert native['company'] == 'EventcQA0' and native['product'] == 'EventcQA00'
    assert (QA / 'StudentAge_Data/app.info').read_text() == 'EventcQA0\nEventcQA00'
    prepared = json.loads((QA / 'prepared.json').read_text())
    assert prepared['plugin_sha256'] == sha(OUTPUT), 'Re-prepare after rebuilding.'
    assert prepared['runtime_plugin_sha256'] == sha(QA / 'BepInEx/plugins' / OUTPUT.name), 'Prepared DLL was modified.'
    assert prepared['driver'] and prepared['fixture'], 'Automated QA requires its driver and synthetic fixture.'
    processes = subprocess.check_output(['ps', '-axo', 'pid,comm,args'], text=True)
    games = [line for line in processes.splitlines() if 'StudentAge.exe' in line and 'winewrapper' not in line and 'exec_command' not in line and ' /bin/zsh -c ' not in line]
    if games:
        raise SystemExit('Another StudentAge player is running; leaving it untouched.\n' + '\n'.join(games))
    subprocess.run([sys.executable, str(ROOT / 'tools/EventCompatibilityTests/qa_baseline.py'), 'record'], check=True)
    if (QA / 'results').exists():
        (QA / 'results').rename(QA / ('results-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S')))
    wine = os.environ.get('STUDENTAGE_CROSSOVER_WINE')
    if not wine:
        candidates = [p / 'Contents/SharedSupport/CrossOver/bin/wine' for directory in (Path.home() / 'Applications', Path('/Applications')) for p in directory.glob('*CrossOver*.app')]
        wine = next((str(p) for p in candidates if p.is_file()), None)
    if not wine:
        raise SystemExit('Set STUDENTAGE_CROSSOVER_WINE to the CrossOver wine executable.')
    exe = 'Z:' + str(QA / 'StudentAge.exe').replace('/', '\\')
    log = 'Z:' + str(QA / 'player.log').replace('/', '\\')
    env = dict(os.environ, SteamAppId='1991040', SteamGameId='1991040')
    command = [wine, '--bottle', os.environ.get('STUDENTAGE_BOTTLE', 'Steam'), '--dll', 'winhttp=n,b', '--workdir', str(QA), exe, '-screen-fullscreen', '0', '-screen-width', str(args.width), '-screen-height', str(args.height), '-logFile', log]
    with (QA / 'wine.log').open('w') as output:
        child = subprocess.Popen(command, env=env, stdout=output, stderr=subprocess.STDOUT)
        (QA / 'run.json').write_text(json.dumps({'launcher_pid': child.pid, 'started': datetime.datetime.now().isoformat(), 'command': command}, ensure_ascii=False, indent=2) + '\n')
        print('QA_RUNNING', child.pid, QA, flush=True)
        for _ in range(40):
            time.sleep(.25)
            processes = subprocess.check_output(['ps', '-axo', 'pid,command'], text=True)
            matches = [line for line in processes.splitlines() if exe + ' -screen' in line and 'winewrapper' not in line]
            if matches:
                player_pid = int(matches[0].strip().split()[0])
                subprocess.run(['osascript', '-e', 'tell application "System Events" to set frontmost of (first process whose unix id is ' + str(player_pid) + ') to true'], capture_output=True)
                break
        try:
            child.wait(timeout=args.timeout)
        except subprocess.TimeoutExpired:
            print('QA_TIMEOUT: isolated process left running for inspection.', flush=True)
            raise SystemExit(2)
    print('QA_PLAYER_EXITED', child.returncode, flush=True)
    subprocess.run([sys.executable, str(ROOT / 'tools/EventCompatibilityTests/qa_baseline.py'), 'verify'], check=True)
    if (QA / 'results/failed.txt').exists():
        raise SystemExit((QA / 'results/failed.txt').read_text())
    if not (QA / 'results/success.txt').exists():
        raise SystemExit('Runtime QA success marker absent; inspect qa/runtime/player.log.')
    print((QA / 'results/success.txt').read_text())


if __name__ == '__main__':
    main()
