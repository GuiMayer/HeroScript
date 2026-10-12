extends VBoxContainer
## Read-only collection, not a second playable hand.
signal card_requested(id: String)
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var model: Dictionary = {}
var cards := {}
var _search := LineEdit.new()
var _sort := OptionButton.new()
var _scroll := ScrollContainer.new()
var _grid := GridContainer.new()
var _message: Label
func _init(context = null) -> void:
	visual_context = context if context != null else ContextType.new()
	theme = visual_context.theme; add_theme_constant_override("separation", 12)
	var tools := HBoxContainer.new()
	_search.placeholder_text = visual_context.translator.text("Search cards"); _search.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_search.text_changed.connect(func(_value): _filter()); tools.add_child(_search)
	_sort.add_item(visual_context.translator.text("Published order")); _sort.add_item(visual_context.translator.text("By name"))
	_sort.item_selected.connect(func(_index): _filter()); tools.add_child(_sort); add_child(tools)
	_message = visual_context.styles.muted("", 16); add_child(_message)
	_scroll.custom_minimum_size = Vector2(600, 430); _scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED; _scroll.follow_focus = true
	_grid.add_theme_constant_override("h_separation", 16); _grid.add_theme_constant_override("v_separation", 16)
	_scroll.add_child(_grid); add_child(_scroll); _scroll.resized.connect(_columns)
func apply_model(data: Dictionary) -> void:
	model = data.duplicate(true)
	_search.editable = bool(model.visible)
	_sort.set_item_disabled(0, not bool(model.ordered)); _sort.select(0 if model.ordered else 1)
	_message.text = visual_context.translator.text("Contents hidden") if not model.visible else (visual_context.translator.text("Empty") if model.count == 0 else visual_context.translator.text("Published order" if model.ordered else "Order hidden — sorted catalog"))
	var live: Array = []
	for item in model.cards:
		var id := str(item.cardInstanceId); live.append(id)
		var card = cards.get(id)
		if card == null:
			card = CardView.new(visual_context); card.name = "CollectionCard_" + id.validate_node_name(); card.animate_entry = false
			card.pressed.connect(func(): card_requested.emit(str(card.model.cardInstanceId)))
			_grid.add_child(card); cards[id] = card
		card.configure(item); card.disabled = false
	for id in cards.keys():
		if id not in live: _grid.remove_child(cards[id]); cards[id].queue_free(); cards.erase(id)
	_filter(); _columns.call_deferred()
	visual_context.styles.apply_view_preferences(self)
func _columns() -> void:
	var width: float = visual_context.card_style.effective_size(visual_context.text_scale).x
	_grid.columns = maxi(1, floori((_scroll.size.x - 20.) / (width + 16.)))
func _filter() -> void:
	var query := _search.text.strip_edges().to_lower()
	var order: Array = model.get("cards", []).duplicate()
	if _sort.selected == 1: order.sort_custom(func(a, b): return str(a.name).to_lower() + str(a.cardInstanceId) < str(b.name).to_lower() + str(b.cardInstanceId))
	for index in order.size():
		var item: Dictionary = order[index]; var card: Control = cards[str(item.cardInstanceId)]
		_grid.move_child(card, index)
		card.visible = query.is_empty() or (str(item.name) + " " + " ".join(item.get("collectionTags", []))).to_lower().contains(query)
func capture_local_state() -> Dictionary: return {"query": _search.text, "sort": _sort.selected, "scroll": _scroll.scroll_vertical}
func restore_local_state(state: Dictionary) -> void:
	_search.text = str(state.get("query", ""))
	_sort.select(1 if not model.get("ordered", false) else int(state.get("sort", 0)))
	_filter(); _scroll.set_deferred("scroll_vertical", int(state.get("scroll", 0)))
