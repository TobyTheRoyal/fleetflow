#!/usr/bin/env python3
"""Local HTTPS/restart smoke test. Never sends inference or contacts a cloud host."""
import argparse
import http.cookiejar
import json
import re
from pathlib import Path
import ssl
import subprocess
import time
import urllib.error
import urllib.request
parser = argparse.ArgumentParser()
parser.add_argument('--confirm-local-restart', action='store_true', required=True)
parser.parse_args()
root = Path(__file__).resolve().parent.parent
base = 'https://localhost:5443'
compose = ['docker', 'compose', '--env-file', str(root / '.portfolio-local/.env'), '-f', str(root / 'compose.portfolio.yaml')]
context = ssl.create_default_context(cafile=str(root / '.portfolio-local/server.crt'))
jar = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPSHandler(context=context), urllib.request.HTTPCookieProcessor(jar))
def request(path, body=None, csrf=None, expected=200):
    headers = {'Accept': 'application/json'}
    if csrf: headers['X-CSRF-TOKEN'] = csrf
    data = None if body is None else json.dumps(body).encode()
    if data is not None: headers['Content-Type'] = 'application/json'
    try: response = client.open(urllib.request.Request(base + path, data=data, headers=headers), timeout=15)
    except urllib.error.HTTPError as failure: response = failure
    assert response.status == expected, f'{path}: expected {expected}, received {response.status}'
    raw = response.read()
    return (json.loads(raw) if raw and 'json' in response.headers.get('Content-Type','') else raw), response.headers
deadline = time.monotonic() + 45
while True:
    try:
        request('/health/ready')
        break
    except (OSError, urllib.error.URLError):
        if time.monotonic() > deadline: raise
        time.sleep(0.2)
request('/api/v1/shipments', expected=401)
html, headers = request('/demo')
assert "frame-ancestors 'none'" in headers['Content-Security-Policy']
assert headers['X-Frame-Options'] == 'DENY'
assert 'max-age=' in headers['Strict-Transport-Security']
asset = re.search(rb'src="(/assets/[^\"]+\.js)"', html)
assert asset is not None
_, asset_headers = request(asset.group(1).decode())
assert 'immutable' in asset_headers['Cache-Control']
request('/api/v1/demo/sessions', {'mode':'Viewer'}, expected=400)
csrf = request('/api/v1/auth/csrf')[0]['token']
viewer, _ = request('/api/v1/demo/sessions', {'mode':'Viewer'}, csrf)
assert viewer['role'] == 'Viewer' and viewer['demo']['mode'] == 'Viewer'
csrf = request('/api/v1/auth/csrf')[0]['token']
request('/api/v1/customers', {'name':'Forbidden'}, csrf, 403)
request('/api/v1/users', expected=403)
request('/api/v1/insights/ai-analysis', {'snapshotFingerprint':'a'*64}, csrf, 403)
request('/api/v1/auth/logout', {}, csrf)
csrf = request('/api/v1/auth/csrf')[0]['token']
user, _ = request('/api/v1/demo/sessions', {'mode':'Dispatcher'}, csrf)
csrf = request('/api/v1/auth/csrf')[0]['token']
customer, _ = request('/api/v1/customers', {'name':'Production restart fictional record','contactName':'Example','email':'restart@example.test'}, csrf, 201)
summary, headers = request('/api/v1/dashboard/summary')
assert headers['Cache-Control'] == 'no-store' and summary['totalNonCancelledShipments'] > 0
subprocess.run(compose + ['restart','web'], cwd=root, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
deadline = time.monotonic() + 45
while True:
    try:
        restored, _ = request('/api/v1/auth/me')
        break
    except (OSError, urllib.error.URLError):
        if time.monotonic() > deadline: raise
        time.sleep(0.2)
assert restored['id'] == user['id'] and restored['demo']['leaseId'] == user['demo']['leaseId']
records, _ = request('/api/v1/customers?pageSize=100')
assert any(item['id'] == customer['id'] for item in records['items'])
csrf = request('/api/v1/auth/csrf')[0]['token']
request('/api/v1/auth/logout', {}, csrf)
request('/api/v1/shipments', expected=401)
print('Local Production HTTPS: verified CA/hostname, cookies/CSRF, Viewer restrictions, Dispatcher persistence, restart-safe session, headers and logout passed. No inference requested.')
