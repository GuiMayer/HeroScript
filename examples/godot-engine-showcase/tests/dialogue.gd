extends SceneTree
## Uses real REST and the actual activity buttons, including redraw after every choice.
var failures := 0
var router
var session
var translator
var prefs
var original := {}

func _init() -> void:
	call_deferred("_run")

func check(value: bool, message: String) -> void:
	print("[PASS] " if value else "[FAIL] ", message)
	if not value: failures += 1

func settle() -> void:
	for _frame in 10: await process_frame

func _run() -> void:
	session = root.get_node("GameSession")
	session.failed.connect(func(error): print("Dialogue request failed: ", error))
	translator = root.get_node("I18n")
	prefs = root.get_node("Preferences")
	original = {"run": prefs.last_run_id, "locale": translator.locale, "size": prefs.window_resolution, "fullscreen": prefs.fullscreen, "scale": prefs.text_scale}
	router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await create_timer(.3).timeout
	while session.busy: await create_timer(.025).timeout
	if not await session.connect_engine():
		check(false, "engine ready")
		_finish()
		return
	var transport = load("res://scripts/engine/http_transport.gd").new()
	root.add_child(transport)
	transport.base_url = prefs.api_url
	var response: Dictionary = await transport.request(HTTPClient.METHOD_POST, "/api/v1/runs", {
		"settingId": "default", "modeId": "dialogue_demo", "playerEntityId": "dialogue-ui-test",
		"contentRevision": session.content_revision, "seed": int(Time.get_unix_time_from_system())})
	transport.queue_free()
	if not response.ok:
		check(false, "create dialogue run: " + str(response))
		_finish()
		return
	check(await session.continue_run(str(response.data.runId)), "connect dialogue run")
	router.open_game()
	await settle()
	await click_start()
	var panel = router.host.get_child(0).find_child("DialoguePanel", true, false)
	check(is_instance_valid(panel), "TALK button starts and renders conversation")
	if not is_instance_valid(panel):
		_finish()
		return
	check(panel.find_child("Choice_secret", true, false) == null, "hidden response is absent")
	prefs.fullscreen = false
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		prefs.set_resolution(resolution, false)
		for locale in ["en", "pt_BR"]:
			translator.set_locale(locale)
			prefs.text_scale = 1.2
			router.show_activity()
			await settle()
			panel = router.host.get_child(0).find_child("DialoguePanel", true, false)
			var speech: Label = panel.find_child("DialogueText", true, false)
			check(speech.text.contains("That ember") if locale == "en" else speech.text.contains("Essa brasa"), "localized speech: " + locale)
			check(panel.get_global_rect().end.x <= router.size.x + 1, "dialogue width fits: " + str(resolution))
			check(panel.choice_buttons.all(func(button): return button.size.x > 150 and button.size.y >= 48), "readable choice buttons")
			if DisplayServer.get_name() != "headless":
				await RenderingServer.frame_post_draw
				DirAccess.make_dir_recursive_absolute("res://tests/output")
				root.get_texture().get_image().save_png("res://tests/output/dialogue-%s-%s.png" % [resolution.x, locale])
	check(preload("res://scripts/presentation/dialogue_presenter.gd").localized({"text": "Fallback", "translations": {"pt_BR": "Traduzido"}}, "fr") == "Fallback", "unknown language falls back to English")
	var before := JSON.stringify(session.run)
	router.toggle_pause()
	await settle()
	check(paused and before == JSON.stringify(session.run), "pause does not advance the conversation")
	router.toggle_pause()
	await click_choice("trade")
	check(str(session.run.dialogues[0].nodeId) == "gift" and float(session.run.resources.gold.current) == 10, "choice applies price and enters next speech")
	check(session.run.deck.cardInstances.size() == 2, "universal effect grants actual card")
	await click_choice("back")
	panel = router.host.get_child(0).find_child("DialoguePanel", true, false)
	check(panel.find_child("Choice_trade", true, false).disabled, "used or unaffordable response stays disabled")
	await click_choice("ask")
	await click_choice("back")
	panel = router.host.get_child(0).find_child("DialoguePanel", true, false)
	check(is_instance_valid(panel.find_child("Choice_secret", true, false)), "remembered conversation unlocks response")
	check(await session.continue_run(str(session.run.runId)), "reconnect resumes dialogue")
	router.open_game()
	await settle()
	await click_choice("secret")
	await click_choice("leave")
	check(bool(session.run.dialogues[0].completed), "terminal choice completes conversation")
	var verified: Dictionary = await session.verify()
	check(verified.ok and bool(verified.data.get("isValid", false)), "real dialogue replay verifies")
	var receipt: Dictionary = await session.replay_commit(str(session.run.runId), int(session.run.sequence))
	if receipt.ok:
		var transcript: Array = preload("res://scripts/presentation/dialogue_presenter.gd").recorded_transcript(receipt.data.stateAfter, translator)
		check(transcript.size() >= 8, "saved replay renders dialogue transcript")
	else: check(false, "read saved dialogue commit")
	_finish()

func click_start() -> void:
	var screen = router.host.get_child(0)
	var button: Button
	for item in screen.choice_buttons:
		if item.text == translator.text("TALK"): button = item
	check(is_instance_valid(button), "TALK action is available")
	if is_instance_valid(button):
		button.pressed.emit()
		await wait_command()

func click_choice(id: String) -> void:
	var button: Button = router.host.get_child(0).find_child("Choice_" + id, true, false)
	check(is_instance_valid(button) and not button.disabled, "choice available: " + id)
	if is_instance_valid(button) and not button.disabled:
		button.pressed.emit()
		await wait_command()

func wait_command() -> void:
	for _attempt in 500:
		await create_timer(.025).timeout
		if not session.busy: break
	await settle()

func _finish() -> void:
	prefs.last_run_id = original.run
	prefs.text_scale = original.scale
	prefs.fullscreen = original.fullscreen
	prefs.set_resolution(original.size, false)
	translator.set_locale(original.locale)
	prefs.save()
	root.get_node("GameAudio").shutdown()
	print("SHOWCASE_DIALOGUE failures=", failures)
	await create_timer(.1).timeout
	quit(0 if failures == 0 else 1)
