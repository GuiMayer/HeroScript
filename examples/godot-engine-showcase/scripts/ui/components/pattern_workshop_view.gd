@tool
extends VBoxContainer
## Workshop screen: node structure is authored in pattern_workshop.tscn.
signal purchase_requested(reference: String)
signal apply_requested(reference: String)
signal preview_requested(reference: String)
signal resolve_requested
signal map_requested
var context
var presenter
var _run: Dictionary = {}
var _choices: Array = []
var _offers: Array = []
var _selected := -1
var _inventory_open := false
var _completed := false
var _editable := true
var _pending_apply := ""
var _bound := false

func bind() -> void:
	if _bound: return
	for i in 3:
		var index := i
		get_node("ContentScroll/Content/Offers/Offer%d/Column/Select" % (i + 1)).pressed.connect(func(): _select(index))
	get_node("Header/Map").pressed.connect(func(): map_requested.emit())
	get_node("Footer/Manage").pressed.connect(_toggle_inventory)
	get_node("Footer/Purchase").pressed.connect(_confirm_purchase)
	get_node("Footer/Continue").pressed.connect(_confirm_leave)
	get_node("PurchaseConfirm").confirmed.connect(func():
		if _selected >= 0 and _selected < _offers.size(): purchase_requested.emit(presenter.option_reference(_offers[_selected])))
	get_node("LeaveConfirm").confirmed.connect(func(): resolve_requested.emit())
	get_node("Compare").confirmed.connect(func():
		if not _pending_apply.is_empty(): apply_requested.emit(_pending_apply))
	get_node("ContentScroll/Content/Inventory").choice_requested.connect(func(reference): preview_requested.emit(reference))
	_bound = true

func present(run: Dictionary, choices: Array, projection, visual_context, item_snapshot: Dictionary, facts: Array, appearance: Dictionary, editable := true) -> void:
	bind(); context = visual_context; theme = context.theme; presenter = projection; _editable = editable
	if _choices != choices or _run.get("sequence") != run.get("sequence") or _run.get("runId") != run.get("runId"):
		_selected = -1
		_pending_apply = ""
		get_node("PurchaseConfirm").hide()
		get_node("Compare").hide()
	_run = run.duplicate(true); _choices = choices.duplicate(true)
	_completed = str(_run.get("currentNodeId", "")) in _run.get("completedActivityNodeIds", [])
	_offers = _choices.filter(func(choice): return str(choice.get("type", "")) == "ACQUIRE_CARD_MODIFIER_ITEM")
	if _selected >= _offers.size(): _selected = -1
	var t = context.translator
	get_node("Header/Title").text = t.text("Pattern workshop")
	get_node("Header/Map").text = t.text("MAP")
	var parts: Dictionary = _run.get("resources", {}).get("parts", {})
	get_node("Balance").text = (t.text("Available Parts: %s") % t.number(float(parts.current))) if parts.has("current") else t.text("Parts balance unavailable")
	if not _offers.is_empty():
		get_node("Notice").text = t.text("Choose a pattern to modify a compatible card. Buying a pattern does not change a card until you apply it.")
	elif _completed:
		get_node("Notice").text = t.text("This workshop is complete. You can apply an acquired pattern now or later.")
	else:
		get_node("Notice").text = t.text("Workshop offers are unavailable. Check the connection or return to the map.")
	for i in 3:
		var offer = get_node("ContentScroll/Content/Offers/Offer%d" % (i + 1))
		offer.visible = i < _offers.size() and not _inventory_open
		if i >= _offers.size(): continue
		var model: Dictionary = presenter.offer(_offers[i])
		offer.get_node("Column/Title").text = str(model.name)
		offer.get_node("Column/Description").text = str(model.summary) if str(model.category) == "modifier_items" else t.text("Effect description unavailable. Inspect a compatible card before applying.")
		offer.get_node("Column/Cost").text = (t.text("Cost: %s") % str(model.cost)) if not str(model.cost).is_empty() else t.text("Cost unavailable")
		offer.get_node("Column/Select").text = t.text("Selected") if i == _selected else t.text("Select")
	get_node("ContentScroll/Content/Offers").visible = not _inventory_open and not _offers.is_empty()
	var inventory = get_node("ContentScroll/Content/Inventory")
	inventory.visible = _inventory_open
	if _inventory_open: inventory.configure(item_snapshot, choices, facts, appearance, context)
	get_node("Footer/Manage").text = t.text("Back to workshop" if _completed else "Back to patterns") if _inventory_open else t.text("Apply now" if _completed else "Manage card modifiers")
	get_node("Footer/Purchase").text = t.text("Acquire pattern")
	get_node("Footer/Purchase").visible = not _inventory_open and not _offers.is_empty()
	get_node("Footer/Continue").text = t.text("Apply later and continue") if _completed else t.text("Leave workshop")
	get_node("Footer/Continue").disabled = not _editable or not _choices.any(func(choice): return str(choice.get("type", "")) == "RESOLVE_NODE")
	_refresh_selection()
	context.styles.apply_view_preferences(self)

