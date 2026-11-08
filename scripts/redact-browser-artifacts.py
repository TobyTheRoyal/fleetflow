#!/usr/bin/env python3
"""Sanitize synthetic browser diagnostics before CI upload; fail closed on malformed archives."""
import base64
import io
import json
from pathlib import Path
import re
import sys
import zipfile

PASSWORD = re.compile(r'Ff!7[a-fA-F0-9]{32,}')
HUB_TOKEN = re.compile(r'(/hubs/logistics\?[^\s"<>]*?\bid=)[^&#\s"<>\\]+')
EMBEDDED = re.compile(r'data:application/zip;base64,([A-Za-z0-9+/=]+)')
SENSITIVE = {'password', 'currentpassword', 'newpassword', 'initialpassword', 'authorization', 'cookie', 'set-cookie', 'x-csrf-token', 'token', 'requesttoken', 'connectiontoken', 'accesstoken'}

def collect(value, secrets):
    if isinstance(value, dict):
        name = str(value.get('name', '')).lower()
        if (name in SENSITIVE or name.startswith('fleetflow.')) and isinstance(value.get('value'), str):
            secrets.add(value['value'])
            for part in value['value'].split(';'):
                if '=' in part:
                    secrets.add(part.split('=', 1)[1].strip())
        for key, child in value.items():
            if key.lower() in SENSITIVE and isinstance(child, str):
                secrets.add(child)
            collect(child, secrets)
    elif isinstance(value, list):
        for child in value:
            collect(child, secrets)

def objects(text):
    for line in text.splitlines():
        try:
            yield json.loads(line)
        except (ValueError, TypeError):
            pass

def scrub(text, secrets):
    text = PASSWORD.sub('[REDACTED]', text)
    text = HUB_TOKEN.sub(r'\1[REDACTED]', text)
    for secret in sorted((s for s in secrets if len(s) >= 8), key=len, reverse=True):
        text = text.replace(secret, '[REDACTED]')
    return EMBEDDED.sub(lambda match: 'data:application/zip;base64,' + base64.b64encode(archive(base64.b64decode(match[1]), secrets)).decode(), text)

def archive(data, secrets):
    with zipfile.ZipFile(io.BytesIO(data)) as source:
        entries = [(entry, source.read(entry)) for entry in source.infolist()]
    for _, data in entries:
        try:
            for obj in objects(data.decode('utf8')):
                collect(obj, secrets)
        except UnicodeDecodeError:
            pass
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as target:
        for entry, data in entries:
            try:
                data = scrub(data.decode('utf8'), secrets).encode()
            except UnicodeDecodeError:
                pass
            target.writestr(entry, data)
    return output.getvalue()

def sanitize(paths):
    files = [p for root in paths if root.exists() for p in ([root] if root.is_file() else root.rglob('*')) if p.is_file()]
    secrets = set()
    for p in files:
        if p.suffix != '.zip':
            try:
                for obj in objects(p.read_text()):
                    collect(obj, secrets)
            except UnicodeDecodeError:
                pass
    for p in files:
        if p.suffix == '.zip':
            p.write_bytes(archive(p.read_bytes(), secrets))
        else:
            try:
                p.write_text(scrub(p.read_text(), secrets))
            except UnicodeDecodeError:
                pass
    return len(files)

if __name__ == '__main__':
    paths = [Path(arg) for arg in sys.argv[1:]] or [Path('frontend/test-results'), Path('frontend/playwright-report'), Path('artifacts/e2e')]
    print(f'Sanitized {sanitize(paths)} browser diagnostic files; no credential values printed.')
