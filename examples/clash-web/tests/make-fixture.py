"""Create isolated JSON-only integration fixtures; never edit shipped game or engine files."""
import json
import shutil
import sys
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
mode = sys.argv[1]
target = Path(sys.argv[2]).resolve()
assert target.is_relative_to(repo / '.runtime') and not target.exists()
target.mkdir(parents=True)
for package in ['default', 'clash', 'circular-table']:
    shutil.copytree(repo / 'content' / package, target / package)
four_modes = ['recycle-four', 'stack-four', 'effects-four', 'effects-four-draw2']
resources = target / ('circular-table' if mode in four_modes else 'clash') / 'Resources'
filename = 'table.json' if mode in four_modes else 'clash.json'

def load(kind):
    return json.loads((resources / kind / filename).read_text(encoding='utf-8-sig'))

def save(kind, value):
    (resources / kind / filename).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

if mode in ['stack-four', 'effects-four', 'effects-four-draw2']:
    # Deterministic physical hands; prefer a legal response to acceptance in this fixture.
    hands = [
        ['t_red_draw2', 't_green_draw2', 't_red_1', 't_green_1', 't_blue_1', 't_yellow_1', 't_red_2'],
        ['t_blue_draw2'] + ['t_blue_' + str(n) for n in range(2, 8)],
        ['t_yellow_draw2'] + ['t_yellow_' + str(n) for n in range(2, 8)],
        ['t_red_draw2'] + ['t_green_' + str(n) for n in range(2, 8)]
    ]
    if mode.startswith('effects-four'):
        hands = [
            ['t_red_' + str(n) for n in range(1, 8)],
            ['t_red_skip'] + ['t_blue_' + str(n) for n in range(1, 7)],
            ['t_blue_draw2' if mode.endswith('draw2') else 't_blue_reverse'] + ['t_green_' + str(n) for n in range(1, 7)],
            ['t_wild_draw4'] + ['t_yellow_' + str(n) for n in range(1, 7)]
        ]
    front = ['t_red_0'] + [hand[index] for index in range(7) for hand in hands]
    run = load('runs')
    rest = run['table_run']['startingCards'][:]
    for definition in front:
        rest.remove(definition)
    run['table_run']['startingCards'] = front + rest
    save('runs', run)
    zones = load('card-zone-systems')
    for flow in zones['table_zones']['flows']:
        flow['steps'] = [step for step in flow['steps'] if step['operation'] != 'Shuffle']
    save('card-zone-systems', zones)
    gambits = load('gambits')
    if mode == 'stack-four':
        gambits['g_t_accept']['priority'] = 400
    else:
        # Force a visible color change after the +4, without changing card effects.
        gambits['g_t_choose_blue']['priority'] = 1001
        gambits['test_special'] = {
            'gambitId': 'test_special', 'priority': 900,
            'predicates': [{'expression': '1'}],
            'action': {'actionType': 'PLAY_CARD', 'cardDefinitionId': hands[2][0]}
        }
        rules = load('combat-rules')
        rules['table_combat']['flow']['ai']['decisionIds'].append('test_special')
        save('combat-rules', rules)
    save('gambits', gambits)
elif mode == 'recycle-four':
    zones = load('card-zone-systems')
    setup = next(flow for flow in zones['table_zones']['flows'] if flow['flowId'] == 'table.setup')
    setup['steps'].append({
        'stepId': 'test.empty-shared-draw', 'operation': 'Move',
        'sourceZoneId': 'draw', 'targetZoneId': 'discard',
        'sourceOwner': 'RunOwner', 'targetOwner': 'RunOwner',
        'selection': {'strategy': 'All'}, 'onInsufficient': 'AllowPartial'
    })
    save('card-zone-systems', zones)
