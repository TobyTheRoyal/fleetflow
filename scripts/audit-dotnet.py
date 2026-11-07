#!/usr/bin/env python3
"""Fail on known direct or transitive NuGet vulnerabilities; no runtime secrets needed."""
import json
import subprocess
import sys
from pathlib import Path
root = Path(__file__).resolve().parent.parent
result = subprocess.run(["dotnet", "list", "backend/FleetFlow.slnx", "package", "--vulnerable", "--include-transitive", "--format", "json", "--no-restore"], cwd=root, capture_output=True, text=True)
if result.returncode:
    print(result.stderr, file=sys.stderr)
    sys.exit(result.returncode)
report = json.loads(result.stdout)
if report.get('problems') or not isinstance(report.get('projects'), list) or not report['projects']:
    print('NuGet audit did not produce complete project results; inspect package feed connectivity.', file=sys.stderr)
    sys.exit(1)
findings = []
for project in report.get("projects", []):
    for framework in project.get("frameworks", []):
        for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
            for advisory in package.get("vulnerabilities", []):
                findings.append((package["id"], package["resolvedVersion"], advisory["severity"], advisory["advisoryurl"]))
for finding in sorted(set(findings)):
    print(" | ".join(finding))
print(f"NuGet audit: {len(set(findings))} known package/advisory findings.")
sys.exit(bool(findings))
