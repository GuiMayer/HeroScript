"""Build the opt-in four-seat table reference package. All gameplay is JSON."""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'data/configs/circular-table'
content = {}
colors = ['red', 'blue', 'green', 'yellow']
symbols = [str(i) for i in range(10)] + ['skip', 'reverse', 'draw2', 'wild']
flags = ['t_color_' + c for c in colors] + ['t_symbol_' + s for s in symbols]


def put(kind, definitions):
    path = OUT / 'Resources' / kind / 'table.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(definitions, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    content[kind] = {'schemaVersion': 1, 'operation': 'definition', 'kind': kind,
                     'sourcePath': f'Resources/{kind}/table.json', 'artifactPath': f'{kind}/table.json'}


def resource(resource_id, maximum=1):
    return {'resourceId': resource_id, 'displayName': resource_id, 'shortName': resource_id,
            'category': 'TACTICAL', 'defaultMin': 0, 'defaultMax': maximum, 'defaultCurrent': 0,
            'canBeNegative': False, 'canExceedMax': False, 'regeneration': {'enabled': False, 'amountPerTurn': 0, 'timing': 'START_TURN'}}


def mutation(resource_id, value, target='SELF', operation='SET'):
    effect = {'effectId': f'{resource_id}.{target}', 'type': 'MODIFY_RESOURCE', 'target': target,
              'targetResource': resource_id, 'operation': operation, 'tags': ['table']}
    effect['formulaValue' if isinstance(value, str) else 'flatValue'] = value
    if resource_id in flags and isinstance(value, int) and value in (0, 1):
        effect['condition'] = f'1 - target.resources.{resource_id}.current' if value == 1 else f'target.resources.{resource_id}.current'
    return effect


def broadcast(resource_id, value):
    return [mutation(resource_id, value), mutation(resource_id, value, 'ALL_ENEMIES')]


def draw(target='SELF', count=1):
    effect = {'effectId': 'table.draw', 'type': 'CARD_ZONE_FLOW', 'target': target, 'cardZoneFlowId': 'table.draw',
              'cardCount': 1, 'condition': 'availableCards', 'tags': ['table']}
    if isinstance(count, str):
        effect['parameters'] = [{'parameter': 'CardCount', 'formulaValue': count, 'channel': 'effect_amount',
                                 'unitId': 'scalar', 'conversion': {'requireInteger': True}}]
    else:
        effect['cardCount'] = count
    return effect


def query(variable, kind, **extra):
    return {'variable': variable, 'kind': kind, **extra}


available = query('availableCards', 'SumZoneCounts', zones=[{'zoneId': z, 'ownerScope': 'RunOwner'} for z in ['draw', 'discard']])
hand = query('myHand', 'ZoneCount', zones=[{'zoneId': 'hand', 'ownerScope': 'Actor', 'actor': 'Source'}])
draws = query('drawAttempts', 'ActivationCommandCount', actionId='t_draw')


def rule(rule_id, commands, condition, inputs=(), action_id=None):
    result = {'ruleId': rule_id, 'commands': commands, 'actorScope': 'All', 'inputs': list(inputs),
              'allowCondition': condition, 'failureCode': rule_id.upper().replace('.', '_'), 'messageKey': rule_id}
    if action_id:
        result['actionId'] = action_id
    return result


