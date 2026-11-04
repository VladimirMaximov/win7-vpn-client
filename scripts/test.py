#!/usr/bin/env python3
"""Run portable tests. They use only synthetic credentials and stub subscriptions."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default=shutil.which('dotnet'))
    args = parser.parse_args()
    if not args.dotnet:
        parser.error('Install .NET SDK 8 or pass --dotnet /path/to/dotnet')
    subprocess.run([sys.executable, str(ROOT / 'scripts/prepare-deps.py')], check=True)
    for name in ['Core.Tests', 'Subscriptions.Tests', 'Diagnostics.Tests', 'Catalog.Tests']:
        subprocess.run([args.dotnet, 'run', '--project', str(ROOT / 'tests' / name / (name + '.csproj'))], cwd=ROOT, check=True)
