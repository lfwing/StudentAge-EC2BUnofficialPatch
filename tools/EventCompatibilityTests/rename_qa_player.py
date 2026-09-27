#!/usr/bin/env python3
"""Change only the cloned player's native preferences identity; never the real player."""
from pathlib import Path
import hashlib
import json
import struct
import UnityPy

ROOT = Path(__file__).resolve().parents[2]
path = ROOT / 'qa/runtime/StudentAge_Data/data.unity3d'
env = UnityPy.load(str(path))
settings = [o for o in env.objects if o.type.name == 'PlayerSettings']
assert len(settings) == 1
obj = settings[0]
raw = obj.get_raw_data()
for old, new in [(b'PakyiGame', b'EventcQA0'), (b'StudentAge', b'EventcQA00')]:
    assert len(old) == len(new)
    if new in raw:
        continue
    marker = struct.pack('<i', len(old)) + old
    assert raw.count(marker) == 1
    raw = raw.replace(marker, struct.pack('<i', len(new)) + new)
obj.set_raw_data(raw)
path.write_bytes(env.file.save(packer='lz4'))
(path.parent / 'app.info').write_text('EventcQA0\nEventcQA00')
actual = next(o for o in UnityPy.load(str(path)).objects if o.type.name == 'PlayerSettings').get_raw_data()
assert b'PakyiGame' not in actual and b'StudentAge' not in actual
assert b'EventcQA0' in actual and b'EventcQA00' in actual
(ROOT / 'qa/runtime/native-identity-verified.json').write_text(json.dumps({'company': 'EventcQA0', 'product': 'EventcQA00', 'player_settings_sha256': hashlib.sha256(actual).hexdigest()}, indent=2) + '\n')
print('QA_NATIVE_IDENTITY_ISOLATED')