resources = {key: resource(key) for key in flags + ['t_energy', 't_choice', 't_called']}
resources['t_debt'] = resource('t_debt', 108)
put('resources', resources)
formulas = {
    't_no_debt': {'operations': [{'op': 'SET', 'value': 'params.source.resources.t_debt.current'}, {'op': 'MIN', 'value': '1'}, {'op': 'MULTIPLY', 'value': '-1'}, {'op': 'ADD', 'value': '1'}]},
    't_empty': {'operations': [{'op': 'SET', 'value': 'params.myHand'}, {'op': 'MIN', 'value': '1'}, {'op': 'MULTIPLY', 'value': '-1'}, {'op': 'ADD', 'value': '1'}, {'op': 'SUBTRACT', 'value': 'params.source.resources.t_choice.current'}]},
    't_can_end': {'operations': [{'op': 'SET', 'value': 'params.availableCards'}, {'op': 'MIN', 'value': '1'}, {'op': 'MULTIPLY', 'value': '-1'}, {'op': 'ADD', 'value': '1'},
                               {'op': 'ADD', 'value': 'params.drawAttempts'}, {'op': 'ADD', 'value': 'params.hasCard'}, {'op': 'SUBTRACT', 'value': 'params.source.resources.t_energy.current'}, {'op': 'ADD', 'value': '1'}]},
    't_one_card': {'operations': [{'op': 'SET', 'value': 'params.myHand'}, {'op': 'SUBTRACT', 'value': '1'}, {'op': 'ABS'}, {'op': 'MULTIPLY', 'value': '-1'}, {'op': 'ADD', 'value': '1'}, {'op': 'MAX', 'value': '0'}, {'op': 'SUBTRACT', 'value': 'params.source.resources.t_called.current'}]},
    't_catchable': {'operations': [{'op': 'SET', 'value': 'params.targetHand'}, {'op': 'SUBTRACT', 'value': '1'}, {'op': 'ABS'}, {'op': 'MULTIPLY', 'value': '-1'}, {'op': 'ADD', 'value': '1'}, {'op': 'MAX', 'value': '0'}, {'op': 'SUBTRACT', 'value': 'params.target.resources.t_called.current'}]}
}
# Declare all named inputs explicitly for the shared math compiler.
for definition in formulas.values():
    definition['params'] = {op['value'][7:]: 0 for op in definition['operations'] if str(op.get('value', '')).startswith('params.')}
put('formulas', formulas)
cards, deck = {}, []
for color in colors + ['wild']:
    for symbol in (symbols[:-1] if color != 'wild' else ['wild', 'draw4']):
        card_id = f't_{color}_{symbol}'
        effects = []
        for flag in flags:
            effects += broadcast(flag, int(flag == 't_color_' + color or flag == 't_symbol_' + (symbol if color != 'wild' else 'wild')))
        effects += [mutation('t_called', 0)]
        if symbol == 'skip':
            effects += [{'effectId': 'skip', 'type': 'TURN_SEQUENCE', 'target': 'SELF', 'sequenceMutation': {'operation': 'AddAdvance', 'amount': 1}}]
        if symbol == 'reverse':
            effects += [{'effectId': 'reverse', 'type': 'TURN_SEQUENCE', 'target': 'SELF', 'sequenceMutation': {'operation': 'ReverseDirection'}}]
        if symbol == 'draw2':
            effects += [mutation('t_debt', 'source.resources.t_debt.current + 2', 'NEXT_PARTICIPANT'), mutation('t_debt', 0)]
        if color == 'wild':
            effects += [mutation('t_choice', 1)]
        if symbol == 'draw4':
            effects += [draw('NEXT_PARTICIPANT', 4), {'effectId': 'skip-after-four', 'type': 'TURN_SEQUENCE', 'target': 'SELF', 'sequenceMutation': {'operation': 'AddAdvance', 'amount': 1}}]
        conditions = [('choice', '1 - source.resources.t_choice.current')]
        if color != 'wild':
            match = f'source.resources.t_color_{color}.current + source.resources.t_symbol_{symbol}.current'
            if symbol == 'draw2':
                match += ' + source.resources.t_debt.current'
            conditions += [('match', match)]
        if symbol != 'draw2':
            conditions += [('debt', 't_no_debt')]
        components = [{'type': 'cost', 'componentId': 'cost', 'order': 10, 'costs': {'costs': [{'resourceId': 't_energy', 'amount': 1}]}}]
        components += [{'type': 'condition', 'componentId': key, 'order': 20 + i, 'expression': expression, 'failureReason': key} for i, (key, expression) in enumerate(conditions)]
        components += [{'type': 'effect', 'componentId': f'effect.{i}', 'order': 30 + i, 'effect': effect} for i, effect in enumerate(effects)]
        components += [{'type': 'targeting', 'componentId': 'targeting', 'order': 100, 'target': 'SELF', 'minimumTargets': 1, 'maximumTargets': 1},
                       {'type': 'disposition', 'componentId': 'disposition', 'order': 110, 'cardZoneResolutionFlowId': 'table.played'}]
        cards[card_id] = {'cardId': card_id, 'rarity': 'Common', 'tags': list(dict.fromkeys(['table', color, symbol])), 'components': components}
        deck += [card_id] * (4 if color == 'wild' else 1 if symbol == '0' else 2)