func _select(index: int) -> void:
	_selected = index
	for i in 3:
		get_node("ContentScroll/Content/Offers/Offer%d/Column/Select" % (i + 1)).text = context.translator.text("Selected" if i == index else "Select")
	_refresh_selection()

func _refresh_selection() -> void:
	var label: Label = get_node("Footer/Selection")
	var button: Button = get_node("Footer/Purchase")
	if _selected < 0 or _selected >= _offers.size():
		label.text = context.translator.text("Select a pattern to see its cost.") if not _offers.is_empty() else ""
		button.disabled = true; return
	var model: Dictionary = presenter.offer(_offers[_selected])
	var can_afford := true
	var known := true
	var costs: Array = _offers[_selected].get("costs", presenter.current_node().get("activity", {}).get("parameters", {}).get("costs", []))
	for cost in costs:
		var pool = _run.get("resources", {}).get(str(cost.get("resourceId", "")), {})
		if not pool is Dictionary or not pool.has("current"): known = false; continue
		if float(pool.current) < float(cost.get("amount", 0)): can_afford = false
	label.text = context.translator.text("Insufficient resources") if known and not can_afford else str(model.name) + (" · " + str(model.cost) if not str(model.cost).is_empty() else "")
	button.disabled = not _editable or (known and not can_afford)

func _toggle_inventory() -> void:
	_inventory_open = not _inventory_open
	present(_run, _choices, presenter, context, _items, _facts, _appearance, _editable)
var _items: Dictionary = {}
var _facts: Array = []
var _appearance: Dictionary = {}
func update_inventory(items: Dictionary, facts: Array, appearance: Dictionary) -> void:
	_items = items.duplicate(true); _facts = facts.duplicate(true); _appearance = appearance.duplicate(true)

func _confirm_purchase() -> void:
	if _selected < 0 or _selected >= _offers.size(): return
	var model: Dictionary = presenter.offer(_offers[_selected])
	var dialog: ConfirmationDialog = get_node("PurchaseConfirm")
	dialog.title = context.translator.text("Confirm choice")
	dialog.dialog_text = str(model.name) + "\n" + str(model.summary) + ("\n" + str(model.cost) if not str(model.cost).is_empty() else "")
	dialog.popup_centered_clamped(Vector2i(500, 260), 0.9)

func _confirm_leave() -> void:
	var dialog: ConfirmationDialog = get_node("LeaveConfirm")
	dialog.title = context.translator.text("Leave workshop")
	dialog.dialog_text = context.translator.text("Continue from the workshop?") if _completed else context.translator.text("Leave without buying a pattern?")
	dialog.popup_centered_clamped(Vector2i(440, 220), 0.9)

func show_comparison(reference: String, models: Array) -> void:
	_pending_apply = reference
	var dialog: ConfirmationDialog = get_node("Compare")
	dialog.title = context.translator.text("Confirm choice")
	var content = dialog.get_node("Content"); content.show()
	content.get_node("Description").text = context.translator.text("Apply this pattern to the selected card?")
	for i in 2:
		var column = content.get_node("Scroll/Comparison/" + ("Before" if i == 0 else "After"))
		column.get_node("Title").text = context.translator.text("Before" if i == 0 else "After")
		var card = column.get_node("Card"); card.visual_context = context; card.configure(models[i])
	dialog.popup_centered_clamped(Vector2i(700, 680), 0.92)
