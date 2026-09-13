#!/usr/bin/env python3
"""Prepare the upstream schema-1 release flow, with one manifest per layout. Never upload."""
import argparse, hashlib, json, shutil
from pathlib import Path
r=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser()
p.add_argument('--repository',default='lfwing/StudentAge-EC2BUnofficialPatch')
p.add_argument('--version',default='1.0.25')
a=p.parse_args()
if len(a.repository.split('/'))!=2 or any(not part or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-' for c in part) for part in a.repository.split('/')):p.error('Expected owner/repository')
if any(c not in '0123456789.' for c in a.version):p.error('Version must be numeric dotted version')
build=json.loads((r/'dist/build-manifest.json').read_text())
if a.version!=build['version']:raise SystemExit('Release version differs from build manifest')
if set(build['artifacts'])!={'merged','split'}:raise SystemExit('Build both layouts before preparing a release')
out=r/'dist/release';out.mkdir(exist_ok=True)
for layout in ['merged','split']:
 files=list((r/'dist'/layout).glob('*.dll'))
 expected={'EC2BUnofficialPatch.dll'}|({'LFBetterAudio.dll'} if layout=='split' else set())
 if {f.name for f in files}!=expected:raise SystemExit('Incomplete layout '+layout)
 for f in files:
  recorded=next((x for x in build['artifacts'][layout] if x['name']==f.name),None)
  if not recorded or recorded['size']!=f.stat().st_size or recorded['sha256']!=hashlib.sha256(f.read_bytes()).hexdigest():raise SystemExit('Artifact changed since build: '+str(f))
  shutil.copy2(f,out/(layout+'-'+f.name))
 dll=r/'dist'/layout/'EC2BUnofficialPatch.dll'
 manifest={'schema':1,'layout':layout,'version':a.version,'channel':'stable','gameVersion':'1.93',
  'assetName':dll.name,'size':dll.stat().st_size,'sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),
  'releasePage':f'https://github.com/{a.repository}/releases/tag/{a.version}',
  'downloadUrls':[f'https://github.com/{a.repository}/releases/download/{a.version}/{layout}-{dll.name}']}
 name='update-merged.json' if layout=='merged' else 'update.json'
 for target in [out/name,r/name,r/'EC2BUnofficialPatch'/name]:
  target.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print('Prepared schema 1 manifests and matching DLLs locally. URLs are publication targets, not uploaded releases.')