put('cards', cards)
actions = {'t_draw': {'actionId': 't_draw', 'displayName': 'Draw one', 'actionType': 'POWER', 'costs': {'costs': []}, 'effects': [draw(), mutation('t_called', 0)], 'requiresTarget': False},
           't_accept': {'actionId': 't_accept', 'displayName': 'Accept accumulated draw', 'actionType': 'POWER', 'costs': {'costs': []},
                        'effects': [draw(count='source.resources.t_debt.current'), mutation('t_debt', 0), mutation('t_energy', 0), mutation('t_called', 0)], 'requiresTarget': False},
           't_call': {'actionId': 't_call', 'displayName': 'Declare one card', 'actionType': 'POWER', 'costs': {'costs': []}, 'effects': [mutation('t_called', 1)], 'requiresTarget': False},
           't_catch': {'actionId': 't_catch', 'displayName': 'Catch undeclared hand', 'actionType': 'POWER', 'costs': {'costs': []}, 'effects': [draw('TARGET', 2)], 'requiresTarget': True}}
for color in colors:
    actions['t_choose_' + color] = {'actionId': 't_choose_' + color, 'displayName': 'Choose ' + color, 'actionType': 'POWER', 'costs': {'costs': []},
                                  'requiresTarget': False, 'effects': sum([broadcast('t_color_' + c, int(c == color)) for c in colors], []) + [mutation('t_choice', 0)]}
put('actions', actions)
zones = [{'zoneId': z, 'ownerScope': 'Actor' if z == 'hand' else 'RunOwner', 'ordering': 'Ordered', 'allowsCardPlay': z == 'hand',
          'visibility': {'contents': 'Owner' if z == 'hand' else 'None' if z == 'draw' else 'All', 'order': 'Hidden' if z == 'draw' else 'Visible'}} for z in ['draw', 'hand', 'discard', 'table']]
zones[-1]['capacity'] = 1
move = lambda src, dst, selection, **extra: {'stepId': f'{src}-to-{dst}', 'operation': 'Move', 'sourceZoneId': src, 'targetZoneId': dst,
                                          'sourceOwner': 'RunOwner', 'targetOwner': 'RunOwner', 'selection': selection, **extra}
