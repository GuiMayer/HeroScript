extends Node

var failures: Array[String] = []
var original_locale := ""
var original_auto := true
var original_last_run := ""

func _ready() -> void:
	call_deferred("_run")

func check(condition: bool, label: String) -> void:
	print("[PASS] " if condition else "[FAIL] ", label)
	if not condition:
		failures.append(label)

func _run() -> void:
	original_locale = I18n.locale
	original_auto = Preferences.auto_animations
	original_last_run = Preferences.last_run_id
	var router := get_parent()
	check(I18n.catalogs.en.size() == I18n.catalogs.pt_BR.size(), "locale catalogs contain the same messages")
	for key in I18n.catalogs.pt_BR:
		if not I18n.catalogs.en.has(key) or str(I18n.catalogs.en[key]).count("%s") != str(key).count("%s"):
			check(false, "translation placeholders: " + key)
	I18n.set_locale("en")
	check(I18n.text("NEW JOURNEY") == "NEW JOURNEY", "English translation resolves")
	I18n.set_locale("pt_BR")
	check(I18n.text("NEW JOURNEY") == "NOVA JORNADA", "Portuguese translation resolves")
	router.show_settings()
	await get_tree().process_frame
	I18n.set_locale("en")
	await get_tree().process_frame
	await get_tree().process_frame
	check(_contains_text(router.host, "SETTINGS"), "settings rebuild in English at runtime")
	var language: OptionButton = router.host.find_child("LanguageSelector", true, false)
	check(language.get_item_text(language.selected) == "English", "language selector matches the active English locale")
	await get_tree().process_frame
	await get_tree().process_frame
	var initial_focus := get_viewport().gui_get_focus_owner()
	check(is_instance_valid(initial_focus), "settings establishes initial keyboard focus")
	var tab := InputEventKey.new()
	tab.keycode = KEY_TAB
	tab.physical_keycode = KEY_TAB
	tab.pressed = true
	Input.parse_input_event(tab)
	await get_tree().process_frame
	check(get_viewport().gui_get_focus_owner() != initial_focus, "physical Tab navigates the explicit focus graph")
	tab.pressed = false
	Input.parse_input_event(tab)
	if not await GameSession.connect_engine():
		check(false, "connect to engine")
		finish()
		return
	var seed := int(Time.get_unix_time_from_system()) % 90000000 + 300000000
	if not await GameSession.start_campaign(seed):
		check(false, "start campaign")
		finish()
		return
	var start := GameSession.command("START_ENCOUNTER")
	check(await GameSession.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})), "enter combat")
	var hash_before := JSON.stringify(GameSession.run).sha256_text()
	Preferences.auto_animations = false
	router.show_combat()
	await get_tree().process_frame
	var screen = router.host.get_child(0)
	for button in screen.action_buttons:
		check(button.get_global_rect().end.y <= router.get_global_rect().end.y, "action button remains inside viewport")
	var candidate: Dictionary = {}
	for item in GameSession.legal_actions:
		if str(item.get("source", "")) == "Card":
			candidate = item
			break
	if candidate.is_empty():
		check(false, "find legal card")
		finish()
		return
	var card_id := str(candidate.command.cardInstanceId)
	check(screen.target_buttons.size() == GameSession.combat.get("actors", []).size(), "every participant is represented on the battlefield")
	var inspection := await GameSession.inspect_hand()
	check(inspection.ok and not inspection.data.get("cards", []).is_empty(), "bulk card inspection is exposed by the engine")
	if inspection.ok:
		screen.presenter.accept_evaluations(inspection.data.get("cards", []))
		check(not screen.presenter.inspection_data(card_id).is_empty(), "version-matched evaluation reaches the presenter")
	var published_draw_cards: Array = []
	for zone in GameSession.card_zones.get("zones", []):
		if str(zone.get("zoneId", "")) == "drawPileInstanceIds":
			published_draw_cards = zone.get("cards", []).duplicate(true)
			break
	var inspected_draw_cards: Array = screen.presenter.pile_cards("drawPileInstanceIds")
	check(inspected_draw_cards.size() == published_draw_cards.size() and
		inspected_draw_cards.all(func(card): return not str(card.get("cardInstanceId", "")).is_empty()),
		"pile inspector uses canonical instance identities")
	check(candidate.has("costs"), "costs come from the canonical legal action")
	var end_turn_button: Button = screen.find_child("EndTurnButton", true, false)
	var end_turn_position := end_turn_button.global_position
	screen._choose_card(card_id)
	await get_tree().process_frame
	check(screen.selected_card == card_id and router.host.get_child(0) == screen, "selection keeps the current combat screen")
	check(end_turn_button.global_position.is_equal_approx(end_turn_position),
		"contextual combat controls preserve footer geometry")
	var targets: Array = screen._targets(candidate)
	if not targets.is_empty():
		check(not screen.target_buttons[targets[0]].disabled, "legal target highlighted")
	check(JSON.stringify(GameSession.run).sha256_text() == hash_before, "selection does not mutate authoritative data")
	screen._cancel_selection()
	I18n.set_locale("pt_BR")
	await get_tree().process_frame
	await get_tree().process_frame
	check(JSON.stringify(GameSession.run).sha256_text() == hash_before, "language change preserves run bytes")
	screen = router.host.get_child(0)
	screen._choose_card(card_id)
	var sequence_before_card := int(GameSession.run.sequence)
	if targets.is_empty():
		screen._offer_candidates([candidate])
	else:
		screen._select_target(str(targets[0]))
	# Wait for the actual REST transaction and acceptance animation.
	for _attempt in 600:
		await get_tree().create_timer(.025).timeout
		if int(GameSession.run.sequence) > sequence_before_card and not screen.submitting:
			break
	check(router.host.get_child(0) == screen, "card input refreshes the persistent combat screen")
	check(screen.card_buttons.values().all(func(card): return not card.animate_entry),
		"snapshot refresh does not replay every card entrance animation")
	check(not Playback.frames.is_empty(), "receipt exposes actual animation frames")
	check(Playback.index == 0, "manual animation mode waits for the player")
	check(screen._locked(), "new commands blocked until animations finish")
	router.toggle_pause()
	check(not router.host.can_process(), "pause suspends gameplay nodes")
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().process_frame
	var pause_focus := get_viewport().gui_get_focus_owner()
	check(is_instance_valid(pause_focus) and router.pause_layer.is_ancestor_of(pause_focus), "pause owns focus instead of the battlefield")
	check(is_instance_valid(router.pause_layer.find_child("AbandonRunButton", true, false)), "pause menu exposes the advertised abandon action")
	var down := InputEventJoypadButton.new()
	down.button_index = JOY_BUTTON_DPAD_DOWN
	down.pressed = true
	Input.parse_input_event(down)
	await get_tree().process_frame
	check(get_viewport().gui_get_focus_owner() != pause_focus, "controller D-pad navigates pause menu")
	down.pressed = false
	Input.parse_input_event(down)
	router.show_timeline()
	check(router.current_screen == "combat", "timeline shortcut cannot navigate behind the pause menu")
	var index_before := Playback.index
	await get_tree().create_timer(.12).timeout
	check(index_before == Playback.index, "pause preserves animation cursor")
	router.toggle_pause()
	var sequence_before := int(GameSession.run.sequence)
	while Playback.index < Playback.frames.size():
		screen._next_frame()
	check(not screen._locked(), "input unlocks when presentation completes")
	check(int(GameSession.run.sequence) == sequence_before, "presentation does not issue extra commands")
	var verification := await GameSession.verify()
	check(verification.ok and bool(verification.data.get("isValid", false)), "replay valid after localized interactive input")
	var campaign_run: String = GameSession.run.runId
	var campaign_bytes := JSON.stringify(GameSession.run)
	var live_cursor := Playback.index
	router.show_timeline()
	var timeline = router.host.get_child(0)
	for _attempt in 400:
		await get_tree().create_timer(.025).timeout
		if not timeline.entries.is_empty(): break
	check(not timeline.entries.is_empty(), "timeline loads real paginated commands")
	check(not timeline.history_allowed and not timeline.fork_allowed and not timeline.branch_read_allowed,
		"campaign exposes a visible timeline without sandbox inspection tools")
	check(not timeline.branch_button.visible and not timeline.branch_key.visible and not timeline.tree_view.visible,
		"campaign hides branch controls instead of presenting unusable actions")
	if not timeline.entries.is_empty():
		await timeline._select(0)
		check(timeline.history_view.presenter == null and timeline.playback.total == 0,
			"campaign timeline does not fetch historical state when inspection is disabled")
	check(GameSession.run.runId == campaign_run and JSON.stringify(GameSession.run) == campaign_bytes and Playback.index == live_cursor,
		"campaign timeline remains a read-only command summary")

	var presentation_file := FileAccess.open("res://data/presentation.json", FileAccess.READ)
	var presentation: Dictionary = JSON.parse_string(presentation_file.get_as_text())
	presentation_file.close()
	check(await GameSession.start_sandbox("combat_sandbox", presentation.default_scenario, seed + 701),
		"start sandbox for historical inspection and branching")
	var sandbox_candidate: Dictionary = {}
	for item in GameSession.legal_actions:
		if str(item.get("source", "")) == "Card":
			sandbox_candidate = item
			break
	if sandbox_candidate.is_empty():
		check(false, "find sandbox card for a resolution-bearing timeline command")
	else:
		check(await GameSession.submit_candidate(sandbox_candidate),
			"submit sandbox card through the canonical command boundary")
	var original_run: String = GameSession.run.runId
	var original_bytes := JSON.stringify(GameSession.run)
	var sandbox_live_cursor := Playback.index
	router.show_timeline()
	timeline = router.host.get_child(0)
	for _attempt in 400:
		await get_tree().create_timer(.025).timeout
		if not timeline.entries.is_empty(): break
	check(timeline.history_allowed and timeline.fork_allowed and timeline.branch_read_allowed,
		"sandbox exposes historical inspection and branch tools")
	if not timeline.entries.is_empty():
		await timeline._select(0)
		check(timeline.history_view.actor_panels.size() == GameSession.combat.actors.size(),
			"historical aggregate is projected into reusable actor views")
		await timeline._select(timeline.entries.size() - 1)
		check(timeline.playback.total > 0, "historical command exposes canonical resolution frames")
		timeline._next_frame()
		check(Playback.index == sandbox_live_cursor and JSON.stringify(GameSession.run) == original_bytes,
			"historical playback does not change live cursor or run")
		timeline.branch_key.text = "ui-branch-%s" % seed
		await timeline._branch()
		check(GameSession.run.runId != original_run and router.current_screen == "combat",
			"branch button activates a separate playable run")
		router.show_timeline()
		timeline = router.host.get_child(0)
		await timeline._load_tree()
		var origin: TreeItem = timeline.tree_view.get_root()
		check(origin != null and str(origin.get_metadata(0)) == original_run and origin.get_child_count() > 0,
			"branch tree retains the real origin and descendants")
		if origin:
			origin.select(0)
			await timeline._activate_selected()
			check(GameSession.run.runId == original_run and JSON.stringify(GameSession.run) == original_bytes,
				"existing branch activation restores the original unchanged run")
	router.back_to_menu()
	finish()

func _contains_text(node: Node, text: String) -> bool:
	if node is Label and node.text == text:
		return true
	for child in node.get_children():
		if _contains_text(child, text):
			return true
	return false

func finish() -> void:
	get_tree().paused = false
	Preferences.auto_animations = original_auto
	Preferences.last_run_id = original_last_run
	Preferences.save()
	I18n.set_locale(original_locale)
	GameAudio.shutdown()
	print("SHOWCASE_UI_SMOKE failures=", failures.size())
	get_tree().create_timer(.15).timeout.connect(func(): get_tree().process_frame.connect(
		func(): get_tree().quit(0 if failures.is_empty() else 1), CONNECT_ONE_SHOT), CONNECT_ONE_SHOT)
