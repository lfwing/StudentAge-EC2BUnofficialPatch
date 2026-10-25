"""Exercise actual built UP readers in three layouts, without installing or starting the game."""
import argparse
from pathlib import Path
import shutil
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--game', type=Path, required=True)
parser.add_argument('--bepinex', type=Path, required=True)
parser.add_argument('--dll', type=Path, default=root / 'bin/Release/net472/EC2BUnofficialPatch.dll')
parser.add_argument('--external-mod', type=Path, help='Read-only validation of an installed external minigame Mod')
args = parser.parse_args()
dotnet = shutil.which('dotnet')
sdk = subprocess.check_output([dotnet, '--list-sdks'], text=True).strip().splitlines()[-1]
csc = Path(sdk.split('[')[1].rstrip(']')) / sdk.split()[0] / 'Roslyn/bincore/csc.dll'
qa = root / 'qa'
qa.mkdir(exist_ok=True)
exe = qa / 'ContentLayoutCheck.exe'
managed = args.game / 'StudentAge_Data/Managed'
subprocess.run([dotnet, str(csc), '-nologo', '-target:exe', '-nostdlib+', '-langversion:latest',
                '-out:' + str(exe), *['-r:' + str(managed / name) for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll']],
                str(root / 'tools/ContentLayoutCheck.cs')], check=True)
command = [str(exe), str(args.dll), str(args.game), str(args.bepinex), str(qa)]
if args.external_mod:
    command.append(str(args.external_mod))
subprocess.run(command, check=True)
