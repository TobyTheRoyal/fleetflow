#!/usr/bin/env python3
"""Check public Git candidates without printing any local credential values."""
import subprocess
import sys
from pathlib import Path
root = Path(__file__).resolve().parent.parent
paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=root).decode().split('\0')
forbidden = {'AGENTS.md', 'FLEETFLOW_SPEC.md', 'CODEX_MILESTONES.md', 'README_FIRST.md', 'appsettings.Local.json'}
secret_values = []
local_files = [root / '.env', root / '.env.demo', root / '.portfolio-local' / '.env']
for local in local_files:
 if local.exists():
    for row in local.read_text().splitlines():
        key, separator, value = row.partition('=')
        if separator and any(word in key.upper() for word in ('PASSWORD', 'TOKEN', 'SECRET')):
            value = value.strip().strip('\"\'')
            if len(value) >= 12:
                secret_values.append(value.encode())
issues = []
for relative in filter(None, paths):
    path = root / relative
    name = path.name
    if name in forbidden or name.endswith(('.pfx', '.p12', '.key', '.pfx.b64', '.p12.b64')) or (name.startswith('.env') and name != '.env.example'):
        issues.append(relative + ': private instruction/configuration filename')
    elif path.is_file() and any(value in path.read_bytes() for value in secret_values):
        issues.append(relative + ': contains a local credential value')
for issue in issues:
    print(issue)
print(f'Public-file check: {len(issues)} findings.')
sys.exit(bool(issues))
