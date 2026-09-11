extends Node

var failures: Array[String] = []
var _exit_code := 0
var _original_last_run := ""

func _ready() -> void:
	call_deferred("_run")

func _run() -> void:
	_original_last_run = Preferences.last_run_id
	check(await GameSession.connect_engine(), "connect to HeroScript and resolve a content revision")
	if not failures.is_empty():
		finish()
		return
	var seed := int(Time.get_unix_time_from_system()) % 90000000 + 1000000
	check(await GameSession.start_campaign(seed), "start showcase campaign")
	check(str(GameSession.run.get("modeId", "")) == "spire_showcase", "pin showcase mode")
	check(GameSession.command("START_ENCOUNTER").has("validPayload"), "discover configured encounter payload")
	var start := GameSession.command("START_ENCOUNTER")
	if not start.is_empty():
		check(await GameSession.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "start configured encounter")
	check(not GameSession.combat.is_empty(), "receive combat projection")
	check(GameSession.run.get("deck", {}).get("handInstanceIds", []).size() == 5, "receive deterministic opening hand")
	check(not GameSession.legal_actions.is_empty(), "receive legal-action previews")
	var card_candidate := first_card_candidate()
	if not card_candidate.is_empty():
		check(await GameSession.submit_candidate(card_candidate), "play legal card")
	else:
		check(false, "find a playable card")
	var timeline := await GameSession.timeline()
	check(timeline.ok and timeline.data.get("items", []).size() >= 2, "read command timeline")
	var origin_run_id := str(GameSession.run.get("runId", ""))
	if timeline.ok and not timeline.data.get("items", []).is_empty():
		var first_sequence := int(timeline.data.items[0].runSequence)
		var branch := await GameSession.create_branch(first_sequence, "smoke-%s" % seed)
		check(branch.ok, "create an immutable timeline branch")
		var tree := await GameSession.branch_tree()
		check(tree.ok, "read the branch tree")
		await GameSession.continue_run(origin_run_id)
	var simulation := await GameSession.simulate_end_turn()
	check(simulation.ok, "simulate commands without mutating the run")
	var verification := await GameSession.verify()
	check(verification.ok and bool(verification.data.get("isValid", verification.data.get("valid", false))), "verify semantic replay")
	var presentation_file := FileAccess.open("res://data/presentation.json", FileAccess.READ)
	var presentation = JSON.parse_string(presentation_file.get_as_text())
	presentation_file.close()
	for mode in presentation.sandbox_modes:
		check(await GameSession.start_sandbox(str(mode.id), presentation.default_scenario, seed + failures.size() + str(mode.id).length()),
			"compile sandbox mode %s" % mode.id)
		check(str(GameSession.run.get("modeId", "")) == str(mode.id), "activate sandbox mode %s" % mode.id)
		check(not GameSession.combat.is_empty(), "materialize combat for %s" % mode.id)
	var cards := await GameSession.content("cards")
	check(cards.ok and cards.data.get("items", []).size() >= 4, "browse published JSON content")
	check(await drive_complete_campaign(seed + 917), "complete all seven campaign activities")
	check(GameSession.run.get("relics", []).size() == 1, "persist and activate relic state")
	check(GameSession.run.get("cardSelections", []).size() == 1, "persist card reward state")
	check(GameSession.run.get("shops", []).size() == 1, "persist shop state")
	check(GameSession.run.get("preparations", []).size() == 1, "persist preparation state")
	check(GameSession.run.get("completedActivityNodeIds", []).has("forge"), "persist card upgrade activity")
	print("BENCHMARK_RUN_ID=", GameSession.run.get("runId", ""))
	var commands: Array = GameServices.diagnostics().filter(func(item): return int(item.method) == HTTPClient.METHOD_POST and str(item.path).ends_with("/commands"))
	var durations: Array = commands.map(func(item): return int(item.ms))
	durations.sort()
	if not durations.is_empty():
		print("COMMAND_HTTP samples=", durations.size(), " median_ms=", durations[durations.size() / 2],
			" max_ms=", durations[-1])
	finish()

func drive_complete_campaign(seed: int) -> bool:
	if not await GameSession.start_campaign(seed):
		return false
	for _step in 160:
		if str(GameSession.run.get("lifecycle", "Active")).to_lower() != "active":
			return str(GameSession.run.get("lifecycle", "")).to_lower() == "completed"
		if not GameSession.combat.is_empty() and str(GameSession.combat.get("status", "ACTIVE")) == "ACTIVE":
			var damage := damaging_card_candidate()
			if not damage.is_empty():
				if not await GameSession.submit_candidate(damage):
					return false
			else:
				var endings: Array = GameSession.legal_actions.filter(func(item): return str(item.command.get("actionType", "")) == "END_TURN")
				if endings.is_empty() or not await GameSession.submit_candidate(endings[0]):
					return false
			continue
		var command := choose_progression_command()
		if command.is_empty():
			return false
		if not await GameSession.submit_activity(command):
			return false
	return false

func damaging_card_candidate() -> Dictionary:
	for candidate in GameSession.legal_actions:
		if str(candidate.get("source", "")) != "Card":
			continue
		for application in candidate.get("applications", []):
			if str(application.get("resourceId", "")) == "health" and \
				float(application.get("currentValue", 0)) < float(application.get("previousValue", 0)):
				return candidate
	return {}

func choose_progression_command() -> Dictionary:
	var choices := GameSession.activity_choices()
	for type in ["START_ENCOUNTER", "RESOLVE_COMBAT", "ACQUIRE_RELIC", "CREATE_CARD_SELECTION", "PICK_CARD_REWARD",
		"CREATE_SHOP", "CREATE_PREPARATION", "APPLY_PREPARATION_OPTION", "UPGRADE_CARD",
		"RESOLVE_NODE", "ADVANCE_NODE"]:
		for choice in choices:
			if str(choice.type) != type:
				continue
			# Test strategy only; the application exposes every advertised upgrade.
			if type == "UPGRADE_CARD" and str(choice.subjectId) != "basic_attack":
				continue
			return choice
	return {}

func first_card_candidate() -> Dictionary:
	for candidate in GameSession.legal_actions:
		if str(candidate.get("source", "")) == "Card":
			return candidate
	return {}

func check(condition: bool, label: String) -> void:
	if condition:
		print("[PASS] ", label)
	else:
		failures.append(label)
		push_error("[FAIL] " + label)

func finish() -> void:
	Preferences.last_run_id = _original_last_run
	Preferences.save()
	print("SHOWCASE_SMOKE failures=", failures.size())
	_exit_code = 0 if failures.is_empty() else 1
	GameAudio.shutdown()
	# The dummy audio mixer runs independently from the accelerated headless
	# frame loop. Give it one real cycle to release the looping WAV playback.
	get_tree().create_timer(0.1, true, false, true).timeout.connect(_after_audio_cleanup, CONNECT_ONE_SHOT)

func _after_audio_cleanup() -> void:
	# The timer itself is released at the end of this frame.
	get_tree().process_frame.connect(_finish_after_cleanup, CONNECT_ONE_SHOT)

func _finish_after_cleanup() -> void:
	get_tree().quit(_exit_code)
