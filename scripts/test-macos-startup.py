#!/usr/bin/env python3
"""Exercise native startup, recovery and session resume from an actual read-only Mac volume."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('runtime', choices=['osx-arm64', 'osx-x64'])
args = parser.parse_args()
archive = ROOT / 'artifacts' / f'TorrentClassic-{args.runtime}-portable.zip'
output = ROOT / 'artifacts' / f'startup-{args.runtime}'
output.mkdir(parents=True, exist_ok=True)
relative_exe = Path('Torrent Classic.app/Contents/MacOS/TorrentClassic.Desktop')


def launch(name, executable, expected_folder, restored, recovery=None):
    case = output / name
    case.mkdir(exist_ok=True)
    result_path = case / 'result.json'
    result_path.unlink(missing_ok=True)
    command = [str(executable), '--startup-smoke-test', str(case)]
    if recovery is not None:
        command.append(str(recovery))
    process = subprocess.run(command, capture_output=True, text=True, timeout=60)
    (case / 'process.log').write_text(process.stdout + process.stderr)
    if process.returncode != 0:
        raise RuntimeError(f'{name} exited with {process.returncode}: {process.stderr[-5000:]}')
    result = json.loads(result_path.read_text())
    assert Path(result['dataDirectory']) == expected_folder / 'Data', result
    assert result['recovered'] == (recovery is not None), result
    assert result['restoredEntries'] == restored, result
    session = json.loads((expected_folder / 'Data/session.json').read_text())
    preferences = json.loads((expected_folder / 'Data/preferences.json').read_text())
    assert len(session) == 1 and session[0]['Label'] == 'Startup regression'
    assert session[0]['SavePath'] == '@portable/Downloads'
    assert preferences['DownloadDirectory'] == '@portable/Downloads'
    assert (case / 'startup-main.png').is_file()
    if recovery is not None:
        assert (case / 'read-only-startup.png').is_file()
    print(f'PASS {name}: native window, expected data folder and saved session', flush=True)


with tempfile.TemporaryDirectory(prefix='TorrentClassic-Startup-') as temporary:
    work = Path(temporary).resolve()
    # Verify the distributed ZIP, including its signed resources, rather than the build output.
    subprocess.run(['ditto', '-x', '-k', str(archive), str(work / 'extracted')], check=True)
    portable = work / 'extracted/TorrentClassic'
    subprocess.run(['codesign', '--verify', '--deep', '--strict', str(portable / 'Torrent Classic.app')], check=True)
    launch('writable-first', portable / relative_exe, portable, 0)
    launch('writable-restart', portable / relative_exe, portable, 1)
    image = work / 'read-only.dmg'
    subprocess.run(['hdiutil', 'create', '-quiet', '-srcfolder', str(portable), '-volname', 'TorrentClassic Startup Test', '-format', 'UDRO', '-o', str(image)], check=True)
    mount = work / 'read-only-volume'
    mount.mkdir()
    subprocess.run(['hdiutil', 'attach', '-quiet', '-readonly', '-nobrowse', '-mountpoint', str(mount), str(image)], check=True)
    try:
        try:
            (mount / '.write-must-fail').write_text('probe')
            raise AssertionError('The test volume is writable')
        except OSError:
            pass
        exe = mount / relative_exe
        # Recover the original portable session, then check fresh-folder startup and restart too.
        launch('read-only-existing-session', exe, portable, 1, portable)
        recovery = work / 'chosen-portable-folder'
        recovery.mkdir()
        launch('read-only-fresh', exe, recovery, 0, recovery)
        launch('read-only-restart', exe, recovery, 1, recovery)
    finally:
        subprocess.run(['hdiutil', 'detach', '-quiet', str(mount)], check=True)
(output / 'result.txt').write_text('PASS: native startup/restart and read-only-volume recovery; fresh and existing portable sessions.\n')
