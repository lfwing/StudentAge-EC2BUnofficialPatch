#!/usr/bin/env python3
"""Record or verify the live game's code, saves and native preference namespace, read-only."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re

ROOT = Path(__file__).resolve().parents[2]
BOTTLE = Path(os.environ.get('STUDENTAGE_BOTTLE_DIR', str(Path.home() / 'Library/Application Support/CrossOver/Bottles/Steam')))
GAME = BOTTLE / 'drive_c/Program Files (x86)/Steam/steamapps/common/StudentAge'
SAVES = BOTTLE / 'drive_c/users/crossover/AppData/LocalLow/PakyiGame/StudentAge'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'verify'))
    parser.add_argument('--output', type=Path, default=ROOT / 'qa/live-baseline.json')
    args = parser.parse_args()
    state = {}
    for folder in (GAME / 'BepInEx/plugins', GAME / 'StudentAge_Data/Managed', SAVES):
        for p in sorted(folder.rglob('*')):
            if p.is_file() and p.suffix.lower() not in ('.log', '.bak'):
                state[str(p.relative_to(BOTTLE))] = [p.stat().st_size, hashlib.sha256(p.read_bytes()).hexdigest()]
    for name in ('data.unity3d', 'app.info'):
        p = GAME / 'StudentAge_Data' / name
        state[str(p.relative_to(BOTTLE))] = [p.stat().st_size, hashlib.sha256(p.read_bytes()).hexdigest()]
    registry = (BOTTLE / 'user.reg').read_text(errors='replace')
    blocks = re.split(r'(?m)(?=^\[)', registry)
    state['player-registry'] = [b for b in blocks if b.startswith('[Software\\\\PakyiGame\\\\StudentAge]')]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.mode == 'verify':
        old = json.loads(args.output.read_text())
        changed = sorted(p for p in set(state) | set(old) if state.get(p) != old.get(p))
        args.output.with_name(args.output.stem + '-verification.json').write_text(json.dumps({'changed': changed, 'checked': len(state)}, ensure_ascii=False, indent=2) + '\n')
        print('LIVE_BASELINE_UNCHANGED' if not changed else 'LIVE_BASELINE_DIFFERENT', len(state), changed)
        if changed:
            raise SystemExit(1)
    else:
        args.output.write_text(json.dumps(state, ensure_ascii=False, indent=2) + '\n')
        print('LIVE_BASELINE_RECORDED', len(state))


if __name__ == '__main__':
    main()
