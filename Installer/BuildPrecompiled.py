"""Build a local precompiled installer from an explicitly supplied, tested runtime.

This does not publish files or determine redistribution rights.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--runtime', type=Path, required=True)
parser.add_argument('--launcher', type=Path, help='Optional newly built launcher')
parser.add_argument('--worldtour', type=Path, help='Optional verified World Tour module candidate')
parser.add_argument('--inputs', type=Path, required=True, help='Folder containing the three original modules used for recompilation')
parser.add_argument('--extractor', type=Path, required=True)
parser.add_argument('--licenses-bundle', type=Path, required=True)
parser.add_argument('--crt', type=Path, required=True, help='Official Visual Studio VC/Redist x64 CRT folder')
parser.add_argument('--crt-notices', type=Path, required=True, help='Visual Studio Redist.txt')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parent
stage = args.output.resolve()
if stage.exists():
    raise SystemExit('Choose a new output directory; existing packages are not overwritten.')
stage.mkdir(parents=True)
bundle = stage/'package'
game = bundle/'payload/Game'
game.mkdir(parents=True)
(bundle/'tools').mkdir()
shutil.copy2(args.extractor, bundle/'tools/extract-xiso.exe')
shutil.copy2(args.launcher or args.runtime/'FifaStreetLauncher.exe', bundle/'payload/FifaStreetLauncher.exe')
for name in ['fifastreet.exe', 'fifastreet_fifadllzf_xex.dll', 'fifastreet_FootballCompEngzf_xex.dll', 'rexruntime.dll', 'rexgpu-xenos.dll', 'TracyClient.dll']:
    source = args.worldtour if name == 'fifastreet_FootballCompEngzf_xex.dll' and args.worldtour else args.runtime/name
    shutil.copy2(source, game/name)
for backend in ['D3D12', 'Vulkan']:
    destination = game/'Backends'/backend
    destination.mkdir(parents=True)
    for name in ['rexruntime.dll', 'rexgpu-xenos.dll', 'TracyClient.dll']:
        shutil.copy2(args.runtime/'Backends'/backend/name, destination/name)
shutil.copy2(root/'payload/Game/fifastreet.toml', game/'fifastreet.toml')
required_crt = ['msvcp140.dll', 'msvcp140_atomic_wait.dll', 'vcruntime140.dll', 'vcruntime140_1.dll']
if not all((args.crt/name).is_file() for name in required_crt):
    raise ValueError('The official x64 CRT folder is incomplete.')
for dll in args.crt.glob('*.dll'):
    shutil.copy2(dll, game/dll.name)
# extract-xiso also imports VCRUNTIME140; it must run on a clean Windows PC.
shutil.copy2(args.crt/'vcruntime140.dll', bundle/'tools/vcruntime140.dll')
# The default selected backend must match the DLLs beside the executable.
for name in ['rexruntime.dll', 'rexgpu-xenos.dll', 'TracyClient.dll']:
    shutil.copy2(game/'Backends/D3D12'/name, game/name)
with zipfile.ZipFile(args.licenses_bundle) as original:
    for entry in original.infolist():
        path = Path(entry.filename)
        if path.parts and path.parts[0] == 'licenses' and not entry.is_dir():
            if path.is_absolute() or '..' in path.parts:
                raise ValueError('Invalid license archive path')
            target = bundle/'payload'/path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(original.read(entry))
if not (bundle/'payload/licenses').is_dir():
    raise ValueError('Third-party license notices are required.')
shutil.copy2(root/'FifaStreetSetupTool/ThirdParty/MonoGame-LICENSE.txt', bundle/'payload/licenses/MonoGame-LZX-MS-PL.txt')
shutil.copy2(args.crt_notices, bundle/'payload/licenses/Microsoft-CRT-Redist.txt')
shutil.copytree(root.parent/'licenses/bundled-tools', bundle/'payload/licenses/bundled-tools')
shutil.copy2(root.parent/'licenses/README.md', bundle/'payload/licenses/LICENSE-SCOPE.md')
for document in ['CREDITS.md', 'CHANGELOG.md', 'LICENSE']:
    shutil.copy2(root.parent/document, bundle/'payload'/document)

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

inputs = {'default.xex': 'default.xex', 'fifadllzf.xex.dll': 'fifadllzf.xex.dll', 'dlc/dlc_FootballCompEng/dlc/FootballCompEng/FootballCompEngzf.xex.dll': 'FootballCompEngzf.xex.dll'}
manifest = {'Format': 1, 'Version': 'Known FIFA Street 2012 build (verified module hashes)', 'Inputs': {key: digest(args.inputs/value) for key, value in inputs.items()}, 'Payload': {p.relative_to(bundle/'payload').as_posix(): digest(p) for p in sorted((bundle/'payload').rglob('*')) if p.is_file()}}
manifest_path = bundle/'precompiled.json'
manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
archive = stage/'precompiled-bundle.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as packed:
    for path in sorted(bundle.rglob('*')):
        if path.is_file():
            packed.write(path, path.relative_to(bundle).as_posix())
subprocess.run(['rtk', 'proxy', 'dotnet', 'publish', str(root/'FifaStreetSetupTool/FifaStreetSetupTool.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:RuntimeFrameworkVersion=8.0.31', f'-p:BundlePath={archive}', f'-p:PrecompiledManifest={manifest_path}', '-o', str(stage/'published')], check=True)
exe = stage/'FifaStreetSetup-Precompiled-LOCAL.exe'
shutil.copy2(stage/'published/FifaStreetSetup.exe', exe)
(stage/'build.json').write_text(json.dumps({'installer': str(exe), 'sha256': digest(exe), 'bytes': exe.stat().st_size, 'publication': 'Local test only; redistribution not reviewed', 'manifest': manifest}, indent=2), encoding='utf-8')
print(f'Local precompiled installer built: {exe} ({exe.stat().st_size / 1024**2:.1f} MiB)')
