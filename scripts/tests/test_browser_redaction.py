import base64
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
spec = importlib.util.spec_from_file_location('redact', Path(__file__).parents[1] / 'redact-browser-artifacts.py')
redact = importlib.util.module_from_spec(spec)
spec.loader.exec_module(redact)

class BrowserRedactionTests(unittest.TestCase):
    def test_trace_and_embedded_html_report_remove_credentials(self):
        password = 'Ff!7' + 'a1' * 24
        cookie = 'synthetic-session-cookie-canary'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data = io.BytesIO()
            with zipfile.ZipFile(data, 'w') as archive:
                archive.writestr('0.network', json.dumps({'headers': [{'name': 'Cookie', 'value': 'FleetFlow.Session=' + cookie}]}))
                archive.writestr('0.trace', json.dumps({'params': {'value': password}, 'message': cookie}))
            (root / 'trace.zip').write_bytes(data.getvalue())
            (root / 'index.html').write_text('data:application/zip;base64,' + base64.b64encode(data.getvalue()).decode())
            (root / 'run.log').write_text(password)
            redact.sanitize([root])
            with zipfile.ZipFile(root / 'trace.zip') as archive:
                self.assertNotIn(cookie, archive.read('0.trace').decode())
                self.assertNotIn(password, archive.read('0.trace').decode())
            embedded = (root / 'index.html').read_text().split('base64,')[1]
            with zipfile.ZipFile(io.BytesIO(base64.b64decode(embedded))) as archive:
                self.assertNotIn(cookie, archive.read('0.trace').decode())
            self.assertNotIn(password, (root / 'run.log').read_text())
