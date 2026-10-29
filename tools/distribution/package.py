"""Build a complete Windows runtime distribution without modifying engine or rules."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/distribution')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', args.version):
        parser.error('Use a semantic version without a v prefix')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    name = f'heroscript-runtime-v{args.version}-win-x64'
    folder = output / name
    # Never overwrite an existing build or recursively delete a supplied path.
    folder.mkdir()
    runtime = folder / 'heroscript'
    subprocess.run(['dotnet', 'publish', str(ROOT / 'src/API/API.csproj'), '-c', 'Release',
                    '-r', 'win-x64', '--self-contained', 'true', '-o', str(runtime),
                    '--artifacts-path', str(output / 'build'), '-p:PublishTrimmed=false',
                    '-p:DefaultItemExcludesInProjectFolder=**/data/**'], cwd=ROOT, check=True)
    shutil.move(str(runtime / 'Resources'), str(folder / 'content'))
    config = json.loads((ROOT / 'heroscript.json').read_text(encoding='utf-8-sig'))
    config.update(runtimeVersion=args.version, contentDirectory='content')
    (folder / 'heroscript.json').write_text(json.dumps(config, indent=2) + '\n', encoding='utf-8')
    engine = re.search(r'CurrentEngineVersion\s*=\s*"([^"]+)"',
                       (ROOT / 'src/Core/Determinism/DeterministicContext.cs').read_text()).group(1)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    manifest = dict(version=args.version, engineVersion=engine, apiContract='v1', platform='win-x64', commit=commit)
    (runtime / 'runtime-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    shutil.copy2(ROOT / 'tools/distribution/Start-HeroScript.ps1', folder)
    (folder / 'Start-HeroScript.bat').write_text('@echo off\npowershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-HeroScript.ps1" %*\n', encoding='ascii')
    shutil.copy2(ROOT / 'docs/distribution.md', folder / 'README.md')
    archive = Path(shutil.make_archive(str(output / name), 'zip', folder))
    (output / (name + '.sha256')).write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + '  ' + archive.name + '\n')
    print(archive)

if __name__ == '__main__':
    main()
