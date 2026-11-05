@tool
extends "res://scripts/ui/screen_view.gd"

const KINDS := ["cards", "entities", "resources", "status-effects", "relics", "modifiers", "modes", "combat-rules", "runs"]
const ScreenContext = preload("res://scripts/ui/context/screen_context.gd")

var request_epoch := 0
var router
var screen_context
var list: ItemList
var detail: TextEdit
var current_items: Array = []
var filtered_items: Array = []
var selected_kind := "cards"

var _bound := false
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		mount(ContextType.new()); theme = visual_context.theme
func _bind_nodes() -> void:
	detail = get_node("Columns/DetailScroll/DetailBody/Detail")
	list = get_node("Columns/List")
	move_child(get_node("Search"), 2)
func _ctx():
	if screen_context == null:
		screen_context = ScreenContext.ensure(router)
	return screen_context
func setup(owner) -> void:
	screen_context = ScreenContext.ensure(owner)
	router = screen_context.router if screen_context.router != null else owner
	mount(screen_context.visual_context)
	_bind_nodes(); theme = visual_context.theme
	var tr = screen_context.i18n
	get_node("Header/Menu").text = tr.text("MENU")
	get_node("Search").placeholder_text = tr.text("Search content")
	get_node("RawToggle").text = tr.text("Show raw JSON")
	get_node("Header/Titles/Title").text = tr.text("ENGINE CODEX")
	get_node("Header/Titles/Description").text = tr.text("Browse the published content used by the game.")
	if not _bound:
		_bound = true
		get_node("Search").text_changed.connect(func(_value): _filter_items())
		get_node("RawToggle").toggled.connect(func(value): detail.visible = value; get_node("Columns/DetailScroll/DetailBody/FormattedDetail").visible = not value)
		get_node("Header/Menu").pressed.connect(func():
			var c = _ctx()
			c.feedback.ui()
			c.navigation.back_to_menu())
		list.item_selected.connect(_select_item)
	var tabs = get_node("TabsScroll/Tabs")
	if tabs.get_child_count() == 0:
		var names := ["Cards", "Entities", "Resources", "Statuses", "Relics", "Modifiers", "Modes", "Rules", "Runs"]
		for kind in KINDS:
			var button: Button = preload("res://scenes/ui/components/codex_tab.tscn").instantiate()
			button.toggle_mode = true
			button.set_meta("kind", kind)
			button.text = tr.text(names[KINDS.find(kind)]).to_upper()
			button.pressed.connect(func():
				_ctx().feedback.ui()
				_load_kind(kind))
			tabs.add_child(button)
	call_deferred("_load_kind", "cards")

func _load_kind(kind: String) -> void:
	selected_kind = kind
	var c = _ctx()
	var tr = c.i18n
	for tab in get_node("TabsScroll/Tabs").get_children(): tab.set_pressed_no_signal(tab.get_meta("kind", "") == kind)
	request_epoch += 1
	var epoch := request_epoch
	detail.text = tr.text("Loading %s…") % kind
	var response: Dictionary = await c.session.content(kind, 200)
	if not is_inside_tree() or epoch != request_epoch:
		return
	if not response.ok:
		detail.text = str(response.get("error", tr.error(response)))
		return
	current_items = response.data.get("items", [])
	_filter_items()
func _filter_items() -> void:
	var query := str(get_node("Search").text).to_lower()
	filtered_items = current_items.filter(func(item): return query.is_empty() or JSON.stringify(item).to_lower().contains(query))
	list.clear()
	for item in filtered_items: list.add_item(str(item.get("definitionId", "")))
	get_node("FoundCount").text = str(filtered_items.size())
	detail.text = ""; get_node("Columns/DetailScroll/DetailBody/FormattedDetail").text = ""; get_node("Columns/DetailScroll/DetailBody/CardPreview").hide()
	if not filtered_items.is_empty(): list.select(0); _select_item(0)
func _select_item(index: int) -> void:
	if index < 0 or index >= filtered_items.size(): return
	var item: Dictionary = filtered_items[index]
	detail.text = JSON.stringify(item, "  ")
	var preview = get_node("Columns/DetailScroll/DetailBody/CardPreview")
	preview.visible = selected_kind == "cards"
	if preview.visible:
		var definition: Dictionary = item.get("definition", {})
		preview.visual_context = visual_context
		preview.animate_entry = false
		preview.configure({"definitionId": str(item.get("definitionId", "")), "name": visual_context.translator.content_name(str(item.get("definitionId", ""))), "costsKnown": false, "costs": [], "effects": [str(definition.get("description", ""))], "summary": visual_context.translator.text("Definition preview — inspect published components for rules."), "tone": "skill"})
		preview.disabled = true

	var lines: Array[String] = []
	for key in item:
		var value = item[key]
		lines.append(str(key).capitalize() + ": " + (JSON.stringify(value, "  ") if value is Dictionary or value is Array else str(value)))
	get_node("Columns/DetailScroll/DetailBody/FormattedDetail").text = "\n\n".join(lines)
	detail.visible = get_node("RawToggle").button_pressed
	get_node("Columns/DetailScroll/DetailBody/FormattedDetail").visible = not detail.visible

func dispose() -> void:
	request_epoch += 1

func _unhandled_key_input(event: InputEvent) -> void:
	if Engine.is_editor_hint() or not is_visible_in_tree() or not event.is_action_pressed("ui_cancel") or event.is_echo(): return
	var viewport := get_viewport()
	if viewport == null or get_tree().root.find_children("*", "Window", true, false).any(func(window): return window.visible and window.exclusive): return
	var back := get_node_or_null("Header/Menu") as Button
	if is_instance_valid(back) and back.is_visible_in_tree() and not back.disabled:
		viewport.set_input_as_handled()
		back.pressed.emit()
