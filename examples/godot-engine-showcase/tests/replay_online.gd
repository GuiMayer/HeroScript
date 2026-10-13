extends SceneTree
var failures := 0
var capture_directory := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func wait_content(screen) -> void:
	for i in 300:
		await create_timer(.01).timeout
		if screen.controller.first_content_ms >= 0 or screen.controller.page_error: return
func wait_idle(session) -> void:
	for i in 300:
		if not session.busy: return
		await create_timer(.01).timeout
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): capture_directory = argument.trim_prefix("--lab-output=")
	var session = root.get_node("GameSession")
	var transport = root.get_node("GameServices")._transport
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	check(await session.connect_engine("default"), "connect isolated replay QA")
	var samples: Array[float] = []
	for locale in ["en", "pt_BR"]:
		root.get_node("I18n").set_locale(locale)
		for resolution in [Vector2i(1280,720), Vector2i(1920,1080), Vector2i(2560,1080)]:
			root.size = resolution
			var before := JSON.stringify([session.run, session.combat])
			var opened := Time.get_ticks_msec()
			router.show_run_replay("8fbd169e-58e4-876e-846f-f4bb37a7db7f", true)
			var screen = router.host.get_child(0)
			await wait_content(screen); await process_frame
			if not capture_directory.is_empty() and DisplayServer.get_name() != "headless": await RenderingServer.frame_post_draw
			var elapsed := float(Time.get_ticks_msec() - opened); samples.append(elapsed)
			check(screen.controller.entries.size() == 25 and screen.selected_index == 0 and screen.controller.through_sequence == 124, "copied long journey opens one page: %s %s" % [locale, resolution])
			check(screen.view.list.item_count == 25 and not screen.view.frame_text.get_parsed_text().is_empty() and not screen.controller.presentation_loading, "recorded content is rendered rather than skeleton")
			check(not screen.controller.inspection_allowed and not screen.view.inspect_button.visible, "normal campaign retains historical inspection restriction")
			check(before == JSON.stringify([session.run,session.combat]), "opening replay cannot activate another gameplay state")
			if not capture_directory.is_empty() and DisplayServer.get_name() != "headless":
				root.get_texture().get_image().save_png(capture_directory.path_join("%s-replay-%sx%s.png" % [locale, resolution.x, resolution.y]))
			await screen._select(24); await screen._next()
			check(screen.selected_index == 25 and screen.controller.entries.size() == 50, "REST playback crosses pinned page")
			router.back_to_menu(); await wait_idle(session); await process_frame
	var sorted := samples.duplicate(); sorted.sort()
	print("[METRIC] ", JSON.stringify({"first_content_render_ms": samples, "p95_ms": sorted.back(), "resolutions": 3, "locales": 2}))
	# Fresh authoritative QA journeys for each setting; these are not personal saves.
	for setting in ["default", "ascendant", "volatile-core"]:
		await wait_idle(session)
		session.select_setting(setting)
		var launch: Dictionary = session.selected_setting.get("launch", {})
		var created_run: Dictionary = await transport.request(HTTPClient.METHOD_POST, "/api/v1/runs",
			{"settingId":setting, "runDefinitionId":launch.get("runDefinitionId", "default_run"),
			"modeId":launch.get("modeId", "standard"), "playerEntityId":"replay-qa-%s-%s" % [setting, Time.get_ticks_usec()],
			"seed":Time.get_ticks_usec(), "contentRevision":session.selected_setting.get("currentRevision", "")})
		var opened: bool = created_run.get("ok", false)
		check(opened, "create replay QA " + setting)
		if not opened: print("[DIAGNOSTIC] ", setting, " ", transport.last_error)
		if not opened: continue
		var id := str(created_run.data.runId)
		var before := JSON.stringify(session.run)
		router.show_run_replay(id, true); var screen = router.host.get_child(0); await wait_content(screen)
		check(screen.controller.selected_index == 0 and screen.controller.entries.size() == 1, "read-only initial recording " + setting)
		check(before == JSON.stringify(session.run), "replay preserves live snapshot " + setting)
		var verified: Dictionary = await session.verify_run(id)
		check(verified.ok and verified.data.get("isValid", false), "semantic replay stays valid " + setting)
		router.back_to_menu(); await wait_idle(session); await process_frame
	# An authorized mode exercises the compact state presentation endpoint.
	await wait_idle(session); session.select_setting("default")
	var created: Dictionary = await transport.request(HTTPClient.METHOD_POST, "/api/v1/runs",
		{"settingId":"default", "runDefinitionId":"default_run", "modeId":"combat_sandbox",
		"playerEntityId":"replay-inspection-qa-%s" % Time.get_ticks_usec(), "seed":Time.get_ticks_usec(),
		"contentRevision":session.selected_setting.get("currentRevision", "")})
	check(created.ok, "create authorized replay mode")
	if created.ok:
		router.show_run_replay(str(created.data.runId), true); var screen = router.host.get_child(0); await wait_content(screen)
		check(screen.controller.inspection_allowed and not screen.controller.commit_cache.is_empty() and not screen.view.state_text.get_parsed_text().is_empty(), "authorized replay publishes compact canonical summary")
		var read_count: int = transport.timings.size()
		await screen._restart()
		check(read_count == transport.timings.size(), "restart cached command performs no HTTP")
	router.queue_free()
	for i in 6: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
