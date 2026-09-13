#!/usr/bin/env python3
"""Prepare the single UP binary and its existing merged update feed. Never publish."""
import argparse, hashlib, json, shutil
from pathlib import Path

r = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--repository', default='lfwing/StudentAge-EC2BUnofficialPatch')
p.add_argument('--version', help='Defaults to the version recorded by build.py')
a = p.parse_args()
if len(a.repository.split('/')) != 2 or any(not part or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-' for c in part) for part in a.repository.split('/')):
    p.error('Expected owner/repository')
build = json.loads((r/'dist/build-manifest.json').read_text())
version = a.version or build['version']
if not version or any(not part.isdigit() for part in version.split('.')):
    p.error('Version must be numeric dotted version')
if version != build['version']:
    raise SystemExit('Release version differs from build manifest')
if set(build['artifacts']) != {'merged'}:
    raise SystemExit('Only the integrated UP build is supported')
files = list((r/'dist/merged').glob('*.dll'))
if {f.name for f in files} != {'EC2BUnofficialPatch.dll'}:
    raise SystemExit('Expected exactly one integrated UP DLL')
dll = files[0]
digest = hashlib.sha256(dll.read_bytes()).hexdigest()
recorded = build['artifacts']['merged']
if len(recorded) != 1 or recorded[0] != {'name':dll.name,'size':dll.stat().st_size,'sha256':digest}:
    raise SystemExit('Artifact changed since build')
out = r/'dist/release'
out.mkdir(parents=True, exist_ok=True)
asset = 'merged-' + dll.name  # Preserve the existing merged-channel download convention.
shutil.copy2(dll, out/asset)
manifest = {'schema':1,'layout':'merged','version':version,'channel':'stable','gameVersion':'1.93',
            'assetName':dll.name,'size':dll.stat().st_size,'sha256':digest,
            'releasePage':f'https://github.com/{a.repository}/releases/tag/{version}',
            'downloadUrls':[f'https://github.com/{a.repository}/releases/download/{version}/{asset}']}
candidates = r/'release-manifests'
candidates.mkdir(exist_ok=True)
for target in [out/'update-merged.json', candidates/'update-merged.json']:
    target.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
print('Prepared UP DLL and update-merged.json locally. Live feeds are unchanged until assets are published.')
