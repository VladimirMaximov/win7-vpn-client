#!/usr/bin/env python3
"""Check the Git-visible text files for common credential leaks (not a full security audit)."""
from pathlib import Path
import re
import subprocess
import sys
import urllib.parse

ROOT = Path(__file__).resolve().parents[1]
PATTERNS = [
    re.compile(r'-----BEGIN (?:OPENSSH |RSA |EC |DSA )?PRIVATE KEY-----'),
    re.compile(r'\bgh[pousr]_[A-Za-z0-9]{30,}\b'),
    re.compile(r'\bgithub_pat_[A-Za-z0-9_]{40,}\b'),
]


def main():
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    problems = []
    for name in set(filter(None, names)):
        path = ROOT / name
        if not path.is_file():
            continue
        data = path.read_bytes()
        if b'\0' in data:
            continue
        text = data.decode('utf-8', errors='replace')
        if any(pattern.search(text) for pattern in PATTERNS):
            problems.append((name, 'private key or access token'))
        for url in re.findall(r'https://[^\s<>"\']+', text):
            try:
                host = urllib.parse.urlsplit(url).hostname or ''
            except ValueError:
                continue  # Source code can contain a regex rather than a URL literal.
            if host.endswith('.azure-api.net'):
                problems.append((name, 'possible private subscription URL'))
        for match in re.finditer(r'(?:vless|vmess)://([0-9a-fA-F-]{36})@', text):
            if match.group(1) != '11111111-1111-4111-8111-111111111111':
                problems.append((name, 'non-test proxy identity'))
    if problems:
        for name, reason in problems:
            print(name + ': ' + reason)
        return 1
    print('PASS: Git-visible files contain no matched credential patterns')
    return 0


if __name__ == '__main__':
    sys.exit(main())
