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

var _bound := false
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		mount(ContextType.new()); theme = visual_context.theme
func _bind_nodes() -> void:
	detail = get_node("Columns/Detail")
	list = get_node("Columns/List")
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
	get_node("Header/Titles/Title").text = tr.text("ENGINE CODEX")
	get_node("Header/Titles/Description").text = tr.text("Browse the published content used by the game.")
	if not _bound:
		_bound = true
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
			button.text = tr.text(names[KINDS.find(kind)]).to_upper()
			button.pressed.connect(func():
				_ctx().feedback.ui()
				_load_kind(kind))
			tabs.add_child(button)
	call_deferred("_load_kind", "cards")

func _load_kind(kind: String) -> void:
	var c = _ctx()
	var tr = c.i18n
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
	list.clear()
	for item in current_items:
		list.add_item(str(item.get("definitionId", tr.text("no ID"))))
	if not current_items.is_empty():
		list.select(0)
		_select_item(0)

func _select_item(index: int) -> void:
	if index < 0 or index >= current_items.size():
		return
	detail.text = JSON.stringify(current_items[index], "  ")


func dispose() -> void:
	request_epoch += 1
