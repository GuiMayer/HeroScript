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
	check(screen.presenter.pile_cards("drawPileInstanceIds").size() == GameSession.run.deck.drawPileInstanceIds.size(), "pile inspector uses canonical instance identities")
	check(candidate.has("costs"), "costs come from the canonical legal action")
	screen._choose_card(card_id)
	check(screen.selected_card == card_id and router.host.get_child(0) == screen, "selection keeps the current combat screen")
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
	if targets.is_empty():
		screen._offer_candidates([candidate])
	else:
		screen._select_target(str(targets[0]))
	# Wait for the actual REST transaction and acceptance animation.
	for _attempt in 600:
		await get_tree().create_timer(.025).timeout
		if router.host.get_child(0) != screen:
			break
	check(router.host.get_child(0) != screen, "card input submits and refreshes the screen")
	screen = router.host.get_child(0)
	check(not Playback.frames.is_empty(), "receipt exposes actual animation frames")
	check(Playback.index == 0, "manual animation mode waits for the player")
	check(screen._locked(), "new commands blocked until animations finish")
	router.toggle_pause()
	check(not router.host.can_process(), "pause suspends gameplay nodes")
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
