#!/usr/bin/env python3
"""Package the single UP plugin with its public installation guide."""
import argparse, hashlib, json, zipfile
from pathlib import Path

r = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
build = json.loads((r/'dist/build-manifest.json').read_text())
assert set(build['artifacts']) == {'merged'}, 'Only integrated UP is supported'
dll = r/'dist/merged/EC2BUnofficialPatch.dll'
recorded = build['artifacts']['merged']
assert recorded == [{'name':dll.name,'size':dll.stat().st_size,'sha256':hashlib.sha256(dll.read_bytes()).hexdigest()}], 'DLL differs from build record'
files = {dll:'BepInEx/plugins/EC2BUnofficialPatch.dll', r/'README.md':'README.md', r/'LICENSE':'LICENSE'}
for directory in ['Docs', 'EC2BUnofficialPatch/Docs', 'EC2BUnofficialPatch/ModAuthorTemplate',
                  'EC2BUnofficialPatch/Examples', 'EC2BUnofficialPatch/Features/Audio/ModAuthorTemplate']:
    for f in (r/directory).rglob('*'):
        if f.is_file() and f.name != '.DS_Store':
            files[f] = f.relative_to(r).as_posix()
a.output.mkdir(parents=True, exist_ok=True)
out = a.output / ('StudentAge-UP-'+build['version']+'.zip')
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for f, name in sorted(files.items(), key=lambda item:item[1]):
        z.writestr(name, f.read_bytes())
with zipfile.ZipFile(out) as z:
    assert z.testzip() is None
    assert [n for n in z.namelist() if n.endswith('.dll')] == ['BepInEx/plugins/EC2BUnofficialPatch.dll']
delivery = {'file':out.name,'version':build['version'],'size':out.stat().st_size,'sha256':hashlib.sha256(out.read_bytes()).hexdigest()}
(r/'dist/delivery.json').write_text(json.dumps(delivery, ensure_ascii=False, indent=2)+'\n')
print(json.dumps(delivery, ensure_ascii=False))
