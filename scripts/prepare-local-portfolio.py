#!/usr/bin/env python3
"""Explicit persistent LOCAL rehearsal secrets. No network/cloud operations."""
import os
from pathlib import Path
import secrets
import subprocess
root = Path(__file__).resolve().parent.parent
folder = root / '.portfolio-local'
folder.mkdir(mode=0o711, exist_ok=True)
if (folder / '.env').exists():
    print('Existing local portfolio secrets retained; nothing regenerated.')
    raise SystemExit(0)
if any(folder.iterdir()):
    raise SystemExit('Local directory is not empty. Review partial initialization manually; no secrets overwritten.')
values = {key: secrets.token_hex(32) for key in ('OWNER_PASSWORD', 'RUNTIME_PASSWORD', 'MAINTENANCE_PASSWORD', 'CERTIFICATE_PASSWORD')}
# External files are created only by this explicit developer command, never by app startup.
subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '365', '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,DNS:postgres,IP:127.0.0.1', '-keyout', str(folder / 'server.key'), '-out', str(folder / 'server.crt')], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
subprocess.run(['openssl', 'pkcs12', '-export', '-inkey', str(folder / 'server.key'), '-in', str(folder / 'server.crt'), '-out', str(folder / 'protection.pfx'), '-passout', 'env:FLEETFLOW_LOCAL_CERT_PASSWORD'], env={**os.environ, 'FLEETFLOW_LOCAL_CERT_PASSWORD': values['CERTIFICATE_PASSWORD']}, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
(folder / '.env').write_text(''.join(f'{key}={value}\n' for key, value in values.items()))
for name in ('server.key', '.env'):
    (folder / name).chmod(0o600)
# Read-only app containers need the encrypted PFX; its password remains in runtime secrets.
(folder / 'protection.pfx').chmod(0o644)
(folder / 'server.crt').chmod(0o644)
print('Persistent local rehearsal secrets created in ignored .portfolio-local/. No values printed.')
