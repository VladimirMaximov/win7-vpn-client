#!/usr/bin/env python3
"""Build one Windows 7 x64 executable, embedding all runtime components."""
import argparse
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'src/VpnClient'
DEPS = ROOT / '.deps'


def load_preparer():
    spec = importlib.util.spec_from_file_location('prepare_deps', ROOT / 'scripts/prepare-deps.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sdk_compiler(dotnet):
    lines = subprocess.check_output([dotnet, '--list-sdks'], text=True).splitlines()
    versions = []
    for line in lines:
        match = re.match(r'(\S+) \[(.+)\]', line)
        if match:
            versions.append(Path(match[2]) / match[1] / 'Roslyn/bincore/csc.dll')
    if not versions:
        raise RuntimeError('Install a .NET SDK for cross-compilation, or use the Windows .NET 3.5 compiler')
    return versions[-1]


def build(args):
    native = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework/v3.5/csc.exe'
    cross = args.dotnet is not None or os.name != 'nt' or not native.exists()
    load_preparer().prepare(with_references=cross)
    bundle = ROOT / 'artifacts/bundle'
    bundle.mkdir(parents=True, exist_ok=True)
    output = ROOT / 'dist/VPN.exe'
    output.parent.mkdir(parents=True, exist_ok=True)
    files = {
        'mihomo.exe': DEPS / 'mihomo.exe',
        'xray.exe': DEPS / 'xray.exe',
        'YamlDotNet.dll': DEPS / 'YamlDotNet.dll',
        'LICENSE.txt': ROOT / 'LICENSE',
        'LICENSE-mihomo.txt': ROOT / 'third_party/licenses/LICENSE-mihomo.txt',
        'LICENSE-Xray.txt': ROOT / 'third_party/licenses/LICENSE-Xray.txt',
        'LICENSE-YamlDotNet.txt': ROOT / 'third_party/licenses/LICENSE-YamlDotNet.txt',
    }
    expected = dict(re.findall(r'\{\s*"([^"\n]+)"\s*,\s*"([0-9a-f]{64})"\s*\}', (SRC / 'Assets.cs').read_text()))
    for name, path in files.items():
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != expected.get(name):
            raise RuntimeError('Embedded hash mismatch: ' + name + '. Update the pinned Assets.cs hashes deliberately.')
        (bundle / (name + '.gz')).write_bytes(gzip.compress(data, compresslevel=6, mtime=0))

    options = ['/nologo', '/codepage:65001', '/langversion:3', '/target:winexe',
               '/platform:x64', '/optimize+', '/main:MihomoDesk.Bootstrap',
               '/out:' + str(output), '/win32icon:' + str(ROOT / 'resources/VPN.ico')]
    assemblies = ['mscorlib', 'System', 'System.Core', 'System.Security', 'System.Windows.Forms', 'System.Drawing']
    if cross:
        dotnet = args.dotnet or shutil.which('dotnet')
        if not dotnet:
            raise RuntimeError('Install .NET SDK or pass --dotnet /absolute/path/to/dotnet')
        refs = DEPS / 'net35-reference-assemblies/build/.NETFramework/v3.5'
        command = [str(dotnet), str(sdk_compiler(str(dotnet)))]
        options.append('/nostdlib+')
        options.extend('/r:' + str(refs / (a + '.dll')) for a in assemblies)
    else:
        command = [str(native)]
        options.extend('/r:' + a + '.dll' for a in assemblies if a != 'mscorlib')
    options.append('/r:' + str(DEPS / 'YamlDotNet.dll'))
    options.extend('/resource:' + str(bundle / (name + '.gz')) + ',assets.' + name + '.gz' for name in files)
    options.extend(str(path) for path in sorted(SRC.glob('*.cs')))
    subprocess.run(command + options, cwd=ROOT, check=True)
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix('.sha256').write_text(checksum + '  VPN.exe\n')
    print('Built:', output)
    print('Target: Windows 7 SP1 x64 / .NET Framework 3.5')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', help='Path to dotnet; enables cross-compilation using .NET 3.5 references')
    build(parser.parse_args())