elif mode in ['stack', 'bot-buy', 'bot-buy-clash', 'bot-extra', 'bot-draw4', 'player-extra']:
    run = load('runs')
    if mode == 'stack':
        front = ['clash_red_0', 'clash_red_draw2', 'clash_green_draw2', 'clash_wild_draw4',
                 'clash_red_reverse', 'clash_red_skip', 'clash_red_7', 'clash_green_2',
                 'clash_red_draw2', 'clash_blue_3', 'clash_blue_4', 'clash_blue_5',
                 'clash_yellow_6', 'clash_green_8', 'clash_green_9']
    else:
        unplayable = ['clash_blue_1', 'clash_blue_2', 'clash_blue_3', 'clash_green_4',
                      'clash_green_5', 'clash_yellow_6', 'clash_yellow_7']
        playable = ['clash_red_' + str(number) for number in range(1, 8)]
        player, bot = (unplayable, playable) if mode == 'player-extra' else (playable, unplayable)
        if mode == 'bot-draw4':
            player = playable
            bot = ['clash_wild_draw4', 'clash_wild_draw4'] + ['clash_blue_' + str(n) for n in range(1, 6)]
            purchases = ['clash_blue_8']
        elif mode == 'bot-buy-clash':
            player = ['clash_red_7']
            purchases = ['clash_yellow_9', 'clash_green_9', 'clash_blue_8']
            actions = load('actions')
            for effect in actions['clash_setup_table']['effects']:
                if effect['effectId'] == 'clash_setup.player_deal':
                    effect['cardCount'] = 1
            save('actions', actions)
            entities = load('entities')
            for key, entity in entities.items():
                pools = next(c['pools'] for c in entity['components'] if c['type'] == 'resources')
                pools['hand_count' if key == 'clash_player' else 'opponent_hand_count']['current'] = 1
            save('entities', entities)
        else:
            purchases = ['clash_yellow_9'] if mode == 'bot-buy' else ['clash_red_reverse', 'clash_yellow_9']
        front = ['clash_red_0'] + player + bot + purchases
    rest = run['clash_run']['startingCards'][:]
    for definition in front:
        rest.remove(definition)
    run['clash_run']['startingCards'] = front + rest
    save('runs', run)
    zones = load('card-zone-systems')
    zones['clash_zones']['flows'] = [flow for flow in zones['clash_zones']['flows'] if flow['flowId'] != 'run.shuffle']
    for flow in zones['clash_zones']['flows']:
        flow['steps'] = [step for step in flow['steps'] if step['operation'] != 'Shuffle']
    save('card-zone-systems', zones)
elif mode == 'recycle':
    zones = load('card-zone-systems')
    zones['clash_zones']['flows'].append({
        'flowId': 'test.empty-draw-into-discard', 'allowedInvocations': ['Effect'], 'steps': [{
            'stepId': 'put-rest-in-discard', 'operation': 'Move', 'sourceZoneId': 'draw', 'sourceOwner': 'RunOwner',
            'targetZoneId': 'discard', 'targetOwner': 'RunOwner', 'selection': {'strategy': 'All'}
        }]})
    save('card-zone-systems', zones)
    actions = load('actions')
    actions['clash_setup_table']['effects'].append({
        'effectId': 'test.empty-draw', 'type': 'CARD_ZONE_FLOW', 'target': 'SELF',
        'cardZoneFlowId': 'test.empty-draw-into-discard', 'cardCount': 1, 'tags': ['clash']})
    save('actions', actions)
elif mode == 'empty':
    actions = load('actions')
    for effect in actions['clash_setup_table']['effects']:
        if effect['effectId'] == 'clash_setup.player_deal':
            effect['cardCount'] = 100
    save('actions', actions)
    entities = load('entities')
    for key, entity in entities.items():
        pools = next(component['pools'] for component in entity['components'] if component['type'] == 'resources')
        if key == 'clash_player':
            pools['hand_count']['current'] = 100
            pools['clash_draw2_pending']['current'] = 8
            pools['clash_draw2_due']['current'] = 1
        else:
            pools['opponent_hand_count']['current'] = 100
    save('entities', entities)
else:
    raise ValueError('Use stack, recycle, empty, bot-buy, bot-buy-clash, bot-extra or player-extra')
print(target)
