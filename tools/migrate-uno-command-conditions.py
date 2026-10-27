"""Move the reference duel's existing pass-button rule into its JSON policy.
Content-specific authoring tool; no UNO semantics are introduced in Core.
"""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'data/configs/uno-clash/Resources'
rules_path = root / 'combat-rules/uno.json'
formulas_path = root / 'formulas/uno.json'
rules = json.loads(rules_path.read_text(encoding='utf-8-sig'))
formulas = json.loads(formulas_path.read_text(encoding='utf-8-sig'))
formula = {'operations': [
    {'op': 'SET', 'value': 'params.duelAvailable'}, {'op': 'MIN', 'value': '1'}, {'op': 'MULTIPLY', 'value': '-1'}, {'op': 'ADD', 'value': '1'},
    {'op': 'ADD', 'value': 'params.duelHasCard'}, {'op': 'MULTIPLY', 'value': '2'},
    {'op': 'SUBTRACT', 'value': 'params.source.resources.uno_draw_available.current'}, {'op': 'ADD', 'value': '1'},
    {'op': 'ADD', 'value': 'params.source.resources.uno_draw2_pending.current'},
    {'op': 'ADD', 'value': 'params.source.resources.uno_setup.current'},
    {'op': 'MULTIPLY', 'value': 'params.source.resources.uno_energy.maximum'},
    {'op': 'SUBTRACT', 'value': 'params.source.resources.uno_energy.current'}, {'op': 'ADD', 'value': '1'}]}
formula['params'] = {item['value'][7:]: 0 for item in formula['operations'] if str(item.get('value', '')).startswith('params.')}
formulas.setdefault('uno_server_can_end', formula)
for definition in rules.values():
    flow = definition['flow']
    flow.setdefault('activationSequence', {'strategy': 'PerRound'})
    conditions = flow.setdefault('commandConditions', [])
    additions = [
        {'ruleId': 'duel.require-draw', 'commands': ['END_TURN', 'PASS'], 'actorScope': 'All',
         'inputs': [{'variable': 'duelAvailable', 'kind': 'SumZoneCounts', 'zones': [{'zoneId': zone, 'ownerScope': 'RunOwner'} for zone in ['draw', 'discard']]},
                    {'variable': 'duelHasCard', 'kind': 'HasBaseLegalCard'}], 'allowCondition': 'uno_server_can_end',
         'failureCode': 'DRAW_REQUIRED', 'messageKey': 'uno.draw_required'},
        {'ruleId': 'duel.choose-color', 'commands': ['END_TURN', 'PASS'], 'actorScope': 'All', 'inputs': [],
         'allowCondition': '1 - source.resources.wild_color_token.current', 'failureCode': 'COLOR_REQUIRED', 'messageKey': 'uno.color_required'}]
    for addition in additions:
        if not any(item['ruleId'] == addition['ruleId'] for item in conditions):
            conditions.append(addition)
rules_path.write_text(json.dumps(rules, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
formulas_path.write_text(json.dumps(formulas, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('Duel source conditions are explicit; existing definitions and policies were preserved.')
