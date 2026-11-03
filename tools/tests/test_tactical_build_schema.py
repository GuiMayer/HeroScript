"""Published build examples must agree with the documented JSON schema.

Run with Python and jsonschema: python -m unittest discover -s tools/tests
-p test_tactical_build_schema.py. Publication remains the semantic authority.
"""
import copy
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[2]
SCHEMA = json.loads((ROOT / "docs/schemas/build-composition-content.schema.json").read_text(encoding="utf-8"))
KINDS = {
    "deck-composition-policies": "deck-composition-policy", "deck-modules": "deck-module",
    "supports": "support", "support-routines": "support-routine", "support-loadout-policies": "support-loadout-policy",
}


def validator(kind):
    return Draft202012Validator({"$schema": SCHEMA["$schema"], "$defs": SCHEMA["$defs"], "$ref": "#/$defs/" + kind})


class TacticalBuildSchemaTests(unittest.TestCase):
    def test_published_build_definitions(self):
        Draft202012Validator.check_schema(SCHEMA)
        count = 0
        for setting in ["prototype-test", "support-lab", "reward-lab", "payment-lab"]:
            package = ROOT / "data/configs" / setting
            manifest = json.loads((package / "package.json").read_text(encoding="utf-8"))
            for entry in manifest["content"]:
                if entry["kind"] not in KINDS:
                    continue
                for identity, definition in json.loads((package / entry["sourcePath"]).read_text(encoding="utf-8")).items():
                    with self.subTest(setting=setting, identity=identity):
                        validator(KINDS[entry["kind"]]).validate(definition)
                    count += 1
        self.assertGreaterEqual(count, 12)

    def test_published_card_payments(self):
        path = ROOT / "data/configs/prototype-test/Resources/cards.json"
        def visit(value):
            if isinstance(value, dict):
                for key, child in value.items():
                    if key == "zonePayments":
                        for payment in child:
                            validator("card-zone-payment").validate(payment)
                            yield payment
                    else:
                        yield from visit(child)
            elif isinstance(value, list):
                for child in value:
                    yield from visit(child)
        self.assertEqual(6, len(list(visit(json.loads(path.read_text(encoding="utf-8"))))))

    def test_support_requires_explicit_cadence_and_outcome_policy(self):
        value = json.loads((ROOT / "data/configs/prototype-test/Resources/supports.json").read_text(encoding="utf-8"))["prototype_sentinel"]
        for property_name in ["ownerActivationInterval", "countsForOutcome"]:
            changed = copy.deepcopy(value)
            changed.pop(property_name)
            self.assertFalse(validator("support").is_valid(changed))
        for interval in [0, 10001]:
            self.assertFalse(validator("support").is_valid(value | {"ownerActivationInterval": interval}))

    def test_inventory_and_revision_policies_are_closed(self):
        value = json.loads((ROOT / "data/configs/prototype-test/Resources/support-loadout-policies.json").read_text(encoding="utf-8"))["prototype_supports"]
        self.assertFalse(validator("support-loadout-policy").is_valid(value | {"inventoryScope": "Unknown"}))
        self.assertFalse(validator("support-loadout-policy").is_valid(value | {"revisionMigration": "SilentlyRewriteHistory"}))
        self.assertTrue(validator("support-loadout-policy").is_valid(value | {"revisionMigration": None}))


if __name__ == "__main__":
    unittest.main()
