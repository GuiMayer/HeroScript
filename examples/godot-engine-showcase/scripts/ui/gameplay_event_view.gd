extends VBoxContainer
## Reused by timeline and isolated laboratory. Consumes only authorized, committed data.
const Presenter = preload("res://scripts/presentation/gameplay_event_presenter.gd")
const Messages = preload("res://scripts/presentation/gameplay_event_messages.gd")
var tree: Tree
var detail: RichTextLabel
var _rows: Array = []
var _locale := "en"

func _ready() -> void:
	_build()

func _build() -> void:
	if is_instance_valid(tree): return
	tree = Tree.new()
	tree.hide_root = true
	tree.custom_minimum_size.y = 140
	tree.size_flags_vertical = Control.SIZE_EXPAND_FILL
	tree.item_selected.connect(_select)
	add_child(tree)
	detail = RichTextLabel.new()
	detail.bbcode_enabled = false
	detail.custom_minimum_size.y = 120
	add_child(detail)

func present(batch: Dictionary, locale := "en", names: Dictionary = {}) -> void:
	_build()
	_locale = locale
	_rows = Presenter.rows(batch, locale, names)
	tree.clear()
	var root := tree.create_item()
	var by_id: Dictionary = {}
	for row in _rows:
		var item := tree.create_item(by_id.get(row.parent, root))
		item.set_text(0, row.message)
		item.set_metadata(0, row)
		by_id[row.id] = item
	detail.text = Messages.word("empty", locale) if _rows.is_empty() else Messages.word("causes", locale)

func _select() -> void:
	var item := tree.get_selected()
	if item != null: detail.text = Presenter.details(item.get_metadata(0), _locale)