flows = [
    {'flowId': 'table.setup', 'triggers': ['encounter.started'], 'allowedInvocations': ['Boundary'], 'steps': [
        move('draw', 'table', {'strategy': 'ByDefinition', 'definitionIds': ['t_red_0'], 'count': 1}),
        {'stepId': 'shuffle', 'operation': 'Shuffle', 'sourceZoneId': 'draw', 'sourceOwner': 'RunOwner'},
        {'stepId': 'deal-all', 'operation': 'ForEachParticipant', 'nestedFlowId': 'table.deal-one', 'participants': {'order': 'Ordinal', 'distribution': 'RoundRobin', 'repetitions': 7}}]},
    {'flowId': 'table.deal-one', 'allowedInvocations': ['Boundary'], 'steps': [move('draw', 'hand', {'strategy': 'First', 'count': 1}, targetOwner='IterationActor')]},
    {'flowId': 'table.recycle', 'allowedInvocations': ['Effect'], 'steps': [move('discard', 'draw', {'strategy': 'All'}, insertion={'strategy': 'ShuffleAfterInsert'})]},
    {'flowId': 'table.draw', 'allowedInvocations': ['Effect'], 'steps': [move('draw', 'hand', {'strategy': 'Top', 'countFormula': 'requestedCount'}, targetOwner='TargetActor',
        onInsufficient='ExecuteFallbackAndRetry', fallbackFlowId='table.recycle', retryAfterFallback=True, moveAvailableBeforeFallback=True)]},
    {'flowId': 'table.played', 'allowedInvocations': ['CardResolution'], 'steps': [move('table', 'discard', {'strategy': 'All'}), move('hand', 'table', {'strategy': 'Explicit'}, sourceOwner='ActiveActor')]}
]
put('card-zone-systems', {'table_zones': {'cardZoneSystemId': 'table_zones', 'zones': zones, 'flows': flows}})
conditions = [rule('draw.required', ['END_TURN', 'PASS'], 't_can_end', [available, draws, query('hasCard', 'HasBaseLegalCard')]),
              rule('choice.required', ['END_TURN', 'PASS'], '1 - source.resources.t_choice.current'), rule('debt.required', ['END_TURN', 'PASS'], 't_no_debt'),
              rule('draw.once', ['POWER'], '1 - drawAttempts', [draws], 't_draw'), rule('draw.available', ['POWER'], 'availableCards', [available], 't_draw'),
              rule('draw.budget', ['POWER'], 'source.resources.t_energy.current', (), 't_draw'), rule('draw.no-debt', ['POWER'], 't_no_debt', (), 't_draw'),
              rule('draw.no-choice', ['POWER'], '1 - source.resources.t_choice.current', (), 't_draw'),
              rule('accept.debt', ['POWER'], 'source.resources.t_debt.current', (), 't_accept'),
              rule('declare.one', ['POWER'], 't_one_card', [hand], 't_call'),
              rule('catch.one', ['POWER'], 't_catchable', [query('targetHand', 'ZoneCount', zones=[{'zoneId': 'hand', 'ownerScope': 'Actor', 'actor': 'Target'}])], 't_catch')]
conditions += [rule('choose.' + c, ['POWER'], 'source.resources.t_choice.current', (), 't_choose_' + c) for c in colors]
base = json.loads((ROOT / 'data/configs/default/Resources/combat-rules/standard_combat.json').read_text())['standard_combat']
base['combatRulesId'] = 'table_combat'
flow = base['flow']
flow['activationSequence'] = {'strategy': 'Circular', 'circular': {'initialDirection': 'Forward', 'firstActor': 'RunOwner', 'roundPolicy': 'Disabled', 'allowSelfSuccessor': True}}
flow['queries'] = [hand, available]
flow['commandConditions'] = conditions
flow['resourceCycle'] = {'resourceId': 't_energy', 'actorScope': 'All', 'startActivation': 'Set', 'amount': 1}
flow['actionBudget'].update(resourceId='t_energy', actorScope='All', consumingCommands=['PLAY_CARD'])
flow['statusTiming']['boundaries'] = ['StartActivation', 'EndActivation']
flow['outcome']['participantVictory'] = {'ruleId': 'empty-hand', 'inputs': [hand], 'condition': 't_empty', 'tieBreak': 'ActiveActorThenOrdinal'}
flow['ai']['autoEndAfterAction'] = False
gambits = {}
for i, action_id in enumerate(['t_choose_red', 't_choose_blue', 't_choose_green', 't_choose_yellow', 't_accept', 't_call', 't_catch']):
    gambits['g_' + action_id] = {'gambitId': 'g_' + action_id, 'priority': 1000 - i, 'predicates': [{'expression': '1'}], 'action': {'actionType': 'POWER', 'actionId': action_id}}
