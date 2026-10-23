extends SceneTree
const Live = preload("res://tests/animation_live.gd")
var failures := 0
var output := ""
class VictoryGateway extends Live.Fixture:
	var full_map: Array = []
	func send_prepared(command: Dictionary) -> Dictionary:
		var result: Dictionary = await super.send_prepared(command)
		combat.status = "VICTORY"
		state.map.nodes = full_map.duplicate(true)
		result.data.state.combat = combat.duplicate(true)
		return result
	func read_projections(run: String, encounter: String, include: bool) -> Dictionary:
		var result: Dictionary = await super.read_projections(run, encounter, include)
		if combat.status == "VICTORY":
			result.commands.data.commands = [{"type": "RESOLVE_COMBAT", "validPayload": {"combatId": combat.combatId}}]
		return result
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 8: await process_frame
func play(screen) -> void: await screen._play_candidate(screen.presenter.candidate_for_card("played"))
func run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
	root.size = Vector2i(1280, 800) # Same embedded-game size as the reported failure.
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = false; prefs.auto_animations = true; prefs.animation_speed = 1.; prefs.animation_preset = "normal"
	var session = root.get_node("GameSession"); var cursor = root.get_node("Playback")
	var gateway := VictoryGateway.new()
	gateway.full_map = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/map_world_one.json")).nodes
	gateway.state.merge({"currentNodeId": "s01_arrival", "lifecycle": "Active", "map": {"nodes": [gateway.full_map[0].duplicate(true)], "resolvedNodeIds": []}})
	session.configure(gateway)
	session._run = gateway.state.duplicate(true); session._combat = gateway.combat.duplicate(true); session._card_zones = gateway.zones.duplicate(true)
	session._legal_actions = gateway.read_legal_actions("", "").data.candidates; session.synchronized = true
	session._card_evaluation_context = session._evaluation_context(session.run, session.combat)
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	router.apply_visual_profile(load("res://data/themes/ivory_run.tres")); router.show_combat(); await settle()
	check(session.run.map.nodes.size() == 1, "active combat starts with the real compact session-map contract")
	play(router.host.get_child(0))
	var started := Time.get_ticks_msec(); var premature := false
	while router.current_screen == "combat" and Time.get_ticks_msec() - started < 5000:
		await process_frame
		if router.current_screen == "map" and (session.busy or cursor.has_blocking_work()): premature = true
	check(router.current_screen == "map" and not premature, "navigation opens only after accepted final presentation and synchronization")
	await settle()
	if router.current_screen == "map":
		var view = router.host.get_child(0).view
		check(view.route.buttons.size() == 50 and view.route._geometry.edges.size() > 0, "first victory exposes all stops and connections before result confirmation")
		check(session.run.activeEncounterId == "combat" and session.combat.status == "VICTORY", "pending encounter reference is preserved until gameplay confirms the result")
		check(view.find_child("MapAction_RESOLVE_COMBAT", true, false) != null, "confirmation remains a published explicit action")
		var first: Button = view.route.buttons.s01_arrival
		check(first.get_global_rect().get_center().distance_to(view._scroll.get_global_rect().get_center()) < 2, "first completed combat stays visually centered")
		var origin := first.get_global_rect()
		await settle()
		check(origin == first.get_global_rect(), "no late camera jump after the first combat exit")
		check(session.run.currentNodeId == "s01_arrival" and session.run.map.resolvedNodeIds.is_empty() and gateway.sends == 1, "showing the full map never auto-confirms, advances or submits another gameplay command")
		if not output.is_empty() and DisplayServer.get_name() != "headless":
			await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(output.path_join("first-victory-map.png"))
	router.queue_free(); await settle(); cursor.clear(); root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
