extends SceneTree
const Live = preload("res://tests/animation_live.gd")
const Policy = preload("res://scripts/presentation/motion_policy.gd")
const Profile = preload("res://scripts/presentation/card_animation_profile.gd")
const Choreography = preload("res://scripts/presentation/card_choreography.gd")
var failures := 0
class Fixture extends Live.Fixture:
	var operation := "use"
	func send_prepared(command: Dictionary) -> Dictionary:
		var result: Dictionary = await super.send_prepared(command)
		# Several visible impacts must not add one second apiece to a card cycle.
		var hit: Dictionary = result.data.state.resolution.frames[0].duplicate(true)
		result.data.state.resolution.frames.append(hit)
		result.data.state.resolution.frames.append(hit.duplicate(true))
		if operation != "retained":
			zones.zones[1].cards = [{"cardInstanceId": "played", "definitionId": "basic_attack"}]; zones.zones[1].count = 1
			result.data.state.resolution.frames.append({"transitionType": "effect", "cardZoneSteps": [{"flowId": "card.played.to-exhaust" if operation == "burn_center" else "card.played.to-discard", "sourceAddress": "$run::::custom", "targetAddress": "$run::::spent", "instanceIds": ["played"]}]})
		return result
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func run_checks() -> void:
	var prefs = root.get_node("Preferences")
	prefs.animation_speed = 1.; prefs.animation_preset = "normal"; prefs.reduced_motion = false; prefs.auto_animations = true
	var session = root.get_node("GameSession"); var cursor = root.get_node("Playback")
	for operation in ["use", "retained", "burn_center"]:
		var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab); await settle()
		lab.animation_picker.select_animation(operation)
		var seconds := .5 if operation == "burn_center" else 1.
		lab.animation_cycle.value = seconds
		var measures: Array = []
		lab.motion_lab.finished.connect(func(_models, metrics): measures.append(metrics))
		lab.play_selected_animation()
		while lab.motion_lab._running: await process_frame
		var lab_time: float = measures[-1].elapsed_ms / 1000.
		check(lab.motion_lab._stage.call().is_equal_approx(Choreography.stage_center(lab.hand)), operation + ": lab uses the canonical card-relative stage")
		lab.queue_free(); await settle(); cursor.clear()
		var fixture := Fixture.new(); fixture.operation = operation; fixture.retained = operation == "retained"
		fixture.zones.zones.append({"zoneId": "spent", "contentsVisible": true, "count": 0, "cards": [], "presentation": {"slot": "exile_pile" if operation == "burn_center" else "discard_pile", "labelKey": "Exile" if operation == "burn_center" else "Discard pile"}})
		session.configure(fixture)
		session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
		session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
		session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
		var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
		var screen = router.host.get_child(0)
		check(screen._card_stage().is_equal_approx(Choreography.stage_center(screen.view.hand)), operation + ": combat uses the identical staging formula")
		var started := Time.get_ticks_usec()
		await screen._play_candidate(screen.presenter.candidate_for_card("played"))
		while cursor.has_blocking_work() and Time.get_ticks_usec() - started < 4000000: await process_frame
		var combat_time := (Time.get_ticks_usec() - started) / 1000000.
		await settle(); screen = router.host.get_child(0)
		print("PARITY_SECONDS=", operation, " lab=", lab_time, " combat=", combat_time)
		check(absf(lab_time - seconds) < .2 and absf(combat_time - lab_time) < .2, operation + ": live cycle matches lab despite several impact reactions")
		check(not cursor.has_blocking_work() and not screen.controller.locked() and fixture.sends == 1, operation + ": one accepted command settles without extra confirmations")
		check(screen.view.feedback.effects.find_child("CardBurnView", true, false) == null, operation + ": no retained burn snapshot")
		screen.view.feedback.clear_cosmetics()
		check(screen.view.feedback._tails.is_empty() and screen.view.actor_portraits.values().all(func(portrait): return portrait.modulate == Color.WHITE), operation + ": cosmetic cancellation cleans tweens and tint")
		router.queue_free(); await settle(); cursor.clear()
	# Explicit Apply writes only this isolated test user directory, never a run.
	var previous := FileAccess.get_file_as_string(Profile.PATH) if FileAccess.file_exists(Profile.PATH) else ""
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab); await settle()
	var captured = Policy.capture("ascendant", {})
	lab.animation_picker.select_animation("draw_five"); lab.animation_cycle.value = .6
	lab.apply_animation_button.pressed.emit()
	check(is_equal_approx(Policy.capture("ascendant", {}).duration("draw", true), .6), "Apply makes the tested duration available to the next combat capture")
	check(captured.duration("draw", true) == 1., "applying a profile cannot change an already captured animation")
	lab.animation_picker.select_animation("use"); lab.animation_cycle.value = .8; lab.apply_animation_button.pressed.emit()
	var use_policy = Policy.capture("ascendant", {})
	check(is_equal_approx(use_policy.duration("card_flight", true) + use_policy.duration("staged_discard", true), .8) and is_equal_approx(use_policy.duration("card_flight", true) + use_policy.duration("retain", true), .8), "use/retained share the same applied two-phase choreography")
	Profile.save("draw", 20., "center")
	check(Policy.capture("ascendant", {}).duration("watchdog") > 20., "long applied cycles are not truncated by the production watchdog")
	await settle()
	if previous.is_empty(): DirAccess.remove_absolute(ProjectSettings.globalize_path(Profile.PATH))
	else:
		var file := FileAccess.open(Profile.PATH, FileAccess.WRITE); file.store_string(previous); file.close()
	lab.queue_free(); await settle(); root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
