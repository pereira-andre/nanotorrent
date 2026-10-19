#!/usr/bin/env python3
"""Build a self-contained, portable macOS or Linux distribution and retain licenses."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
os.chdir(ROOT)
parser = argparse.ArgumentParser()
parser.add_argument('runtime', choices=['osx-arm64', 'osx-x64', 'linux-x64', 'linux-arm64'])
args = parser.parse_args()
rid = args.runtime
mac = rid.startswith('osx-')
if mac and sys.platform != 'darwin':
    parser.error('Create and ad-hoc sign macOS bundles on macOS.')
artifacts = ROOT / 'artifacts'
package = artifacts / rid / 'NanoTorrent'
if package.exists():
    shutil.rmtree(package)
package.mkdir(parents=True)
bundle = package / 'NanoTorrent.app'
publish = bundle / 'Contents' / 'MacOS' if mac else package
publish.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, AVALONIA_TELEMETRY_OPTOUT='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
subprocess.run(['dotnet', 'publish', 'src/TorrentClassic.Desktop', '-c', 'Release', '-r', rid, '--self-contained', 'true', '-p:PublishTrimmed=false', '-o', str(publish)], check=True, env=env)
for name in ['LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md']:
    shutil.copy2(ROOT / name, package / name)
license_dir = package / 'licenses'
shutil.copytree(ROOT / 'licenses', license_dir)
shutil.copy2(ROOT / 'vendor/MonoTorrent.Client/README.md', license_dir / 'MonoTorrent-LOCAL-CHANGES.md')
cache = Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages'))
assets = json.loads((ROOT / 'src/TorrentClassic.Desktop/obj/project.assets.json').read_text())
manifest = ['# Build dependency manifest', '', 'License declarations below are read from the restored NuGet packages.', 'Build-only and other-platform packages are listed as well for transparency.', '', '| Package | Version | Declared license |', '| --- | --- | --- |']
for name, lib in sorted(assets['libraries'].items()):
    if lib['type'] != 'package':
        continue
    folder = cache / lib['path']
    metadata = ET.parse(next(folder.glob('*.nuspec'))).getroot().find('{*}metadata')
    declared = metadata.find('{*}license')
    license_name = declared.text if declared is not None else 'See retained notices'
    manifest.append(f'| {name.rsplit("/", 1)[0]} | {name.rsplit("/", 1)[1]} | {license_name} |')
    # Preserve all original license and bundled third-party notice texts, including Skia/HarfBuzz.
    for path in folder.rglob('*'):
        if path.is_file() and any(x in path.name.lower() for x in ['license', 'licence', 'notice', 'copying']):
            target = license_dir / (name.replace('/', '-') + '-' + str(path.relative_to(folder)).replace('/', '-'))
            shutil.copy2(path, target)
config = json.loads((publish / 'NanoTorrent.runtimeconfig.json').read_text())
for framework in config['runtimeOptions']['includedFrameworks']:
    folder = cache / (framework['name'].lower() + '.runtime.' + rid) / framework['version']
    found = []
    for path in folder.iterdir():
        if path.is_file() and path.name.upper() in ['LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']:
            shutil.copy2(path, license_dir / (framework['name'] + '-' + path.name)); found.append(path)
    if not found:
        raise RuntimeError('Missing runtime notices for ' + framework['name'])
(license_dir / 'PACKAGE-MANIFEST.md').write_text('\n'.join(manifest) + '\n')
exe = publish / 'NanoTorrent'
exe.chmod(0o755)
if mac:
    resources = bundle / 'Contents/Resources'
    resources.mkdir()
    shutil.copy2(ROOT / 'docs/NanoTorrent.icns', resources / 'NanoTorrent.icns')
    version = ET.parse(ROOT / 'src/TorrentClassic.Desktop/TorrentClassic.Desktop.csproj').getroot().findtext('.//Version')
    info = dict(CFBundleName='NanoTorrent', CFBundleDisplayName='NanoTorrent', CFBundleIdentifier='org.nanotorrent.desktop', CFBundleExecutable='NanoTorrent', CFBundlePackageType='APPL', CFBundleShortVersionString=version, CFBundleVersion=version, CFBundleIconFile='NanoTorrent', LSMinimumSystemVersion='14.0', NSHighResolutionCapable=True)
    (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps(info))
    # macOS treats files in MacOS/ as nested code too. Sign each file first.
    # This is ad-hoc, not Developer ID/notarization.
    for path in sorted(publish.iterdir()):
        if path.is_file() and path != exe:
            subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', str(path)], check=True, capture_output=True)
    subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', str(bundle)], check=True)
    subprocess.run(['codesign', '--verify', '--deep', '--strict', str(bundle)], check=True)
    archive = artifacts / f'NanoTorrent-{rid}-portable.zip'
    if archive.exists(): archive.unlink()
    subprocess.run(['ditto', '-c', '-k', '--keepParent', str(package), str(archive)], check=True)
else:
    launcher = package / 'NanoTorrent.sh'
    launcher.write_text('#!/bin/sh\nset -eu\nAPP_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)\nexec "$APP_DIR/NanoTorrent" "$@"\n')
    launcher.chmod(0o755)
    shutil.copy2(ROOT / 'docs/icon.png', package / 'NanoTorrent.png')
    archive = artifacts / f'NanoTorrent-{rid}-portable.tar.gz'
    with tarfile.open(archive, 'w:gz') as tar:
        tar.add(package, arcname='NanoTorrent')
digest = hashlib.file_digest(archive.open('rb'), 'sha256').hexdigest()
Path(str(archive) + '.sha256').write_text(f'{digest}  {archive.name}\n')
print(f'Portable package: {archive} ({archive.stat().st_size / 1024 / 1024:.1f} MiB)')
