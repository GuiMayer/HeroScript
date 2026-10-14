extends MenuButton
## Data-driven recursive menus. Selection never starts playback or changes a run.
signal animation_selected(id: String)
var selected_id := ""
var _catalog: Dictionary = {}
var _scenarios: Dictionary = {}
var _entries: Dictionary = {}
var _serial := 0
var _translator

func setup(catalog: Dictionary, scenarios: Array, translator) -> void:
	_catalog = catalog.duplicate(true); _translator = translator
	_scenarios.clear()
	for scenario in scenarios: _scenarios[str(scenario.id)] = scenario.duplicate(true)
	name = "LabAnimationPicker"
	flat = false
	custom_minimum_size = Vector2(340, 40)
	text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	refresh_labels()

func refresh_labels() -> void:
	var previous := selected_id
	get_popup().hide(); get_popup().clear(true)
	_entries.clear(); _serial = 0
	_build_menu(get_popup(), _catalog.get("categories", []), [])
	disabled = _entries.is_empty()
	var preferred: String = previous if _entries.has(previous) else str(_catalog.get("defaultAnimationId", ""))
	if not _entries.has(preferred): preferred = str(_entries.keys()[0]) if not _entries.is_empty() else ""
	select_animation(preferred)

func _build_menu(popup: PopupMenu, items: Array, ancestors: Array) -> void:
	if not popup.id_pressed.is_connected(_on_item_pressed): popup.id_pressed.connect(_on_item_pressed)
	for item in items:
		if not item is Dictionary: continue
		var label := str(item.get("label", ""))
		var path: Array = ancestors + [label]
		if item.get("items") is Array:
			var group_id := _serial; _serial += 1
			var child := PopupMenu.new()
			child.name = "AnimationGroup_%s" % group_id
			popup.add_submenu_node_item(_translator.text(label), child, group_id)
			_build_menu(child, item.items, path)
			continue
		var id := str(item.get("id", ""))
		var scenario: Dictionary = _scenarios.get(str(item.get("scenarioId", "")), item.get("scenario", {}))
		if id.is_empty() or _entries.has(id) or scenario.is_empty(): continue
		var menu_id := _serial; _serial += 1
		popup.add_radio_check_item(_translator.text(label), menu_id)
		_entries[id] = {"scenario": scenario.duplicate(true), "path": path, "popup": popup, "menu_id": menu_id}

func _on_item_pressed(menu_id: int) -> void:
	for id in _entries:
		if _entries[id].menu_id == menu_id: select_animation(str(id)); return

func select_animation(id: String) -> bool:
	if not _entries.has(id): return false
	var changed := selected_id != id
	selected_id = id
	for key in _entries:
		var entry: Dictionary = _entries[key]
		entry.popup.set_item_checked(entry.popup.get_item_index(entry.menu_id), key == id)
	text = " › ".join(_entries[id].path.map(func(key): return _translator.text(str(key))))
	tooltip_text = text
	if changed: animation_selected.emit(id)
	return true

func selected_scenario() -> Dictionary:
	return _entries[selected_id].scenario.duplicate(true) if _entries.has(selected_id) else {}
