extends SceneTree
## Opt-in: creates its own QA attempts; never opens or changes an existing save.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> bool:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
	return ok
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	var cursor = root.get_node("Playback")
	var prefs = root.get_node("Preferences")
	prefs.auto_animations = true; prefs.animation_preset = "fast"
	prefs.reduced_motion = false; prefs.animation_speed = 1.0
	if not check(await session.connect_engine(), "connect real engine"): quit(1); return
	var seed := int(Time.get_unix_time_from_system()) % 1000000000
	for setting in ["default", "ascendant", "volatile-core"]:
		cursor.clear()
		if not check(session.select_setting(setting) and await session.start_campaign(seed), setting + ": create QA attempt"): continue
		print("QA_RUN=", setting, " ", session.run.get("runId", ""))
		for _step in 12:
			if not session.combat.is_empty(): break
			var choice := {}
			for type in ["START_ENCOUNTER", "ADVANCE_NODE", "RESOLVE_NODE"]:
				for item in session.activity_choices():
					if item.type == type: choice = item; break
				if not choice.is_empty(): break
			if choice.is_empty() or not await session.submit_activity(choice): break
		if not check(not session.combat.is_empty(), setting + ": enter configured combat"): continue
		var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
		cursor.skip(); await settle() # Skip encounter entry presentation only.
		var screen = router.host.get_child(0)
		var candidate := {}
		for option in session.legal_actions:
			if str(option.get("source", "")) == "Card": candidate = option; break
		if check(not candidate.is_empty(), setting + ": receive legal card"):
			var sequence := int(session.run.sequence)
			var trace = screen._interaction_trace
			if setting == "ascendant":
				var card_id := str(candidate.command.cardInstanceId)
				var center: Vector2 = preload("res://scripts/ui/card_motion_pose.gd").read(screen.view.hand.card_for(card_id)).center
				screen.view.hand.interaction.pressed = card_id; screen.view.hand.interaction.press_position = center; screen.view.hand.interaction.dragging = true
				screen.view.hand._drag_visual(center + Vector2(70, -40))
			await screen._play_candidate(candidate)
			var published: String = JSON.stringify(session.run)
			var started := Time.get_ticks_msec()
			while cursor.has_blocking_work() and Time.get_ticks_msec() - started < 8000: await process_frame
			await settle()
			check(not cursor.has_blocking_work() and int(session.run.sequence) == sequence + 1, setting + ": real receipt settles without a second command")
			check(JSON.stringify(session.run) == published, setting + ": animation leaves authoritative state unchanged")
			var marks: Dictionary = trace.snapshot()
			check(marks.has("receipt") and marks.has("envelope") and marks.has("settled") and marks.sent >= marks.input and marks.receipt >= marks.sent and marks.settled >= marks.envelope,
				setting + ": correlated input, REST and settlement milestones")
			print("INTERACTION_METRIC=", JSON.stringify(marks))
			check(not cursor.measurements().is_empty(), setting + ": record separate synchronization and visual timings")
			print("PRESENTATION_METRIC=", JSON.stringify(cursor.measurements()[-1]) if not cursor.measurements().is_empty() else "none")
			var verification: Dictionary = await session.verify()
			check(verification.get("ok", false) and verification.data.get("isValid", false), setting + ": verify canonical replay")
		router.queue_free(); await settle(); cursor.clear()
	# Historical visual playback is tested with a mode that explicitly grants inspection.
	session.select_setting("default")
	var content = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	if check(await session.start_sandbox("development_lab", content.default_scenario, seed + 1), "create authorized historical QA sandbox"):
		var candidate := {}
		for option in session.legal_actions:
			if str(option.get("source", "")) == "Card": candidate = option; break
		if check(not candidate.is_empty() and await session.submit_candidate(candidate), "record historical QA command"):
			var response: Dictionary = await session.begin_replay_inspection(str(session.run.runId), int(session.run.sequence)).result()
			if check(response.get("ok", false), "load authorized canonical commit on demand"):
				var history = preload("res://scripts/ui/historical_beat_view.gd").new(); root.add_child(history)
				history.load_commit(response.data, {})
				var started := Time.get_ticks_msec()
				while history.blocking() and Time.get_ticks_msec() - started < 8000: await process_frame
				check(not history.blocking(), "real saved command uses shared historical director")
				history.queue_free(); await settle()
	cursor.clear(); root.get_node("GameAudio").shutdown(); await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
