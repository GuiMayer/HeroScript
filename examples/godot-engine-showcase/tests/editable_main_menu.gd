extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print(("[PASS] " if ok else "[FAIL] ") + message)
	if not ok: failures += 1
func settle() -> void:
	for frame in 6: await process_frame
func run() -> void:
	var router = load("res://main.tscn").instantiate()
	root.add_child(router); await settle()
	var menu = router.host.get_child(0)
	var selector = menu.setting_selector
	var composition = menu.get_node("%MenuComposition")
	var epoch: int = router.navigation_epoch
	router._show_main_menu(); await settle()
	check(router.host.get_child(0) == menu and menu.setting_selector == selector, "Menu refresh preserves its scene and setting selector")
	check(router.navigation_epoch == epoch + 1, "Retained menu still invalidates older asynchronous resume lookups")
	check(router.shell == router.get_node("%ApplicationShell") and router._menu_paper == router.get_node("%MenuPaper"), "Application root binds its authored shell and paper")
	var sample = load("res://scripts/ui/scene_registry.gd").instantiate("menu")
	sample.mount(router.visual_context)
	sample.apply_model(menu.model)
	var play = sample.get_node("%NewJourneyButton")
	var requests: Array = []
	sample.action_requested.connect(func(action): requests.append(action))
	composition.add_theme_constant_override("separation",31)
	sample.apply_model(menu.model); sample.apply_model(menu.model)
	play.pressed.emit()
	check(requests == ["new"] and sample.get_node("%NewJourneyButton") == play, "Repeated updates retain buttons and emit only one intent")
	check(menu.get_node("%MenuComposition") == composition and composition.get_theme_constant("separation") == 31, "Menu updates preserve the authored composition and spacing")
	check(sample.get_child_count() == 1, "Menu has one authored static composition")
	check(sample.scene_file_path == menu.scene_file_path, "Fixtures and production route use the exact same scene")
	check(root.get_node("GameServices").diagnostics().is_empty(), "Menu fixtures submit no real HTTP")
	sample.free(); router.free()
	quit(1 if failures else 0)
