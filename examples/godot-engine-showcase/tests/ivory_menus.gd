extends SceneTree
## Real menu/pause compositions with read-only fixtures. No HTTP or game commands.
var failures := 0
var output_dir := ""
var captures: Array[String] = []
const Layout = preload("res://tests/layout_inspector.gd")
class Gateway extends RefCounted:
	func replay_context() -> Array: return ["visual-fixture", 0]
	func begin_run_timeline(id: String, cursor: int, limit: int, _through: int):
		var ticket = preload("res://scripts/engine/read_ticket.gd").new()
		var response: Dictionary = run_timeline(id, cursor, limit)
		response.data.merge({"runId": id, "throughSequence": 1, "nextCursor": 1, "hasMore": false, "historicalInspectionAllowed": false}, true)
		ticket.finish(response); return ticket
	var reads := 0
	func content(kind: String, _revision: String, _setting: String, _limit: int) -> Dictionary:
		reads += 1
		return {"ok": true, "data": {"items": [{"definitionId": "example_" + kind, "components": [{"type": "effect", "resourceId": "custom", "amount": 5}]}]}}
	func run_history(_player: String, setting: String) -> Dictionary:
		reads += 1
		return {"ok": true, "data": {"items": [{"runId": "visual-record", "settingId": setting, "sequence": 1, "lifecycle": "Completed", "modeId": "story", "seed": 424242, "currentNodeId": "boss"}]}}
	func run_timeline(_id: String, _cursor: int, _limit: int) -> Dictionary:
		reads += 1
		return {"ok": true, "data": {"items": [{"sequence": 1, "commandType": "START_ENCOUNTER", "step": 1, "stateHash": "visual-fixture-not-engine-hash", "frames": [{"scope": "combat", "kind": "activation.started"}]}], "nextCursor": 0}}
	func run_commit(_id: String, _sequence: int) -> Dictionary:
		reads += 1
		return {"ok": true, "data": {"stateAfter": {"lifecycle": "Completed", "currentNodeId": "boss", "deck": {"cardInstances": {}}, "resourceState": {"resources": {"gold": {"current": 40, "maximum": 100}}}}}}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		reads += 1
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 10: await process_frame
func capture(name: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	var file := "%s-%sx%s.png" % [name, root.size.x, root.size.y]
	check(root.get_texture().get_image().save_png(output_dir.path_join(file)) == OK, "saved " + file)
	captures.append(file)
func show_menu(router, route: String) -> void:
	match route:
		"settings": router.show_settings()
		"history": router.show_history()
		"codex": router.show_codex()
		"sandbox": router.show_sandbox()
		"replay": router.show_run_replay("visual-record", true)
func combat(session) -> void:
	session._run = {"runId": "visual-run", "sequence": 1, "playerEntityId": "hero", "lifecycle": "Active"}
	session._combat = {"combatId": "visual-combat", "activeActorId": "hero", "actors": [
		{"instanceId": "hero", "definitionId": "spire_adept", "sideId": "player", "resources": {"health": {"current": 48, "maximum": 72}, "energy": {"current": 3, "maximum": 3}}, "statuses": []},
		{"instanceId": "wisp", "definitionId": "spire_wisp", "sideId": "enemy", "resources": {"health": {"current": 24, "maximum": 28}}, "statuses": []}]}
	session._card_zones = {"zones": [{"zoneId": "hand", "allowsCardPlay": true, "contentsVisible": true, "count": 2, "cards": [
		{"cardInstanceId": "one", "definitionId": "basic_attack"}, {"cardInstanceId": "two", "definitionId": "defend"}]}]}
	session._legal_actions = [{"source": "Card", "command": {"actionType": "PLAY_CARD", "cardInstanceId": "one", "targetIds": ["wisp"]}, "costs": [{"resourceId": "energy", "amount": 1}]},
		{"source": "Card", "command": {"actionType": "PLAY_CARD", "cardInstanceId": "two", "targetIds": ["hero"]}, "costs": [{"resourceId": "energy", "amount": 1}]}]
	session._available_commands = [{"type": "ABANDON_RUN", "validPayload": {"reason": "test"}}]
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	root.content_scale_size = Vector2i(1440, 900)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	root.gui_embed_subwindows = true
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var entry_profile := AppTheme.profile
	var original: Resource = load("res://data/themes/ivory_run.tres")
	var saved_file := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var requests: int = root.get_node("GameServices").diagnostics().size()
	prefs.reduced_motion = true
	session.configure(Gateway.new())
	session.available = false
	session._settings = [{"settingId": "default", "displayName": "Ember Archive"}]
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		root.size = resolution
		if DisplayServer.get_name() != "headless": DisplayServer.window_set_size(resolution)
		for language in ["en", "pt_BR"]:
			router.back_to_menu()
			i18n.set_locale(language)
			prefs.text_scale = 1.2 if language == "pt_BR" else 1.0
			prefs.high_contrast = false
			router._apply_preferences()
			await settle()
			for route in ["settings", "history", "codex", "sandbox", "replay"]:
				show_menu(router, route)
				await settle()
				check(AppTheme.profile == router.MenuProfile and router.get_node("MenuPaper").visible and not router.shell.run_scenery.background.is_processing() and Layout.vertical_text_issues(router.host).is_empty(), "ivory/readable menu %s / %s / %s" % [route, resolution, language])
				if route == "settings":
					var settings = router.host.get_child(0)
					for category in ["general", "audio", "display", "accessibility", "controls", "connection"]:
						settings._show_category(category)
						await settle()
						check(Layout.vertical_text_issues(settings).is_empty() and settings.page_content.get_global_rect().end.x <= root.get_visible_rect().end.x + 1, "settings category fits %s / %s / %s" % [category, resolution, language])
						if category in ["audio", "controls"]: await capture("settings-" + category + "-" + language)
					settings._show_category("general")
					await settle()
				await capture(route + "-" + language)
			combat(session)
			router.show_combat()
			await settle()
			var screen = router.host.get_child(0)
			var card: CardView = screen.view.hand.card_for("one")
			var card_theme := card.get_theme_stylebox("normal")
			var card_count: int = card.configuration_count
			var snapshot := JSON.stringify([session.run, session.combat, session.legal_actions])
			var reads: int = session._gateway.reads
			var generation := AppTheme.generation
			check(AppTheme.profile == original and card.visual_style.printed_face, "combat uses the independent ivory run profile and printed cards")
			router.toggle_pause()
			await settle()
			check(AppTheme.profile == original and AppTheme.generation == generation and card.get_theme_stylebox("normal") == card_theme and card.configuration_count == card_count and session._gateway.reads == reads, "pause styling cannot recolor/rebuild gameplay or trigger engine reads")
			check(router.pause_layer.theme.default_font == router.MenuProfile.body_font and router.shell.pause_context.profile == router.MenuProfile and router.visual_context.profile == original, "pause carries an independent presentation context and native theme")
			check(Layout.vertical_text_issues(router.pause_layer).is_empty() and router.pause_layer.is_ancestor_of(root.gui_get_focus_owner()) and not router.host.can_process(), "ivory pause remains readable and modal")
			await capture("pause-" + language)
			var abandon: Button = router.pause_layer.find_child("AbandonRunButton", true, false)
			check(abandon != null, "canonical abandon choice is still present in pause")
			abandon.pressed.emit()
			await settle()
			var confirmation: ConfirmationDialog = router.pause_layer.get_children().filter(func(child): return child is ConfirmationDialog)[0]
			check(confirmation.theme == router.pause_layer.theme, "abandon confirmation inherits the ivory modal theme")
			confirmation.hide(); confirmation.queue_free()
			prefs.high_contrast = true
			router._apply_preferences()
			await settle()
			check(router.pause_layer.find_child("PauseCircuit", true, false).simplified and AppTheme.profile == original, "accessible pause refresh stays scoped to the overlay")
			await capture("pause-high-contrast-" + language)
			prefs.high_contrast = false
			router.toggle_pause()
			router._apply_preferences()
			await settle()
			check(snapshot == JSON.stringify([session.run, session.combat, session.legal_actions]) and not paused, "resume never changes gameplay records")
			router.show_settings(true)
			await settle()
			check(AppTheme.profile == router.MenuProfile, "settings opened from pause/gameplay also uses ivory")
			router.open_game()
			await settle()
			check(AppTheme.profile == original and router.current_screen == "combat", "return from settings restores the gameplay profile")
			router.show_run_replay("visual-record", false)
			await settle()
			check(AppTheme.profile == original, "replay opened from run result retains the run theme")
	router.queue_free()
	await settle()
	check(AppTheme.profile == entry_profile and requests == root.get_node("GameServices").diagnostics().size(), "menu suite leaves no palette change or real HTTP request")
	check(saved_file == (FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""), "visual suite never writes player preferences")
	if not output_dir.is_empty():
		var report := FileAccess.open(output_dir.path_join("captures.json"), FileAccess.WRITE)
		if report: report.store_string(JSON.stringify({"fixture": true, "captures": captures, "failures": failures, "godot": Engine.get_version_info().string}, "\t") + "\n"); report.close()
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
