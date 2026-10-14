extends SceneTree
var failures := 0
var output_dir := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func capture(label: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(label + ".png")) == OK, "saved " + label)
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	root.gui_embed_subwindows = true
	var reading = load("res://scripts/ui/dialogue_reading.gd").new()
	var model := {"runId": "r", "branchId": "b", "dialogueInstanceId": "d", "nodeId": "n", "text": "First paragraph is read before choosing. Second paragraph is still only local reading."}
	reading.bind(model, "en", 45)
	check(reading.pages.size() > 1 and reading.fully_revealed(), "instant readable text is default")
	reading.gradual = true; reading.advance_reading()
	check(reading.page == 0 and reading.fully_revealed(), "confirm reveals without advancing or choosing")
	reading.advance_reading()
	check(reading.page == 1 and not reading.fully_revealed(), "second confirm changes only the local page")
	var state: Dictionary = reading.capture()
	reading.page = 0; reading.restore(state)
	check(reading.page == 1, "reading state restores in the same context")
	reading.gradual = false; reading.restore(state)
	check(not reading.gradual and not state.has("gradual"), "local reading state never overrides accessibility preferences")
	model.branchId = "other"; reading.bind(model, "en", 45); reading.restore(state)
	check(reading.page == 0, "reading state cannot leak to another branch")
	var panel = load("res://scripts/ui/dialogue_panel.gd").new()
	root.add_child(panel); panel.size = Vector2(1000, 620)
	model.merge({"title": "A conversation", "speaker": "", "portrait": "", "history": [], "options": [{"id": "one", "text": "A costly choice", "available": true, "cost": "5 Gold", "reason": "", "command": {"type": "CHOOSE_DIALOGUE_OPTION"}}]})
	panel.apply_model(model)
	var calls: Array = []; panel.choice_requested.connect(func(command): calls.append(command))
	panel.reading.gradual = true; panel.reading.revealed = 0.; panel._sync_reading()
	panel.choice_buttons[0].pressed.emit()
	check(calls.is_empty() and panel.choice_buttons[0].disabled, "unread text cannot submit a choice")
	panel.advance_reading(); panel.set_read_only(true)
	check(panel.choice_buttons[0].disabled, "session lock remains authoritative after reveal")
	panel.queue_free(); await settle()
	await _preferences_and_pause()
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)

