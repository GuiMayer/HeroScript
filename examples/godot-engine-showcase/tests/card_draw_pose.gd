extends SceneTree
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
const Motion = preload("res://scripts/ui/card_zone_motion.gd")
const Registry = preload("res://scripts/ui/card_transit_registry.gd")
const Handle = preload("res://scripts/presentation/presentation_handle.gd")
const Live = preload("res://tests/animation_live.gd")
var failures := 0
var output := ""
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 8: await process_frame
func model(id: String) -> Dictionary:
	return {"cardInstanceId": id, "definitionId": "basic_attack", "name": "Strike", "effects": ["Test"], "costs": [{"resourceId": "energy", "amount": 1}]}
func same_pose(first: Dictionary, second: Dictionary) -> bool:
	return first.center.distance_to(second.center) < .1 and absf(angle_difference(first.rotation, second.rotation)) < .001 and first.scale.distance_to(second.scale) < .001
func run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
	# Different hand and overlay transforms catch center/pivot math that only works at 1:1.
	for mode in ["adaptive", "reading"]:
		for counts in [Vector2i(0, 1), Vector2i(0, 2), Vector2i(2, 5), Vector2i(5, 10)]:
			var context = preload("res://scripts/ui/visual_context.gd").new(null, {"reduced_motion": true}, preload("res://data/themes/ivory_run.tres"))
			var holder := Control.new(); holder.position = Vector2(27, 15); holder.scale = Vector2.ONE * .85; root.add_child(holder)
			var hand = preload("res://scripts/ui/card_hand_view.gd").new(context)
			hand.size = Vector2(1400, 520); hand.mode_override = mode; holder.add_child(hand)
			var overlay := Control.new(); overlay.position = Vector2(55, 18); overlay.scale = Vector2.ONE * .9; overlay.rotation = .04; root.add_child(overlay)
			var initial: Array = []; var final: Array = []; var ids: Array = []
			for index in counts.y:
				final.append(model("card-%s" % index))
				if index < counts.x: initial.append(final[-1])
				else: ids.append(final[-1].cardInstanceId)
			# Respect published insertion positions, not just append-only purchases.
			if counts.x == 2: final.reverse()
			hand.sync_models(initial, "fixture"); await settle()
			context._options.reduced_motion = false; context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
			var cycle := .04 if counts.y == 1 else .12
			context.motion._values.draw = cycle; context.motion._values.stagger = 0; context.motion._values.batch_span = 0
			hand.set_read_only(true); hand.begin_presentation(final)
			var owner = Registry.new(hand, overlay, context)
			var steps := [{"sourceAddress": "pile", "targetAddress": "hand", "instanceIds": ids, "targetOrderAfter": final.map(func(card): return card.cardInstanceId)}]
			var snapshots := [{"zones": [{"contentsVisible": true, "cards": final}]}]
			var original := JSON.stringify([steps, snapshots, final])
			var anchor := func(address, id): return hand.destination_center(id) if address == "hand" else Vector2(90, 590)
			var role := func(address): return "hand" if address == "hand" else "pile"
			var handle = Handle.new()
			var arrivals: Array = []
			Motion.execute(steps, snapshots, {}, anchor, overlay, context, handle, role, owner, func(item):
				arrivals.append(same_pose(Pose.read(owner._records[item.id]), hand.destination_pose(item.id))))
			var label := "%s %s→%s" % [mode, counts.x, counts.y]
			check(hand._aligning_arrivals and hand._destination_order == final.map(func(card): return card.cardInstanceId), "reserves the final ordered fan: " + label)
			if counts.x > 0: check(not hand._motions.is_empty(), "existing cards open space during the flight: " + label)
			# Step actual executor tweens to inspect their shared trajectory deterministically.
			for tween in handle._tweens:
				tween.pause(); tween.custom_step(cycle * .5)
			for id in ids:
				var ghost: Control = owner._records[id]
				var measured := Pose.read(ghost); var target: Dictionary = hand.destination_pose(id)
				check(absf(angle_difference(measured.rotation, target.rotation)) < .001, "in-flight card already follows its fan angle: " + id + "/" + label)
				check(measured.center.distance_to(Pose.arrival_center(Vector2(90, 590), target.center, target.rotation, context.motion.progress("zone_transfer", .5))) < .1, "center follows the cubic path despite transformed overlay: " + id + "/" + label)
			if not output.is_empty() and DisplayServer.get_name() != "headless" and counts == Vector2i(2, 5):
				await RenderingServer.frame_post_draw
				root.get_texture().get_image().save_png(output.path_join(mode + "-midflight.png"))
			# Finish the parallel slot reflow before checking the final frame handoff.
			for tween in hand._motions.values():
				if tween.is_valid(): tween.custom_step(1)
			for state in hand._emphasis.values():
				if state.tween != null and state.tween.is_valid(): state.tween.custom_step(1)
			for tween in handle._tweens.duplicate():
				if tween.is_valid(): tween.custom_step(cycle * .5 + .001)
			check(handle.done and handle.result == "completed" and arrivals.size() == ids.size() and arrivals.all(func(ok): return ok), "last transit frame equals the destination pose: " + label)
			for id in ids:
				check(hand.card_for(id).visible and same_pose(Pose.read(hand.card_for(id)), hand.destination_pose(id)) and hand._emphasis[id].tween == null, "handoff needs no corrective rotation or translation: " + id + "/" + label)
			check(is_equal_approx(context.motion.duration("draw", true), cycle) and JSON.stringify([steps, snapshots, final]) == original, "duration and published facts remain unchanged: " + label)
			if not handle.done: handle.finish("cancelled")
			hand.end_presentation(); hand.sync_models(final, "fixture"); owner.dispose()
			holder.queue_free(); overlay.queue_free(); await settle()
	var lab_seconds := await lifecycle_checks()
	await combat_checks(lab_seconds)
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
func lifecycle_checks() -> float:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab); await settle()
	lab.visual_context._options.reduced_motion = false
	lab.motion_lab.preset = "normal"
	var outcomes: Array = []
	lab.motion_lab.finished.connect(func(_models, metrics): outcomes.append(metrics))
	var scenario := {"id": "draw", "operation": "draw", "count": 2, "cycleSeconds": .3}
	lab.motion_lab.play(scenario, lab.models)
	await create_timer(.08).timeout
	var ghosts: Array = lab.motion_lab.registry._records.values()
	check(not ghosts.is_empty() and lab.hand._aligning_arrivals, "actual laboratory uses shared slot reservation")
	var first: Control = ghosts[0]; var snapshot := Pose.read(first)
	var pause_started := Time.get_ticks_usec()
	paused = true; await create_timer(.05).timeout
	check(is_instance_valid(first) and same_pose(Pose.read(first), snapshot), "pause freezes the actual curved purchase")
	paused = false
	var pause_seconds := (Time.get_ticks_usec() - pause_started) / 1000000.0
	while lab.motion_lab._running: await process_frame
	var active_seconds: float = outcomes[-1].elapsed_ms / 1000.0 - pause_seconds
	check(absf(active_seconds - .3) < .1, "configured laboratory cycle stays at 0.3 active seconds")
	check(lab.motion_lab.overlay.get_child_count() == 0 and lab.hand._transit.is_empty(), "laboratory completion leaves no transit visuals")
	lab.motion_lab.play(scenario, lab.models); await create_timer(.03).timeout
	lab.motion_lab.skip(); await settle()
	check(not lab.motion_lab._running and lab.motion_lab.overlay.get_child_count() == 0 and lab.hand._transit.is_empty(), "skip removes curves and releases the hand")
	for preset in ["instant", "normal"]:
		lab.motion_lab.preset = preset; lab.visual_context._options.reduced_motion = preset == "normal"
		lab.motion_lab.play(scenario, lab.models)
		await settle()
		check(not lab.motion_lab._running and lab.motion_lab.overlay.get_child_count() == 0, "instant/reduced purchase has no animation wait: " + preset)
	lab.queue_free(); await settle()
	return active_seconds

