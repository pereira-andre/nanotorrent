#!/usr/bin/env python3
"""Encode the checked-in PNG artwork as native Windows/macOS/Linux app icons (macOS tools)."""
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent
source = ROOT / 'docs/icons/penguin-magnet.png'
with tempfile.TemporaryDirectory(prefix='NanoTorrent-Icons-') as temporary:
    work = Path(temporary)
    frames = []
    for size in [16, 24, 32, 48, 64, 128, 256]:
        png = work / f'{size}.png'
        subprocess.run(['sips', '-z', str(size), str(size), str(source), '--out', str(png)], check=True, stdout=subprocess.DEVNULL)
        frames.append((size, png.read_bytes()))
    offset = 6 + 16 * len(frames)
    directory = []
    for size, data in frames:
        directory.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    (ROOT / 'src/TorrentClassic.App/app.ico').write_bytes(struct.pack('<HHH', 0, 1, len(frames)) + b''.join(directory) + b''.join(data for size, data in frames))
    shutil.copy2(work / '256.png', ROOT / 'docs/icon.png')
    shutil.copy2(work / '128.png', ROOT / 'src/TorrentClassic.Core/Remote/icon.png')
    iconset = work / 'NanoTorrent.iconset'
    iconset.mkdir()
    for size in [16, 32, 128, 256, 512]:
        for scale in [1, 2]:
            name = f'icon_{size}x{size}' + ('@2x' if scale == 2 else '') + '.png'
            subprocess.run(['sips', '-z', str(size * scale), str(size * scale), str(source), '--out', str(iconset / name)], check=True, stdout=subprocess.DEVNULL)
    subprocess.run(['iconutil', '-c', 'icns', str(iconset), '-o', str(ROOT / 'docs/NanoTorrent.icns')], check=True)
print('Encoded app.ico, NanoTorrent.icns and portable/remote PNG icons from the penguin artwork.')
