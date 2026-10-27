import json
from itertools import product
from pathlib import Path
import unittest


class DuelCommandMigrationTests(unittest.TestCase):
    def test_authored_formula_preserves_the_reference_duel_draw_rule(self):
        path = Path(__file__).parents[2] / 'data/configs/uno-clash/Resources/formulas/uno.json'
        operations = json.loads(path.read_text(encoding='utf-8-sig'))['uno_server_can_end']['operations']
        for energy, draw_available, debt, available, has_card, setup in product([0, 1, 2, 10], [0, 1], [0, 2], [0, 1, 79], [0, 1], [0, 1]):
            values = {'duelAvailable': available, 'duelHasCard': has_card, 'source.resources.uno_energy.current': energy,
                      'source.resources.uno_energy.maximum': 10, 'source.resources.uno_draw_available.current': draw_available,
                      'source.resources.uno_draw2_pending.current': debt, 'source.resources.uno_setup.current': setup}
            result = 0
            for operation in operations:
                token = operation['value']
                value = values[token[7:]] if token.startswith('params.') else float(token)
                result = {'SET': lambda: value, 'ADD': lambda: result + value, 'SUBTRACT': lambda: result - value,
                          'MULTIPLY': lambda: result * value, 'MIN': lambda: min(result, value)}[operation['op']]()
            must_draw = debt == 0 and energy > 0 and draw_available > 0 and not has_card and available > 0
            self.assertEqual(bool(setup) or not must_draw, result > 0, values)


if __name__ == '__main__':
    unittest.main()
