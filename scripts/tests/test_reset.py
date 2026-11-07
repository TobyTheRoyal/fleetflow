import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class ResetGuardTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'scripts').mkdir()
        (self.root / '.env').write_text('POSTGRES_PASSWORD=fictional-test-only\n')
        for name in ('reset-demo.sh', 'setup-env.sh', 'demo-users.sh'):
            shutil.copy(Path(__file__).resolve().parents[1] / name, self.root / 'scripts' / name)
        binary = self.root / 'bin'
        binary.mkdir()
        fake = binary / 'docker'
        fake.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$CALLS"\n')
        fake.chmod(0o755)
        self.calls = self.root / 'calls'
        self.env = {**os.environ, 'PATH': str(binary) + os.pathsep + os.environ['PATH'], 'CALLS': str(self.calls), 'COMPOSE_PROJECT_NAME': 'unrelated-project'}

    def test_no_docker_mutation_without_explicit_confirmation(self):
        result = subprocess.run(['sh', str(self.root / 'scripts/reset-demo.sh')], env=self.env, capture_output=True)
        self.assertEqual(result.returncode, 2)
        self.assertFalse(self.calls.exists())

    def test_confirmed_reset_names_only_development_project(self):
        result = subprocess.run(['sh', str(self.root / 'scripts/reset-demo.sh'), '--confirm'], env=self.env, capture_output=True)
        self.assertEqual(result.returncode, 0)
        self.assertEqual(self.calls.read_text().splitlines(), [
            'compose -p fleetflow down --volumes',
            'compose -p fleetflow up --build -d --wait',
            'compose -p fleetflow run --rm --no-deps api --migrate --seed',
            'compose -p fleetflow --env-file .env --env-file .env.demo run --rm --no-deps api --migrate --seed-users',
        ])


if __name__ == '__main__':
    unittest.main()