gambits['g_play'] = {'gambitId': 'g_play', 'priority': 500, 'predicates': [{'expression': '1'}], 'action': {'actionType': 'PLAY_CARD'}}
gambits['g_draw'] = {'gambitId': 'g_draw', 'priority': 100, 'predicates': [{'expression': '1'}], 'action': {'actionType': 'POWER', 'actionId': 't_draw'}}
gambits['g_end'] = {'gambitId': 'g_end', 'priority': 0, 'predicates': [{'expression': '1'}], 'action': {'actionType': 'END_TURN'}}
flow['ai']['decisionIds'] = list(gambits)
put('gambits', gambits)
put('combat-rules', {'table_combat': base})
entity = {'definitionId': 'table_actor', 'displayName': 'Table player', 'components': [
    {'type': 'resources', 'componentId': 'resources', 'pools': {key: {'current': int(key in ['t_color_red', 't_symbol_0', 't_energy']), 'max': value['defaultMax']} for key, value in resources.items()}},
    {'type': 'abilities', 'componentId': 'abilities', 'abilityIds': list(actions)}]}
put('entities', {'table_actor': entity})
participants = [{'instanceId': f'seat_{i}', 'definitionId': 'table_actor', 'sideId': f'side_{i}',
                 'controllerBinding': {'kind': 'Player'} if i == 0 else {'kind': 'AI', 'policyId': 'gambit'}} for i in range(4)]
put('runs', {'table_run': {'runId': 'table_run', 'startingResources': {'gold': 0}, 'initialPlayableCardCount': 0, 'initialCardZoneId': 'draw', 'initialCardOwner': 'RunOwner',
                         'startingCards': deck, 'mapNodes': [{'nodeId': 'table', 'activity': {'type': 'Encounter', 'parameters': {'participants': participants}},
                                                           'completionPolicy': 'Required', 'entryEffects': [], 'exitEffects': [], 'nextNodeIds': []}]}})
put('card-pools', {'table_cards': {'poolId': 'table_cards', 'explicitCardIds': list(cards)}})
put('enemy-pools', {'table_actors': {'enemyPoolId': 'table_actors', 'entityDefinitionIds': ['table_actor']}})
mode = {'modeId': 'table_mode', 'runDefinitionId': 'table_run', 'allowCustomSeed': True, 'flowRulesId': 'standard_flow', 'combatRulesId': 'table_combat',
        'calculationPipelineIds': ['default_effect_amount', 'signed_resource_delta'], 'cardPoolIds': ['table_cards'], 'enemyPoolIds': ['table_actors'],
        'replayPolicyId': 'sandbox_replay', 'timelinePolicyId': 'sandbox_timeline', 'contentBindingPolicyId': 'pinned_published',
        'capabilityPolicyId': 'sandbox_tools', 'progressionPolicyId': 'sandbox_progression', 'cardZoneSystemId': 'table_zones'}
put('modes', {'table_mode': mode})
(OUT / 'settings').mkdir(exist_ok=True)
(OUT / 'settings/circular-table.json').write_text(json.dumps({'schemaVersion': 1, 'settingId': 'circular-table', 'displayName': 'Four Seat Table',
    'description': 'Opt-in circular card table; existing duel and combat modes are preserved.', 'launch': {'runDefinitionId': 'table_run', 'playerEntityId': 'seat_0', 'modeId': 'table_mode'},
    'packages': [{'packageId': 'heroscript.circular-table', 'versionRange': '1.0.0'}]}, indent=2) + '\n')
(OUT / 'package.json').write_text(json.dumps({'schemaVersion': 1, 'packageId': 'heroscript.circular-table', 'version': '1.0.0', 'dataOnly': True,
    'dependencies': [{'packageId': 'heroscript.base', 'versionRange': '1.0.0'}], 'content': list(content.values())}, indent=2) + '\n')
assert len(deck) == 108
print(f'Wrote {len(content)} definition files, {len(cards)} card definitions, {len(deck)} physical cards.')