func combat_checks(lab_seconds: float) -> void:
	var session = root.get_node("GameSession"); var cursor = root.get_node("Playback")
	var preferences = root.get_node("Preferences")
	preferences.reduced_motion = false; preferences.animation_speed = 1.0; preferences.animation_preset = "normal"; preferences.auto_animations = false
	var fixture := Live.Fixture.new()
	fixture.zones.zones.append({"zoneId": "deck", "contentsVisible": false, "count": 3, "cards": [], "presentation": {"slot": "draw_pile", "labelKey": "Draw pile"}})
	session.configure(fixture); cursor.clear()
	session._run = fixture.state.duplicate(true); session._combat = fixture.combat.duplicate(true); session._card_zones = fixture.zones.duplicate(true)
	session._legal_actions = fixture.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
	var screen = router.host.get_child(0)
	var before := {"run": session.run, "combat": session.combat, "zones": fixture.zones.duplicate(true)}
	var after := before.duplicate(true)
	after.run.sequence = 2; after.combat.step = 2
	var ids := ["drawn-a", "drawn-b", "drawn-c"]
	for id in ids: after.zones.zones[0].cards.append({"cardInstanceId": id, "definitionId": "basic_attack"})
	after.zones.zones[0].count = 5; after.zones.zones[1].count = 0
	var frame := {"transitionType": "effect", "cardZoneSteps": [{"sourceAddress": "$run::::deck", "targetAddress": "$run::::custom", "instanceIds": ids}]}
	var receipt := {"sequence": 2, "state": {"combat": after.combat, "resolution": {"frames": [frame]}}}
	var preserved := JSON.stringify([before, after, receipt])
	var envelope = preload("res://scripts/presentation/presentation_envelope.gd").build(receipt, before, after, [])
	session._run = after.run.duplicate(true); session._combat = after.combat.duplicate(true); session._card_zones = after.zones.duplicate(true)
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	screen._active_motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
	screen._active_motion._values.draw = .3; screen._active_motion._values.stagger = 0; screen._active_motion._values.batch_span = 0
	cursor.load_envelope(envelope); screen._on_presentation_ready(envelope)
	var started := Time.get_ticks_usec()
	screen.presentation_driver.advance()
	await create_timer(.1).timeout
	check(screen.view.hand._aligning_arrivals and screen._transits != null, "actual combat enters the same executor-managed purchase")
	var correct := true
	for id in ids:
		var ghost: Control = screen._transits._records.get(id)
		correct = correct and is_instance_valid(ghost) and absf(angle_difference(Pose.read(ghost).rotation, screen.view.hand.destination_pose(id).rotation)) < .001
	check(correct, "combat ghosts use the same in-flight fan pose as the laboratory")
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png(output.path_join("combat-midflight.png"))
	while cursor.has_blocking_work() and Time.get_ticks_usec() - started < 2000000: await process_frame
	var combat_seconds := (Time.get_ticks_usec() - started) / 1000000.0
	check(not cursor.has_blocking_work() and absf(combat_seconds - .3) < .1 and absf(combat_seconds - lab_seconds) < .1, "combat and laboratory preserve the same configured active duration")
	await settle(); screen = router.host.get_child(0)
	check(screen.view.hand.order.size() == 5 and ids.all(func(id): return screen.view.hand.card_for(id).visible), "canonical combat reconciliation keeps every drawn card")
	check(JSON.stringify([before, after, receipt]) == preserved and fixture.sends == 0, "visual exercise neither mutates published facts nor issues gameplay commands")
	router.queue_free(); await settle(); cursor.clear()
