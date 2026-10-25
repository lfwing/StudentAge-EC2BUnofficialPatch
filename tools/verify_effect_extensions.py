"""Run real game/UP effect methods without installing the DLL or starting Unity."""
import argparse
from pathlib import Path
import shutil
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--game', type=Path, required=True)
parser.add_argument('--bepinex', type=Path, required=True)
parser.add_argument('--cfg', type=Path, required=True)
args = parser.parse_args()
dotnet = shutil.which('dotnet')
sdk = subprocess.check_output([dotnet, '--list-sdks'], text=True).strip().splitlines()[-1]
csc = Path(sdk.split('[')[1].rstrip(']')) / sdk.split()[0] / 'Roslyn/bincore/csc.dll'
out = root / 'qa/effect-extensions-2026-10-25'
out.mkdir(exist_ok=True)
exe = out / 'EffectExtensionCheck.exe'
managed = args.game / 'StudentAge_Data/Managed'
prepare = out / 'PrepareEffectTestRuntime.exe'
subprocess.run([dotnet, str(csc), '-nologo', '-target:exe', '-nostdlib+', '-langversion:latest',
    '-out:' + str(prepare), *['-r:' + str(managed / n) for n in ['mscorlib.dll', 'System.dll', 'System.Core.dll']],
    '-r:' + str(args.bepinex / 'Mono.Cecil.dll'), str(root / 'tools/PrepareEffectTestRuntime.cs')], check=True)
subprocess.run([str(prepare), str(managed / 'UnityEngine.CoreModule.dll'), str(out / 'clr-runtime/UnityEngine.CoreModule.dll'), str(args.bepinex)], check=True)
refs = sorted(managed.glob('*.dll')) + [args.bepinex / name for name in ['BepInEx.dll', '0Harmony.dll']]
command = ['-nologo', '-target:exe', '-nostdlib+', '-langversion:latest', '-out:' + str(exe)]
command += ['-r:' + str(path) for path in refs] + [str(root / 'tools/EffectExtensionCheck.cs'), str(root / 'tools/TraitMechanicCheck.cs'), str(root / 'tools/VisibilityConsoleCheck.cs')]
rsp = out / 'compiler.rsp'
rsp.write_text('\n'.join(subprocess.list2cmdline([arg]) for arg in command) + '\n', encoding='utf-8')
subprocess.run([dotnet, str(csc), '@' + str(rsp)], check=True)
subprocess.run([str(exe), str(root / 'dist/merged/EC2BUnofficialPatch.dll'), str(args.game), str(args.bepinex), str(args.cfg), str(out)], check=True)
shutil.copyfile(out / 'sa.EC2B.UnofficialPatch.cfg', root / 'dist/release/sa.EC2B.UnofficialPatch.cfg')
