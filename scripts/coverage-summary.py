#!/usr/bin/env python3
"""Print per-suite coverage without incorrectly averaging unrelated coverage runs."""
import json
from pathlib import Path
from xml.etree import ElementTree
root = Path(__file__).resolve().parent.parent
for suite in sorted((root / 'artifacts/backend').glob('*')):
    files = sorted(suite.rglob('*cobertura*.xml'), key=lambda p: p.stat().st_mtime)
    if not files:
        continue
    report = ElementTree.parse(files[-1]).getroot()
    print(f'{suite.name}: line {float(report.attrib["line-rate"]):.2%}, branch {float(report.attrib["branch-rate"]):.2%}')
    for package in report.findall('./packages/package'):
        print(f'  {package.attrib["name"]}: line {float(package.attrib["line-rate"]):.2%}, branch {float(package.attrib["branch-rate"]):.2%}')
frontend = root / 'frontend/coverage/coverage-summary.json'
if frontend.exists():
    total = json.loads(frontend.read_text())['total']
    print('Frontend component suite: ' + ', '.join(f'{metric} {total[metric]["pct"]}%' for metric in ('lines', 'branches', 'functions', 'statements')))
