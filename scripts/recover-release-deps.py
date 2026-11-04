#!/usr/bin/env python3
"""Recover checksum-pinned dependencies embedded in an earlier release.

This preserves reproducibility when an upstream release asset is removed.
Only bytes matching the dependency manifest are accepted.
"""
import hashlib
import json
from pathlib import Path
import sys
import zlib

root = Path(__file__).resolve().parents[1]
items = json.loads((root / 'third_party/dependencies.json').read_text())['dependencies']
expected = {item['output_sha256']: item['output'] for item in items}
blob = Path(sys.argv[1]).read_bytes()
folder = root / '.deps'
folder.mkdir(exist_ok=True)
position = 0
recovered = set()
while True:
    position = blob.find(b'\x1f\x8b\x08', position)
    if position < 0:
        break
    try:
        stream = zlib.decompressobj(31)
        data = stream.decompress(blob[position:], 128 * 1024 * 1024)
        name = expected.get(hashlib.sha256(data).hexdigest()) if stream.eof else None
        if name:
            (folder / name).write_bytes(data)
            recovered.add(name)
            print('Recovered and verified:', name)
    except zlib.error:
        pass
    position += 3
if 'xray.exe' not in recovered:
    raise SystemExit('Pinned Xray engine not present in previous release')
