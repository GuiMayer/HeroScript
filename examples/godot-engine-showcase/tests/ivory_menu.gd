extends SceneTree
## Actual main-menu route, isolated preference directory, no real HTTP or runs.
var failures := 0
var output_dir := ""
var captures: Array[String] = []
const Layout = preload("res://tests/layout_inspector.gd")
const Focus = preload("res://scripts/ui/focus_navigation.gd")
class FixtureGateway extends RefCounted:
	var calls := 0
	func run_history(_player: String, _setting: String) -> Dictionary:
		calls += 1
		return {"ok": true, "data": {"items": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 12: await process_frame
func capture(router, state: String, language: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	var name := "%s-%s-%sx%s.png" % [state, language, root.size.x, root.size.y]
	check(root.get_texture().get_image().save_png(output_dir.path_join(name)) == OK, "saved " + name)
	captures.append(name)
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	root.content_scale_size = Vector2i(1440, 900)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var previous_profile := AppTheme.profile
	var saved_file := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var saved := {"gateway": session._gateway, "settings": session._settings, "selected": session.selected_setting_id,
		"available": session.available, "scale": prefs.text_scale, "contrast": prefs.high_contrast,
		"motion": prefs.reduced_motion, "locale": i18n.locale, "index": prefs.resume_index, "last": prefs.last_run_id}
	var gateway := FixtureGateway.new()
	session.configure(gateway)
	session.available = true
	session._settings = [
		{"settingId": "ascendant", "displayName": "Ascendant Matrix", "description": "World one: an energy-only branching act with persistent health, card transformations, five build families and a two-state boss.", "launch": {"playerEntityId": "player"}},
		{"settingId": "default", "displayName": "Ember Archive"},
		{"settingId": "volatile", "displayName": "Volatile Crucible"}]
	session.selected_setting_id = "ascendant"
	prefs.resume_index = load("res://scripts/application/run_resume_index.gd").new()
	prefs.last_run_id = ""
	prefs.reduced_motion = true
	prefs.high_contrast = false
	var run_snapshot := JSON.stringify([session.run, session.combat, session.legal_actions])
	var real_requests: int = root.get_node("GameServices").diagnostics().size()
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	check(AppTheme.profile == router.MenuProfile and router.get_node("MenuPaper").visible and not router.shell.run_scenery.background.visible and not router.shell.run_scenery.background.is_processing(), "main menu opts into static ivory paper; old particles stop")
	var view = router.host.get_child(0)
	check(view.name == "MainMenuView" and view.setting_selector.item_count == 3 and str(view.setting_selector.get_item_metadata(view.setting_selector.selected)) == "ascendant", "passive menu preserves all published settings and active selection")
	check(view.continue_button.disabled and router.continuation_run_id.is_empty(), "empty selected-setting history keeps Continue disabled")
	var actions: Array[String] = []
	var sample = load("res://scripts/ui/main_menu_view.gd").new()
	sample.mount(router.visual_context)
	sample.apply_model(view.model)
	sample.action_requested.connect(func(action): actions.append(action))
	for pair in [["NewJourneyButton", "new"], ["ContinueButton", "continue"], ["HistoryButton", "history"], ["RulesLabButton", "sandbox"], ["CodexButton", "codex"], ["SettingsButton", "settings"], ["QuitButton", "quit"]]:
		sample.find_child(pair[0], true, false).pressed.emit()
		check(actions[-1] == pair[1], "passive action still maps to the canonical router intent: " + pair[1])
	sample.apply_model(view.model)
	check(sample.find_children("NewJourneyButton", "", true, false).size() == 1, "reapplying a menu model never duplicates composition or actions")
	sample.free()
	for resolution in [Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution
		if DisplayServer.get_name() != "headless": DisplayServer.window_set_size(resolution)
		for language in ["en", "pt_BR"]:
			i18n.set_locale(language)
			for text_scale in [1.0, 1.2]:
				prefs.text_scale = text_scale
				router._apply_preferences()
				await settle()
				view = router.host.get_child(0)
				var viewport := root.get_visible_rect()
				var controls_inside := true
				for control in Focus.controls(view):
					controls_inside = controls_inside and viewport.encloses(control.get_global_rect())
				check(Layout.vertical_text_issues(view).is_empty() and controls_inside and view.find_child("MenuScroll", true, false).get_v_scroll_bar().max_value <= view.find_child("MenuScroll", true, false).get_v_scroll_bar().page + 1, "menu fits with readable controls and no required scroll: %s / %s / %s" % [resolution, language, text_scale])
				if text_scale == (1.2 if language == "pt_BR" else 1.0): await capture(router, "menu", language)
	var retained = router.host.get_child(0)
	var reads := gateway.calls
	for _repeat in 5: router._apply_preferences()
	await settle()
	check(retained == router.host.get_child(0) and gateway.calls == reads and retained.find_child("SettingTitle", true, false).get_theme_font_size("font_size") == roundi(retained.visual_style.title_size * prefs.text_scale), "preferences apply without rebuilding, extra reads or accumulating font scale")
	var start: Button = retained.find_child("NewJourneyButton", true, false)
	check(start.theme_type_variation == "PrimaryAction" and start.get_theme_color("font_color") == AppTheme.color("text.inverse"), "primary play action uses deliberate inverse accent")
	var source := FileAccess.get_file_as_string("res://scripts/ui/main_menu_view.gd")
	check(not source.contains("GameSession.") and not source.contains("Preferences.") and not source.contains("I18n.") and not source.contains("HTTPRequest"), "menu view has no session, preferences, global translator or transport authority")
	var compact = load("res://scripts/ui/main_menu_view.gd").new()
	compact.mount(router.visual_context)
	compact.apply_model(retained.model)
	compact.theme = router.theme
	compact.size = Vector2(820, 820)
	root.add_child(compact)
	await settle()
	check(compact.find_child("MenuBody", true, false).vertical and Layout.vertical_text_issues(compact).is_empty() and compact.find_child("MenuScroll", true, false).follow_focus, "compact layout stacks columns and keeps focus-following scroll for short windows")
	compact.queue_free()
	await settle()
	session._settings[0].displayName = "A Deliberately Long Setting Name With Several Additional Words From A User Mod"
	router._show_main_menu()
	await settle()
	retained = router.host.get_child(0)
	check(Layout.vertical_text_issues(retained).is_empty() and retained.setting_selector.get_global_rect().end.x <= root.get_visible_rect().end.x and retained.find_child("SettingTitle", true, false).max_lines_visible == 2, "unknown long setting names stay bounded and preserve full metadata/details")
	await capture(router, "long-setting", "pt_BR")
	session._settings[0].displayName = "Ascendant Matrix"
	router._show_main_menu()
	await settle()
	retained = router.host.get_child(0)
	prefs.high_contrast = true
	router._apply_preferences()
	await settle()
	check(retained.get_node("MenuCircuit0").simplified and retained.get_node("MenuCircuit1").simplified, "high contrast removes passive circuits")
	await capture(router, "high-contrast", "pt_BR")
	prefs.high_contrast = false
	router._apply_preferences()
	session.available = false
	router._show_main_menu()
	await settle()
	check(router.host.find_child("SettingSelector", true, false).disabled and router.host.find_child("HistoryButton", true, false).disabled, "offline setting/history state remains honest")
	router.show_error("Offline fixture — no engine request was made.")
	check(router.toast.get_theme_stylebox("normal").bg_color == AppTheme.color("surface.toast"), "menu notices inherit paper instead of a stale dark background")
	await capture(router, "offline", "pt_BR")
	router.host.find_child("SettingsButton", true, false).pressed.emit()
	await settle()
	check(router.current_screen == "settings" and AppTheme.profile == router.MenuProfile and router.get_node("MenuPaper").visible and not router.shell.run_scenery.background.visible, "actual Settings navigation keeps the ivory menu profile")
	router.back_to_menu()
	await settle()
	check(AppTheme.profile == router.MenuProfile and router.host.find_child("SettingSelector", true, false) != null, "return to menu restores Marfim without losing the selector")
	check(run_snapshot == JSON.stringify([session.run, session.combat, session.legal_actions]) and real_requests == root.get_node("GameServices").diagnostics().size(), "theme/navigation changes no gameplay state and makes no real HTTP calls")
	router.queue_free()
	await settle()
	check(AppTheme.profile == previous_profile, "destroying the menu router restores its entry profile")
	check(saved_file == (FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""), "visual tests never write player preferences")
	session.configure(saved.gateway); session._settings = saved.settings; session.selected_setting_id = saved.selected; session.available = saved.available
	prefs.text_scale = saved.scale; prefs.high_contrast = saved.contrast; prefs.reduced_motion = saved.motion
	prefs.resume_index = saved.index; prefs.last_run_id = saved.last
	i18n.set_locale(saved.locale)
	AppTheme.configure_preferences(prefs.text_scale, prefs.high_contrast)
	if not output_dir.is_empty():
		var report := FileAccess.open(output_dir.path_join("captures.json"), FileAccess.WRITE)
		if report: report.store_string(JSON.stringify({"fixture": true, "captures": captures, "failures": failures, "rendered": DisplayServer.get_name() != "headless", "godot": Engine.get_version_info().string}, "\t") + "\n"); report.close()
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
