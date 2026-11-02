extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print(("[PASS] " if ok else "[FAIL] ") + message)
	if not ok: failures += 1
func run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(null,{"reduced_motion":true})
	var view = load("res://scripts/ui/loadout/character_attributes_view.gd").create()
	view.get_node("%CharacterName").add_theme_font_size_override("font_size",23)
	view.mount(context)
	var model := {"name":"Hero","attributes":[{"id":"armor","label":"Armor","group":"Defense","format":"number","base":1,"value":5,"change":4,"sources":[]}]}
	var before := JSON.stringify(model)
	view.present(model)
	var table = view.get_node("%Table")
	var row = view._rows.armor.row
	table.add_theme_constant_override("separation",17)
	view.present(model)
	check(view._rows.armor.row == row and view.get_node("%Table") == table, "Attribute updates retain authored table and current rows")
	check(table.get_theme_constant("separation") == 17, "Attribute updates preserve inspector spacing")
	view.mount(context)
	check(view.get_node("%CharacterName").get_theme_font_size("font_size") == 23, "Repeated mounting preserves authored typography")
	check(view.get_node("%Table").get_child_count() == 3, "Repeated mounting does not duplicate headers or groups")
	check(JSON.stringify(model) == before, "Attribute view never mutates published data")
	var drawer = load("res://scripts/ui/components/combat_attributes_drawer.gd").create()
	drawer.get_theme_stylebox("panel").content_margin_left = 21
	drawer.mount(context)
	var shared = drawer.attributes_view
	drawer.present({"actors":[{"instanceId":"hero","definitionId":"Hero","resources":{"health":{"current":7,"maximum":12}}}],"run":{"playerEntityId":"hero"}},context)
	check(shared.scene_file_path == "res://scenes/ui/components/loadout/character_attributes_view.tscn", "Combat drawer uses the exact loadout scene")
	check(drawer.get_theme_stylebox("panel").content_margin_left == 21, "Data projection preserves the authored drawer padding")
	drawer.set_open(true); drawer.mount(context)
	check(drawer.expanded and drawer.visible and drawer.attributes_view == shared, "Repeated binding preserves the open drawer and shared component")
	check(drawer.toggle_button.pressed.get_connections().size() == 1, "Repeated binding never duplicates the close action")
	check(root.get_node("GameServices").diagnostics().is_empty(), "Attributes are passive projections without HTTP")
	drawer.free(); view.free()
	quit(1 if failures else 0)
