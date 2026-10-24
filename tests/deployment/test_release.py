"""Build-host checks for release contents and real installer behavior with mocked OS commands."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('package_release', ROOT / 'scripts/package-release.py')
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PackagingTests(unittest.TestCase):
    def test_cross_os_native_build_is_rejected_before_creating_artifact(self):
        with tempfile.TemporaryDirectory() as output, patch.object(package.platform, 'system', return_value='Darwin'):
            with self.assertRaises(RuntimeError):
                package.package('win-x64', '1.0.0', Path(output))
            self.assertEqual([], list(Path(output).iterdir()))

    def test_workflows_do_not_upload_actions_artifacts(self):
        import re
        for path in (ROOT / '.github/workflows').glob('*.yml'):
            source = path.read_text()
            self.assertIsNone(re.search(r'uses:\s*actions/(?:upload|download)-artifact', source), path.name)
        release = (ROOT / '.github/workflows/release.yml').read_text()
        self.assertIn("DOCKER_BUILD_RECORD_UPLOAD: 'false'", release)

    def test_dependency_sources_are_pinned_and_https(self):
        lock = json.loads((ROOT / 'deploy/dependencies.json').read_text())
        for key in ('mintOsuApi', 'preview'):
            self.assertRegex(lock[key]['revision'], r'^[0-9a-f]{40}$')
        import re
        source = (ROOT / 'scripts/install-preview-cli.py').read_text()
        revision = re.search(r'REVISION = "([0-9a-f]+)"', source).group(1)
        self.assertEqual(lock['preview']['revision'], revision)
        for key in ('windowsMySql', 'windowsVcRuntime'):
            self.assertTrue(lock[key]['url'].startswith('https://'))

    @unittest.skipIf(os.name == 'nt', 'Bash is validated by the Linux job')
    def test_compose_configure_preserves_existing_credentials(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'configure.sh').write_bytes((ROOT / 'deploy/compose/configure.sh').read_bytes())
            env_file = root / '.env'
            original = 'MINTAPI_API_KEY=retain-me\n'
            env_file.write_text(original)
            docker = root / 'docker'
            docker.write_text('#!/bin/sh\nexit 0\n')
            docker.chmod(0o755)
            subprocess.run(['bash', root / 'configure.sh'], check=True, env=dict(os.environ, PATH=str(root) + os.pathsep + os.environ['PATH']), capture_output=True)
            self.assertEqual(original, env_file.read_text())

    @unittest.skipIf(os.name == 'nt', 'Bash is validated by the Linux job')
    def test_compose_configure_generates_distinct_secrets_and_rejects_invalid_id(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'configure.sh').write_bytes((ROOT / 'deploy/compose/configure.sh').read_bytes())
            docker = root / 'docker'
            docker.write_text('#!/bin/sh\nexit 0\n')
            docker.chmod(0o755)
            env = dict(os.environ, PATH=str(root) + os.pathsep + os.environ['PATH'])
            invalid = subprocess.run(['bash', root / 'configure.sh'], input='mint/test\n1.2.3\nzero\nsecret\n', text=True, env=env, capture_output=True)
            self.assertNotEqual(0, invalid.returncode)
            self.assertFalse((root / '.env').exists())
            subprocess.run(['bash', root / 'configure.sh'], input="mint/test\n1.2.3\n123\nsecret$with'quote\n", text=True, env=env, check=True, capture_output=True)
            values = dict(line.split('=', 1) for line in (root / '.env').read_text().splitlines())
            secrets = [values[k] for k in ('MINTAPI_API_KEY', 'MYSQL_PASSWORD', 'MYSQL_ROOT_PASSWORD', 'REDIS_PASSWORD')]
            self.assertEqual(4, len(set(secrets)))
            for secret in secrets:
                self.assertRegex(secret, r'^[0-9a-f]{48}$')
            self.assertEqual(0o600, (root / '.env').stat().st_mode & 0o777)
            self.assertEqual("'secret$with\\'quote'", values['OSU_CLIENT_SECRET'])


if __name__ == '__main__':
    unittest.main()
