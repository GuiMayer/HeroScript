@tool
class_name CardCollectionView
extends VBoxContainer
## Read-only collection, not a second playable hand.
signal card_requested(id: String)
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var model: Dictionary = {}
var cards := {}
var _search: LineEdit
var _sort: OptionButton
var _scroll: ScrollContainer
var _grid: GridContainer
var _message: Label
var _back: TextureRect
func _init(context = null) -> void:
	visual_context = context if context != null else ContextType.new()
static func create(context = null) -> CardCollectionView:
	var view = (load("res://scenes/ui/components/card_collection_view.tscn") as PackedScene).instantiate()
	if context != null: view.visual_context = context
	return view
func _bind_nodes() -> void:
	_search = get_node("%Search"); _sort = get_node("%Sort")
	_scroll = get_node("%CollectionScroll"); _grid = get_node("%Cards")
	_message = get_node("%Message"); _back = get_node("%HiddenContents")
	theme = visual_context.theme
	_search.placeholder_text = visual_context.translator.text("Search cards")
	if not _search.text_changed.is_connected(_on_search): _search.text_changed.connect(_on_search)
	if not _sort.item_selected.is_connected(_on_sort): _sort.item_selected.connect(_on_sort)
	if not _scroll.resized.is_connected(_columns): _scroll.resized.connect(_columns)
	if _sort.item_count == 0: _sort.add_item(""); _sort.add_item("")
	_sort.set_item_text(0,visual_context.translator.text("Published order"))
	_sort.set_item_text(1,visual_context.translator.text("By name"))
	_back.texture = preload("res://scripts/ui/combat_chrome.gd").texture("pile")
	_back.modulate = visual_context.styles.INK
func _on_search(_value: String) -> void: _filter()
func _on_sort(_index: int) -> void: _filter()
func apply_model(data: Dictionary) -> void:
	_bind_nodes()
	model = data.duplicate(true)
	_search.editable = bool(model.visible)
	_sort.disabled = not bool(model.visible); _scroll.visible = bool(model.visible)
	_back.visible = not bool(model.visible) and int(model.count) > 0
	_sort.set_item_disabled(0, not bool(model.ordered)); _sort.select(0 if model.ordered else 1)
	_message.text = visual_context.translator.text("Contents hidden") if not model.visible else (visual_context.translator.text("Empty") if model.count == 0 else visual_context.translator.text("Published order" if model.ordered else "Order hidden — sorted catalog"))
	var live: Array = []
	for item in model.cards:
		var id := str(item.cardInstanceId); live.append(id)
		var card = cards.get(id)
		if card == null:
			card = CardView.create(visual_context); card.name = "CollectionCard_" + id.validate_node_name(); card.animate_entry = false
			card.pressed.connect(func(): card_requested.emit(str(card.model.cardInstanceId)))
			_grid.add_child(card); cards[id] = card
		card.configure(item); card.disabled = false
	for id in cards.keys():
		if id not in live: _grid.remove_child(cards[id]); cards[id].queue_free(); cards.erase(id)
	_filter(); _columns(); _columns.call_deferred()
	visual_context.styles.apply_view_preferences(self)
func _columns() -> void:
	var width: float = visual_context.card_style.effective_size(visual_context.text_scale).x
	_grid.columns = maxi(1, floori((maxf(_scroll.size.x, _scroll.custom_minimum_size.x) - 20.) / (width + 16.)))
func _filter() -> void:
	var query := _search.text.strip_edges().to_lower()
	var order: Array = model.get("cards", []).duplicate()
	if _sort.selected == 1: order.sort_custom(func(a, b): return str(a.name).to_lower() + str(a.cardInstanceId) < str(b.name).to_lower() + str(b.cardInstanceId))
	for index in order.size():
		var item: Dictionary = order[index]; var card: Control = cards[str(item.cardInstanceId)]
		_grid.move_child(card, index)
		card.visible = query.is_empty() or (str(item.name) + " " + " ".join(item.get("collectionTags", []))).to_lower().contains(query)
func capture_local_state() -> Dictionary: return {"query": _search.text, "sort": _sort.selected, "scroll": _scroll.scroll_vertical}
func update_card(id: String, data: Dictionary) -> void:
	if not cards.has(id): return
	cards[id].configure(data)
	for index in model.cards.size():
		if str(model.cards[index].cardInstanceId) == id: model.cards[index] = data.duplicate(true); break
	_filter()
func restore_local_state(state: Dictionary) -> void:
	_search.text = str(state.get("query", ""))
	_sort.select(1 if not model.get("ordered", false) else int(state.get("sort", 0)))
	_filter(); _scroll.set_deferred("scroll_vertical", int(state.get("scroll", 0)))
