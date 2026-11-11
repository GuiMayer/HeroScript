extends SceneTree
var failures := 0
class Session extends "res://tests/fixtures/unified_loadout_session.gd":
	func _init() -> void: run.settingId = "prototype-test"
	func snapshot() -> Dictionary:
		var value := super.snapshot()
		value.policy = {"capacityOnly": true, "capacity": 6, "slots": [], "equipmentWindows": ["OutsideCombat"]}
		for i in range(24):
			value.items.append({"instance": {"instanceId": "item_%d" % i}, "definition": {"inscriptionId": "focus", "equipmentCost": 3, "presentation": {"name": "Inscription %d" % i}}})
		return JSON.parse_string(JSON.stringify(value))
func _init() -> void: run.call_deferred()
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func settle() -> void:
	for i in 20: await process_frame
func run() -> void:
	var prefs = root.get_node("Preferences")
	var original_scale: float = prefs.text_scale
	var translator = root.get_node("I18n")
	var original_locale: String = translator.locale
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		for language in ["en", "pt_BR"]:
			for scale in [1.0, 1.2, 1.6]:
				root.size = resolution; root.content_scale_size = resolution
				translator.set_locale(language); prefs.text_scale = scale
				var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate()
				screen.session = Session.new(); screen.initial_tab = "inscriptions"; root.add_child(screen)
				await settle()
				var view = screen.inscriptions_view
				var bounds: Rect2 = screen.shell.content.get_global_rect()
				var label := "%s %s scale=%s" % [resolution, language, scale]
				var slots: Rect2 = view.slot_grid.get_global_rect()
				check(bounds.encloses(view.get_global_rect()), "complete inscription view fits the fixed panel: " + label)
				check(bounds.encloses(slots), "all common slots fit in the fixed panel: " + label)
				check(bounds.encloses(view.find_child("CharacterIdentity", true, false).get_global_rect()), "identity slot fits in the fixed panel: " + label)
				check(bounds.encloses(view.inventory_scroll.get_global_rect()), "inventory viewport fits the panel: " + label)
				check(view.inventory_scroll.get_v_scroll_bar().max_value > view.inventory_scroll.get_v_scroll_bar().page, "inventory can scroll with many items: " + label)
				var before: Vector2 = view.slot_grid.global_position
				view.inventory_scroll.scroll_vertical = 400; await settle()
				check(view.inventory_scroll.scroll_vertical > 0 and view.slot_grid.global_position == before, "scrolling inventory keeps slots fixed: " + label)
				screen._select_item("owned"); await settle()
				check(bounds.encloses(view.get_global_rect()) and bounds.encloses(view.slot_grid.get_global_rect()) and bounds.encloses(view.inventory_scroll.get_global_rect()), "selection leaves the layout within the panel: " + label)
				var details_button: Button = view.find_child("ShowInscriptionDetails", true, false)
				check(details_button.visible, "selected inscription offers full details: " + label)
				details_button.pressed.emit(); await settle()
				var dialog: AcceptDialog = view.find_child("@AcceptDialog@*", false, false)
				check(dialog != null and dialog.visible and dialog.dialog_text == view.details.text, "details dialog preserves full inscription text: " + label)
				if dialog != null: dialog.queue_free(); await settle()
				check(preload("res://tests/layout_inspector.gd").vertical_text_issues(screen).is_empty(), "text is not vertically compressed: " + label)
				screen.queue_free(); await settle()
	prefs.text_scale = original_scale; translator.set_locale(original_locale)
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
