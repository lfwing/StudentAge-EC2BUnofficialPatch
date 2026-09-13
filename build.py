#!/usr/bin/env python3
"""Reproducible local-reference build. Never installs to the game or downloads game assemblies."""
import argparse, hashlib, json, shutil, subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parent
p=argparse.ArgumentParser()
p.add_argument('--game',type=Path,required=True)
p.add_argument('--bepinex',type=Path,required=True,help='BepInEx/core directory')
p.add_argument('--layout',choices=['merged','split','all'],default='all')
a=p.parse_args()
managed=a.game/'StudentAge_Data/Managed'
for f in [managed/'Assembly-CSharp.dll',a.bepinex/'BepInEx.dll',a.bepinex/'0Harmony.dll']:
 if not f.is_file(): p.error('Missing reference: '+str(f))
dotnet=shutil.which('dotnet')
if not dotnet: p.error('.NET SDK required')
sdk=subprocess.check_output([dotnet,'--list-sdks'],text=True).strip().splitlines()[-1]
csc=Path(sdk.split('[')[1].rstrip(']'))/sdk.split()[0]/'Roslyn/bincore/csc.dll'
refs=sorted(managed.glob('*.dll'))+[a.bepinex/n for n in ['BepInEx.dll','BepInEx.Harmony.dll','0Harmony.dll']]
qa=ROOT/'qa';qa.mkdir(exist_ok=True)
up=ROOT/'EC2BUnofficialPatch';ba=ROOT/'LFBetterAudio'
def sources(folder):
 return sorted(f for f in folder.rglob('*.cs') if not {'bin','obj','UpdaterHelper','Examples'} & set(f.relative_to(folder).parts))
upsrc=sources(up); basrc=sources(ba)
def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest()
def compile(name,output,src,resources=(),version='1.0.25.0',assemblyversion='1.0.9.0'):
 output.mkdir(parents=True,exist_ok=True)
 attrs=output/(name+'.AssemblyInfo.cs')
 attrs.write_text('using System.Reflection;\n'+f'[assembly:AssemblyVersion("{assemblyversion}")]\n[assembly:AssemblyFileVersion("{version}")]\n[assembly:AssemblyInformationalVersion("1.0.25-handoff.4")]\n')
 cmd=[dotnet,str(csc),'-nologo','-target:library','-nostdlib+','-langversion:latest','-optimize+','-deterministic+','-define:STUDENTAGE_HANDOFF','-out:'+str(output/(name+'.dll')),'-pathmap:'+str(ROOT)+'=/_/']
 cmd+=['-r:'+str(f) for f in refs]
 cmd+=['-resource:'+str(f)+','+logical for f,logical in resources]
 cmd += [str(f) for f in src]+[str(attrs)]
 run=subprocess.run(cmd,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 (qa/(output.name+'-'+name+'-build.log')).write_text(run.stdout)
 print(run.stdout,end='')
 run.check_returncode()
 return output/(name+'.dll')
helper=ROOT/'dist/updater/EC2BUnofficialPatch.Updater.exe'
helper.parent.mkdir(parents=True,exist_ok=True)
subprocess.run([dotnet,str(csc),'-nologo','-target:exe','-nostdlib+','-langversion:latest','-optimize+','-deterministic+','-out:'+str(helper)]+['-r:'+str(managed/n) for n in ['mscorlib.dll','System.dll','System.Core.dll']]+[str(up/'UpdaterHelper/Program.cs')],check=True)
resources=[(helper,'EC2BUnofficialPatch.Embedded.Updater.exe'),(up/'ModAuthorTemplate/ExternalLive2D/readme.txt','EC2BUnofficialPatch.Embedded.ExternalLive2D.Readme.txt')]
# Embed the original single-DLL updater; manifests must match the installation layout.
manifest={'version':'1.0.25','channel':'handoff.4','gameAssemblySHA256':sha(managed/'Assembly-CSharp.dll'),'bepinexSHA256':sha(a.bepinex/'BepInEx.dll'),'artifacts':{},'validation':'See qa and docs/VALIDATION.md; compilation is not full gameplay acceptance.'}
for layout in (['merged','split'] if a.layout=='all' else [a.layout]):
 out=ROOT/'dist'/layout
 if layout=='merged':files=[compile('EC2BUnofficialPatch',out,upsrc+basrc,resources)]
 else:files=[compile('LFBetterAudio',out,basrc,version='1.0.0.0',assemblyversion='1.0.0.0'),compile('EC2BUnofficialPatch',out,upsrc,resources)]
 manifest['artifacts'][layout]=[{ 'name':f.name,'size':f.stat().st_size,'sha256':sha(f)} for f in files]
(ROOT/'dist/build-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
if a.layout == 'all':
 subprocess.run([__import__('sys').executable,str(ROOT/'tools/prepare_release.py')],check=True)
print('Build complete:',ROOT/'dist')
