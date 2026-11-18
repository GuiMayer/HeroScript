extends SceneTree
## Runs against an isolated API via --api-url.

var failures := 0

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, description: String) -> bool:
	print("[PASS] " if ok else "[FAIL] ", description)
	if not ok: failures += 1
	return ok

func _submit(session, type: String) -> bool:
	var options: Array = session.activity_choices().filter(func(choice): return choice.type == type)
	return check(not options.is_empty() and await session.submit_activity(options[0]), type)

func _opening_hand(session) -> Array:
	var zones = preload("res://scripts/presentation/card_zone_presenter.gd").new(session.card_zones, root.get_node("I18n"))
	return zones.playable_cards().map(func(card): return str(card.cardInstanceId))

func _exercise(session) -> void:
	if not check(await session.connect_engine("alisyum"), "connect to published Alisyum"): return
	if not check(await session.start_campaign(int(Time.get_unix_time_from_system())), "open Piromancer campaign"): return
	for type in ["START_DIALOGUE", "CHOOSE_DIALOGUE_OPTION", "RESOLVE_NODE", "ADVANCE_NODE",
			"CREATE_PREPARATION", "APPLY_PREPARATION_OPTION", "RESOLVE_NODE", "ADVANCE_NODE", "START_ENCOUNTER"]:
		if not await _submit(session, type): return
	var cards := _opening_hand(session)
	check(cards.size() == 5, "first encounter has five cards before any end-turn command")
	check(session.legal_actions.any(func(action): return action.get("command", {}).get("cardInstanceId", "") in cards),
		"the opening hand contains a legal action")
	var encounter := str(session.combat.get("combatId", ""))
	var run_id := str(session.run.runId)
	if check(await session.continue_run(run_id), "continue the active encounter"):
		check(str(session.combat.get("combatId", "")) == encounter and _opening_hand(session) == cards,
			"continuing preserves the exact opening hand")

func _run() -> void:
	await _exercise(root.get_node("GameSession"))
	root.get_node("GameAudio").shutdown()
	await create_timer(0.1).timeout
	quit(1 if failures else 0)