func _preferences_and_pause() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"):
		check(false, "preference interactions require an isolated user directory"); return
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var original := ConfigFile.new()
	if original.load(prefs.SAVE_PATH) != OK: prefs.save(false); original.load(prefs.SAVE_PATH)
	var saved_run: Dictionary = session._run.duplicate(true)
	var saved_commands: Array = session._available_commands.duplicate(true)
	var requests: int = root.get_node("GameServices").diagnostics().size()
	var invalid := ConfigFile.new()
	invalid.set_value("accessibility", "reveal_text_gradually", "true")
	check(not prefs._read_option(invalid, "accessibility", "reveal_text_gradually", "reveal_text_gradually", false), "malformed gradual preference uses instant fallback")
	var router = load("res://main.tscn").instantiate()
	root.add_child(router); await settle()
	var dialogue := {"dialogueInstanceId": "conversation", "nodeId": "line", "activityNodeId": "stop", "contentRevision": "revision",
		"title": {"text": "Meeting", "translations": {"pt_BR": "Encontro"}}, "speaker": {"text": "Keeper"},
		"text": {"text": "A long page to read before making a choice. ".repeat(40)}, "choices": [],
		"transcript": [{"speaker": {"text": "Keeper"}, "text": {"text": "Welcome", "translations": {"pt_BR": "Bem-vindo"}}},
			{"speaker": {"text": ""}, "text": {"text": "A previous choice", "translations": {"pt_BR": "Uma escolha anterior"}}}]}
	session._available_commands = []
	for language in ["en", "pt_BR"]:
		router.back_to_menu(); i18n.set_locale(language); prefs.locale = language; await settle()
		session._run = {"runId": "reading-fixture", "branchId": language, "currentNodeId": "stop", "dialogues": [dialogue],
			"map": {"nodes": [{"nodeId": "stop", "activity": {"type": "Dialogue"}}]}}
		prefs.reveal_text_gradually = false; prefs.save()
		router.show_dialogue(); await settle()
		var screen = router.host.get_child(0)
		var narrative = screen.view.panel
		check(narrative.reading.fully_revealed(), language + ": instant text is default")
		check(narrative.find_children("*", "CheckButton", true, false).is_empty() and not narrative.has_method("_open_history"), "dialogue has no accessibility/history controls")
		narrative.reading.page = 1; narrative._sync_reading()
		router.toggle_pause(); await settle()
		var history: Button = router.pause_layer.find_child("ConversationHistoryButton", true, false)
		check(history != null and not history.disabled and history.text == i18n.text("CONVERSATION HISTORY"), language + ": history lives in pause")
		var before: Dictionary = narrative.capture_local_state()
		var snapshot := JSON.stringify(session.run)
		history.pressed.emit(); await settle()
		var dialog: AcceptDialog = router.shell._history_dialog
		var transcript: RichTextLabel = dialog.content.get_child(0)
		check(dialog.visible and dialog.can_process() and dialog.exclusive and router.pause_layer.is_ancestor_of(dialog) and paused, "history opens as an interactive modal over paused gameplay")
		check(transcript.text.contains("Welcome" if language == "en" else "Bem-vindo") and transcript.text.contains("A previous choice" if language == "en" else "Uma escolha anterior"), "history displays localized transcript, including choices")
		await capture("conversation-history-" + language)
		dialog.canceled.emit(); await settle()
		check(paused and history.has_focus() and narrative.capture_local_state() == before and JSON.stringify(session.run) == snapshot, "closing history returns focus to pause without advancing reading/rules")
		# Use the real pause -> settings -> dialogue return path.
		router.shell.settings_requested.emit(); await settle()
		var settings = router.host.get_child(0)
		settings._show_category("accessibility"); await settle()
		var toggle: CheckButton = settings.find_child("RevealTextGraduallyToggle", true, false)
		check(toggle != null and not toggle.button_pressed, "gradual text toggle is under Accessibility")
		await capture("dialogue-accessibility-" + language)
		toggle.button_pressed = true; await settle()
		var config := ConfigFile.new(); config.load(prefs.SAVE_PATH)
		check(settings == router.host.get_child(0) and settings.selected_category == "accessibility" and prefs.reveal_text_gradually and config.get_value("accessibility", "reveal_text_gradually") == true, "toggle applies/persists without resetting settings category")
		prefs.reveal_text_gradually = false; prefs.load_settings()
		check(prefs.reveal_text_gradually, "gradual setting survives reload")
		settings._close(); await settle()
		narrative = router.host.get_child(0).view.panel
		check(router.current_screen == "dialogue" and narrative.reading.page == 1 and narrative.reading.gradual and not narrative.reading.fully_revealed(), "return keeps page and uses saved accessibility preference")
		router.toggle_pause(); await settle()
		before = narrative.capture_local_state()
		await create_timer(.08).timeout
		check(narrative.capture_local_state() == before, "pause freezes gradual reveal")
		history = router.pause_layer.find_child("ConversationHistoryButton", true, false)
		history.pressed.emit(); await settle()
		var closing = router.shell._history_dialog
		router.toggle_pause(); await settle()
		check(not paused and not is_instance_valid(closing) and router.pause_layer == null, "resume removes history window with the pause canvas")
		router.show_settings(true); await settle()
		settings = router.host.get_child(0); settings._show_category("accessibility"); await settle()
		toggle = settings.find_child("RevealTextGraduallyToggle", true, false)
		toggle.button_pressed = false; settings._close(); await settle()
		narrative = router.host.get_child(0).view.panel
		check(narrative.reading.page == 1 and not narrative.reading.gradual and narrative.reading.fully_revealed(), "turning gradual off shows current page instantly; reading cache cannot override it")
		# Same-run records from another activity must never show stale conversation history.
		session._run.currentNodeId = "another-stop"
		router.toggle_pause(); await settle()
		check(router.pause_layer.find_child("ConversationHistoryButton", true, false).disabled, "history is unavailable outside the current conversation context")
		router.toggle_pause(); await settle()
	check(requests == root.get_node("GameServices").diagnostics().size(), "settings/history/reading interactions send no engine requests")
	router.queue_free(); await settle()
	session._run = saved_run; session._available_commands = saved_commands
	original.save(prefs.SAVE_PATH); prefs.load_settings(); i18n.set_locale(prefs.locale)
