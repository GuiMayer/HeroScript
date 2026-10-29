"""CLI regression tests. Run after building the tool with --artifacts-path .runtime/tools."""
import hashlib
import shutil
import json
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
DLL = ROOT / '.runtime/tools/bin/GameplayResponseMigration/release/GameplayResponseMigration.dll'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def invoke(setting, *arguments):
    return subprocess.run(['dotnet', str(DLL), '--setting', str(setting), *map(str, arguments)],
                          capture_output=True, text=True, encoding='utf-8', cwd=ROOT)


class PreflightTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            raise RuntimeError('Build tools/GameplayResponseMigration with --artifacts-path .runtime/tools first')

    def test_current_packages_are_read_only(self):
        settings = sorted(path.parent for path in (ROOT / 'data/configs').glob('*/Resources'))
        before = {path: digest(path) for setting in settings for path in setting.rglob('*.json')}
        for setting in settings:
            with self.subTest(setting=setting.name):
                result = invoke(setting)
                self.assertEqual(0, result.returncode, result.stderr + result.stdout)
                report = json.loads(result.stdout)
                self.assertFalse(report['publicationReady'])
                self.assertEqual([], report['errors'])
                self.assertEqual('readOnlyPreflight', report['mode'])
        self.assertEqual(before, {path: digest(path) for path in before})

    def test_proposal_preserves_unrelated_json_and_hashes_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            setting, path, original = self.fixture(temporary)
            result = invoke(setting)
            self.assertEqual(0, result.returncode, result.stderr + result.stdout)
            report = json.loads(result.stdout)
            self.assertEqual(1, report['changedDefinitions'])
            proposal = report['files'][0]
            self.assertEqual(digest(path), proposal['originalSha256'])
            changed = proposal['proposedDocument']['test']
            self.assertEqual(original['test']['presentation'], changed['presentation'])
            self.assertEqual(original['test']['customData'], changed['customData'])
            self.assertNotIn('triggers', changed)
            self.assertEqual('ContainerBoundary', changed['responses'][0]['timing'])
            self.assertEqual(original, json.loads(path.read_text(encoding='utf-8')))

    def test_mixed_authoring_has_no_proposal_and_does_not_write(self):
        with tempfile.TemporaryDirectory() as temporary:
            setting, path, original = self.fixture(temporary)
            original['test']['responses'] = [{'responseId': 'already', 'effects': [
                {'type': 'REMOVE_STATUS', 'statusId': 'absent'}]}]
            path.write_text(json.dumps(original), encoding='utf-8')
            before = digest(path)
            result = invoke(setting)
            self.assertEqual(2, result.returncode)
            report = json.loads(result.stdout)
            self.assertEqual(0, report['changedFiles'])
            self.assertTrue(report['errors'])
            self.assertEqual(before, digest(path))

    def test_output_and_apply_cannot_overwrite_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            setting, path, _ = self.fixture(temporary)
            before = digest(path)
            self.assertEqual(2, invoke(setting, '--output', path).returncode)
            self.assertEqual(2, invoke(setting, '--apply').returncode)
            self.assertEqual(before, digest(path))

    def test_trailing_separator_and_report_errors_preserve_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            setting, path, _ = self.fixture(temporary)
            before = digest(path)
            result = invoke(str(setting) + '/')
            self.assertEqual(0, result.returncode, result.stderr + result.stdout)
            self.assertEqual(1, json.loads(result.stdout)['changedFiles'])
            metadata = setting / 'settings/definition.json'
            metadata.parent.mkdir()
            metadata.write_text('{}', encoding='utf-8')
            self.assertEqual(2, invoke(setting, '--output', metadata).returncode)
            self.assertEqual('{}', metadata.read_text(encoding='utf-8'))
            self.assertEqual(2, invoke(setting, '--output', temporary).returncode)
            self.assertEqual(before, digest(path))

    def test_root_apply_validates_and_backs_up_the_exact_preimage(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / 'packages'
            shutil.copytree(ROOT / 'data/configs', root)
            path = root / 'default/Resources/status-effects/status_effects.json'
            document = json.loads(path.read_text(encoding='utf-8'))
            document['migration_fixture'] = {'statusId': 'migration_fixture', 'triggers': [{
                'triggerId': 'fixture', 'boundary': 'EndRound', 'effects': [
                    {'type': 'REMOVE_STATUS', 'statusId': 'migration_fixture'}]}]}
            path.write_text(json.dumps(document), encoding='utf-8')
            before = path.read_bytes()
            backup = pathlib.Path(temporary) / 'backup'
            result = subprocess.run(['dotnet', str(DLL), '--root', str(root), '--apply', str(backup)],
                capture_output=True, text=True, encoding='utf-8', cwd=ROOT)
            self.assertEqual(0, result.returncode, result.stderr + result.stdout)
            self.assertEqual(before, (backup / path.relative_to(root)).read_bytes())
            self.assertTrue((backup / 'completed').exists())
            current = json.loads(path.read_text(encoding='utf-8'))['migration_fixture']
            self.assertNotIn('triggers', current)
            self.assertEqual('fixture', current['responses'][0]['responseId'])

    def test_root_apply_rejects_invalid_graph_before_writing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / 'packages'
            shutil.copytree(ROOT / 'data/configs', root)
            path = root / 'default/Resources/status-effects/status_effects.json'
            document = json.loads(path.read_text(encoding='utf-8'))
            document['migration_fixture'] = {'statusId': 'migration_fixture', 'triggers': [{
                'triggerId': 'fixture', 'boundary': 'EndRound', 'effects': [
                    {'type': 'REMOVE_STATUS', 'statusId': 'nonexistent_migration_fixture'}]}]}
            path.write_text(json.dumps(document), encoding='utf-8')
            before = {p: digest(p) for p in root.rglob('*.json')}
            result = subprocess.run(['dotnet', str(DLL), '--root', str(root), '--apply', str(pathlib.Path(temporary) / 'backup')],
                capture_output=True, text=True, encoding='utf-8', cwd=ROOT)
            self.assertEqual(2, result.returncode)
            self.assertEqual(before, {p: digest(p) for p in before})

    @staticmethod
    def fixture(temporary):
        setting = pathlib.Path(temporary) / 'setting'
        path = setting / 'Resources/status-effects/test.json'
        path.parent.mkdir(parents=True)
        original = {'test': {'statusId': 'test', 'presentation': {'unknownFutureField': 'preserve'},
            'customData': {'nested': [1, 2, {'future': True}]}, 'triggers': [{
                'triggerId': 'tick', 'boundary': 'EndRound', 'effects': [{'type': 'REMOVE_STATUS', 'statusId': 'absent'}]}]}}
        path.write_text(json.dumps(original), encoding='utf-8')
        return setting, path, original


if __name__ == '__main__':
    unittest.main()
