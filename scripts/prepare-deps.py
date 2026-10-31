#!/usr/bin/env python3
"""Download pinned upstream build inputs into the ignored .deps directory."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DEPS = ROOT / '.deps'
MANIFEST = json.loads((ROOT / 'third_party/dependencies.json').read_text())


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def archive_for(item):
    cache = DEPS / 'cache'
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / item['archive']
    if archive.exists():
        if digest(archive) != item['sha256']:
            raise RuntimeError('Checksum mismatch in cached archive: ' + archive.name)
        return archive
    temporary = archive.with_suffix(archive.suffix + '.tmp')
    try:
        request = urllib.request.Request(item['url'], headers={'User-Agent': 'win7-vpn-client-build'})
        with urllib.request.urlopen(request, timeout=90) as response, temporary.open('wb') as output:
            shutil.copyfileobj(response, output)
        if digest(temporary) != item['sha256']:
            raise RuntimeError('Checksum mismatch in download: ' + archive.name)
        temporary.replace(archive)
    finally:
        temporary.unlink(missing_ok=True)
    return archive


def prepare(with_references=False):
    DEPS.mkdir(parents=True, exist_ok=True)
    for item in MANIFEST['dependencies']:
        target = DEPS / item['output']
        if target.exists() and digest(target) == item['output_sha256']:
            print('Verified:', item['output'])
            continue
        with zipfile.ZipFile(archive_for(item)) as archive:
            data = archive.read(item['member'])
        if hashlib.sha256(data).hexdigest() != item['output_sha256']:
            raise RuntimeError('Extracted file checksum mismatch: ' + item['output'])
        temporary = target.with_suffix(target.suffix + '.tmp')
        temporary.write_bytes(data)
        temporary.replace(target)
        print('Prepared:', item['output'])
    if with_references:
        # Reference assemblies, not a runtime; only used to cross-compile on macOS/Linux.
        folder = DEPS / 'net35-reference-assemblies'
        with zipfile.ZipFile(archive_for(MANIFEST['reference_assemblies'])) as archive:
            for name in archive.namelist():
                relative = PurePosixPath(name)
                if relative.is_absolute() or '..' in relative.parts:
                    raise RuntimeError('Invalid reference archive path')
            archive.extractall(folder)
        print('Prepared: .NET Framework 3.5 reference assemblies')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--with-reference-assemblies', action='store_true')
    prepare(parser.parse_args().with_reference_assemblies)
