extends VBoxContainer

const KINDS := ["cards", "entities", "resources", "status-effects", "relics", "modifiers", "modes", "combat-rules", "runs"]

var router
var list: ItemList
var detail: TextEdit
var current_items: Array = []

func setup(owner) -> void:
	router = owner
	add_theme_constant_override("separation", 16)
	var header := HBoxContainer.new()
	var titles := VBoxContainer.new()
	titles.add_child(AppTheme.title("CÓDICE DA ENGINE", 34))
	titles.add_child(AppTheme.muted("Conteúdo publicado e versionado, lido pela mesma REST API usada durante o jogo."))
	header.add_child(titles)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button("MENU", router.back_to_menu, 120))
	add_child(header)
	var tabs := HBoxContainer.new()
	tabs.add_theme_constant_override("separation", 8)
	for kind in KINDS:
		tabs.add_child(_button(kind.to_upper(), func(): _load_kind(kind)))
	add_child(tabs)
	var columns := HSplitContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(columns)
	list = ItemList.new()
	list.custom_minimum_size.x = 310
	list.item_selected.connect(_select_item)
	columns.add_child(list)
	detail = TextEdit.new()
	detail.editable = false
	detail.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	detail.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	columns.add_child(detail)
	call_deferred("_load_kind", "cards")

func _load_kind(kind: String) -> void:
	detail.text = "Carregando %s…" % kind
	var response := await GameSession.content(kind, 200)
	if not response.ok:
		detail.text = response.error
		return
	current_items = response.data.get("items", [])
	list.clear()
	for item in current_items:
		list.add_item(str(item.get("definitionId", "sem id")))
	if not current_items.is_empty():
		list.select(0)
		_select_item(0)

func _select_item(index: int) -> void:
	if index < 0 or index >= current_items.size():
		return
	detail.text = JSON.stringify(current_items[index], "  ")

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.custom_minimum_size.y = 42
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
