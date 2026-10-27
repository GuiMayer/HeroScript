import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("migration", Path(__file__).parents[1] / "migrate-activation-sequence.py")
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)


class MigrationTests(unittest.TestCase):
    def test_preserves_explicit_policy_and_other_values(self):
        source = {"a": {"combatRulesId": "a", "flow": {"value": 4}},
                  "b": {"combatRulesId": "b", "flow": {"activationSequence": {"strategy": "Circular"}}}}
        self.assertEqual(["a"], migration.migrate_document(source))
        self.assertEqual(4, source["a"]["flow"]["value"])
        self.assertEqual("Circular", source["b"]["flow"]["activationSequence"]["strategy"])
        self.assertEqual([], migration.migrate_document(source))

    def test_rejects_missing_flow(self):
        with self.assertRaises(ValueError):
            migration.migrate_document({"invalid": {"combatRulesId": "invalid"}})


if __name__ == "__main__":
    unittest.main()
