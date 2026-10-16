extends SceneTree
var failures := 0
class Transport extends RefCounted:
	var requests: Array = []
	func request(method, path, body = null, _timeout = 0) -> Dictionary:
		requests.append({"method": method, "path": path, "body": body}); return {"ok": true, "data": {}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var transport := Transport.new()
	var gateway = load("res://scripts/engine/engine_gateway.gd").new(transport)
	var numbers = load("res://scripts/engine/request_numbers.gd")
	for entry in [["REMOVE_CARD_TRANSFORMATION", "transformationId"], ["REPLACE_CARD_TRANSFORMATION", "transformationId"], ["RESTORE_HEAD_FROM_HISTORY", "sourceSequence"], ["INVOKE_CARD_ZONE_GAMEPLAY_FLOW", "requestedCount"], ["APPLY_RUN_RESOURCE", "operation"], ["PLAY_CARD", "actionType"]]:
		var input: Dictionary = JSON.parse_string(JSON.stringify({entry[1]: 12, "value": 7.25, "metadata": {entry[1]: 2.0}}))
		var before := JSON.stringify(input)
		var command: Dictionary = gateway.prepare_command("runs", "run", entry[0], input, 2, 3)
		check(typeof(command.envelope.payload[entry[1]]) == TYPE_INT and JSON.stringify(command.envelope.payload).contains('"%s":12' % entry[1]), "typed integer payload token: " + entry[0])
		check(typeof(command.envelope.payload.value) == TYPE_FLOAT and command.envelope.payload.value == 7.25 and typeof(command.envelope.payload.metadata[entry[1]]) == TYPE_FLOAT and JSON.stringify(input) == before, "only declared field paths change; input and gameplay decimals stay intact")
		await gateway.send_prepared(command); await gateway.send_prepared(command)
		check(JSON.stringify(transport.requests[-1].body) == JSON.stringify(transport.requests[-2].body), "retry retains the exact normalized envelope and command identity")
	var fraction := {"transformationId": 2.5}
	check(numbers.payload("REMOVE_CARD_TRANSFORMATION", fraction).transformationId == 2.5, "fractional integer fields are not silently truncated")
	check(numbers.payload(" remove_card_transformation ", {"TransformationId": 2.0}).TransformationId is int, "accepted command and property casing use the same numeric contract")
	check(numbers.integer_token(9007199254740992.0) is float and numbers.integer_token(INF) == INF and numbers.integer_token("12") == "12" and numbers.integer_token(null) == null, "unsafe precision, nonfinite values, strings and null are not coerced")
	check(numbers.integer_token(9007199254740991.0) is int and numbers.integer_token(9223372036854775807) == 9223372036854775807, "safe float boundary and already-exact integer inputs retain their values")
	var scenario: Dictionary = JSON.parse_string('{"schemaVersion":2,"initialState":{"effects":[{"statusId":"burn","stacks":3}],"resourcesByActor":{"hero":{"health":72.5,"stacks":1,"priority":2}}},"influences":[{"priority":4,"value":0.125}],"participants":[{"controllerBinding":{"kind":1}}]}')
	var original := JSON.stringify(scenario)
	await gateway.create_sandbox("sandbox", scenario, 42, "revision", "setting")
	var body: Dictionary = transport.requests[-1].body
	check(body.schemaVersion is int and body.seed is int and body.initialState.effects[0].stacks is int and body.influences[0].priority is int and body.participants[0].controllerBinding.kind is int, "sandbox JSON editor normalizes nested stack counts, priorities and numeric enum tokens")
	check(body.initialState.resourcesByActor.hero.health == 72.5 and body.initialState.resourcesByActor.hero.stacks is float and body.initialState.resourcesByActor.hero.priority is float and body.influences[0].value == 0.125 and JSON.stringify(scenario) == original, "resource IDs named stacks or priority are not mistaken for integer schema fields")
	var commands: Array = JSON.parse_string('[{"type":"REMOVE_CARD_TRANSFORMATION","payload":{"transformationId":17}},{"type":"APPLY_RUN_RESOURCE","payload":{"value":1.75,"operation":0}},{"type":"CUSTOM_MOD","payload":{"transformationId":4}}]')
	var commands_before := JSON.stringify(commands)
	await gateway.simulate("run", 5, commands)
	body = transport.requests[-1].body
	check(body.sourceSequence is int and body.commands[0].payload.transformationId is int and body.commands[1].payload.operation is int and body.commands[1].payload.value == 1.75, "simulation shares canonical command normalization while preserving fractional resources")
	check(body.commands[2].payload.transformationId is float and JSON.stringify(commands) == commands_before, "unknown mod commands and original simulation input are not rewritten")
	await gateway.transformation_preview("run", "card", "Remove", "", 17.0)
	check(transport.requests[-1].path.ends_with("transformationId=17"), "transformation query uses an integer token instead of 17.0")
	await gateway.read_session("run", 3); await gateway.run_timeline("run", 3, 25, 12)
	check(transport.requests[-2].path.ends_with("expectedSequence=3") and transport.requests[-1].path.ends_with("afterSequence=3&limit=25&throughSequence=12"), "typed session and timeline query parameters already preserve integer syntax")
	await gateway.preview_loadout({"runId": "run", "actorId": "hero", "runSequence": 2.0, "expectedStep": 3.0}, {})
	check(transport.requests[-1].body.expectedSequence is int and transport.requests[-1].body.expectedStep is int, "loadout shares the safe integer-token policy")
	await gateway.preview_loadout({"runId": "run", "actorId": "hero", "runSequence": 2.5, "expectedStep": 3.5}, {})
	check(transport.requests[-1].body.expectedSequence == 2.5 and transport.requests[-1].body.expectedStep == 3.5, "loadout no longer truncates malformed version numbers into valid commands")
	quit(1 if failures else 0)
