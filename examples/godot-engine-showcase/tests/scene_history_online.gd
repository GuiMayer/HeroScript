extends SceneTree
## Dedicated REST store only. Inspecting scenes must not activate historical state.
var failures := 0
func _init(): call_deferred("_run")
func check(ok: bool, message: String):
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func settle():
	for _frame in 8: await process_frame
func _run():
	var prefs = root.get_node("Preferences")
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-") or prefs.api_url != "http://127.0.0.1:61987":
		check(false,"requires isolated user directory and API/store on port 61987"); quit(1); return
	prefs.reduced_motion = true
	var session = root.get_node("GameSession")
	check(await session.connect_engine("default"),"connect historical-scene test to isolated REST")
	var data = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	check(await session.start_sandbox("development_lab",data.default_scenario,int(Time.get_unix_time_from_system())),"create dedicated inspectable sandbox")
	if failures: quit(1); return
	var candidate := {}
	for action in session.legal_actions:
		if action.get("source") == "Card": candidate = action; break
	check(not candidate.is_empty() and await session.submit_candidate(candidate),"record one canonical card command")
	root.get_node("Playback").clear()
	var router = load("res://main.tscn").instantiate(); root.add_child(router); await settle()
	var saved := JSON.stringify([session.run,session.combat])
	var id := str(session.run.runId)
	router.show_run_replay(id,true)
	var screen = router.host.get_child(0)
	var deadline := Time.get_ticks_msec()+15000
	while (screen.controller.selected_index < 0 or screen.controller.presentation_loading) and not screen.controller.page_error and Time.get_ticks_msec()<deadline: await process_frame
	await settle()
	check(screen.scene_file_path.ends_with("screens/run_replay.tscn") and not screen.entries.is_empty(),"real replay loads through canonical editable screen")
	check(screen.view.list.item_count == screen.entries.size() and not screen.view.frame_text.get_parsed_text().is_empty(),"recorded REST content fills native list and text controls")
	check(screen.controller.inspection_allowed,"sandbox publishes historical inspection capability")
	if screen.controller.inspection_allowed:
		await screen._watch(); await settle()
		check(is_instance_valid(screen._visual_replay) and screen._visual_replay.scene_file_path.ends_with("historical_beat.tscn"),"real command inspection uses the shared authored historical executor")
	check(JSON.stringify([session.run,session.combat]) == saved,"replay and visual inspection leave live run and combat immutable")
	router.show_timeline(); await settle()
	var timeline = router.host.get_child(0)
	deadline = Time.get_ticks_msec()+15000
	while timeline.entries.is_empty() and Time.get_ticks_msec()<deadline: await process_frame
	check(timeline.scene_file_path.ends_with("screens/timeline.tscn") and not timeline.entries.is_empty(),"real timeline populates the native scene without activating a branch")
	router.show_history(); await settle()
	var archive = router.host.get_child(0)
	deadline = Time.get_ticks_msec()+15000
	while archive.loading and Time.get_ticks_msec()<deadline: await process_frame
	check(archive.scene_file_path.ends_with("screens/run_history.tscn") and archive.entries.any(func(record): return record.runId == id),"real archive includes the dedicated run in its setting")
	check(JSON.stringify([session.run,session.combat]) == saved,"opening timeline and archive never restores their inspected state into gameplay")
	var verified: Dictionary = await session.verify()
	check(verified.get("ok",false) and verified.get("data",{}).get("isValid",false),"canonical replay integrity survives all scene transitions")
	router.queue_free(); await settle()
	var transport = root.get_node("GameServices")._transport
	deadline = Time.get_ticks_msec()+5000
	while (not transport._get_flights.is_empty() or not transport._queue.is_empty() or transport._idle.size()!=transport._workers.size()) and Time.get_ticks_msec()<deadline: await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
