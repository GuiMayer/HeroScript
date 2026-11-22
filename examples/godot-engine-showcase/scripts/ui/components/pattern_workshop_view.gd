@tool
extends VBoxContainer
## Workshop screen: node structure is authored in pattern_workshop.tscn.
signal purchase_requested(reference: String)
signal apply_requested(reference: String)
signal preview_requested(reference: String)
signal resolve_requested
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

const OfferScene = preload("res://scenes/ui/components/pattern_workshop_offer.tscn")
var _bound_offers := 0

func _ensure_offer_slots(count: int) -> Array:
	var offers_host := get_node("ContentScroll/Content/Offers")
	var target_count := maxi(3, count)
	while offers_host.get_child_count() < target_count:
		var slot := OfferScene.instantiate()
		slot.name = "Offer%d" % (offers_host.get_child_count() + 1)
		offers_host.add_child(slot)
	for i in range(_bound_offers, offers_host.get_child_count()):
		var index := i
		offers_host.get_child(i).get_node("Column/Acquire").pressed.connect(func(): _confirm_purchase(index))
	_bound_offers = offers_host.get_child_count()
	return offers_host.get_children()

func bind() -> void:
	if _bound: return
	_ensure_offer_slots(3)
	get_node("Footer/Manage").pressed.connect(_toggle_inventory)
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
		get_node("LeaveConfirm").hide()
		get_node("Compare").hide()
	_run = run.duplicate(true); _choices = choices.duplicate(true)
	var was_completed := _completed
	_completed = str(_run.get("currentNodeId", "")) in _run.get("completedActivityNodeIds", [])
	if _completed and not was_completed and item_snapshot.get("items", []).any(func(entry): return str(entry.get("instance", {}).get("state", "")) == "Available"):
		_inventory_open = true
	_offers = _choices.filter(func(choice): return str(choice.get("type", "")) == "ACQUIRE_CARD_MODIFIER_ITEM")
	if _selected >= _offers.size(): _selected = -1
	var t = context.translator
	var forge := _is_alisyum()
	get_node("Header/Title").text = presenter.node_name(str(run.get("currentNodeId", ""))) if forge else t.text("Pattern workshop")
	var parts: Dictionary = _run.get("resources", {}).get("parts", {})
	get_node("Balance").text = (t.text("Available Parts: %s") % t.number(float(parts.current))) if parts.has("current") else t.text("Parts balance unavailable")
	if forge and parts.has("current"):
		get_node("Balance").text = t.text("Available %s: %s") % [t.content_name("parts"), t.number(float(parts.current))]
	if not _offers.is_empty():
		get_node("Notice").text = t.text("Choose a pattern to modify a compatible card. Buying a pattern does not change a card until you apply it.")
		if forge: get_node("Notice").text = t.text("Buy a card rune, then choose a compatible card. You can apply it now or later.")
	elif _completed:
		get_node("Notice").text = t.text("This workshop is complete. You can apply an acquired pattern now or later.")
	else:
		get_node("Notice").text = t.text("Workshop offers are unavailable. Check the connection.")
	var slots := _ensure_offer_slots(_offers.size())
	for i in slots.size():
		var offer: Control = slots[i]
		offer.visible = i < _offers.size() and not _inventory_open
		if i >= _offers.size(): continue
		var model: Dictionary = presenter.offer(_offers[i])
		offer.get_node("Column/Rune").texture = preload("res://scripts/ui/components/rune_drag.gd").icon(str(_offers[i].get("subjectId", "")))
		offer.get_node("Column/Title").text = str(model.name)
		offer.get_node("Column/Description").text = str(model.summary) if str(model.category) == "modifier_items" else t.text("Effect description unavailable. Inspect a compatible card before applying.")
		offer.get_node("Column/Cost").text = (t.text("Cost: %s") % str(model.cost)) if not str(model.cost).is_empty() else t.text("Cost unavailable")
		offer.get_node("Column/Acquire").text = t.text("Buy card rune" if forge else "Acquire pattern")
	get_node("ContentScroll/Content/Offers").visible = not _inventory_open and not _offers.is_empty()
	var acquired = get_node("ContentScroll/Content/Acquired")
	acquired.visible = _completed and not _inventory_open
	if acquired.visible:
		var chosen: Dictionary = {}
		for entry in item_snapshot.get("items", []):
			var instance: Dictionary = entry.get("instance", {})
			if str(instance.get("acquisitionSource", "")) != "node:" + str(_run.get("currentNodeId", "")): continue
			if chosen.is_empty() or int(instance.get("acquisitionSequence", -1)) > int(chosen.get("instance", {}).get("acquisitionSequence", -1)):
				chosen = entry
		if chosen.is_empty():
			acquired.get_node("Column/Title").text = t.text("Acquired pattern details unavailable")
			acquired.get_node("Column/Description").text = ""
			acquired.get_node("Column/Status").text = t.text("Open your modifiers to check the inventory.")
		else:
			var definition: Dictionary = chosen.get("definition", {})
			var id := str(definition.get("itemDefinitionId", ""))
			var name: String = t.content_name(id, str(definition.get("presentation", {}).get("name", id)))
			acquired.get_node("Column/Title").text = t.text("Acquired: %s") % name
			acquired.get_node("Column/Description").text = preload("res://scripts/presentation/activity_presenter.gd").modifier_description(definition, t)
			var state := str(chosen.get("instance", {}).get("state", ""))
			acquired.get_node("Column/Status").text = t.text("This pattern has been applied to a card.") if state == "Bound" else (t.text("Not applied yet. Choose a compatible card, now or later.") if state == "Available" else t.text("Check this pattern in your inventory."))
	var inventory = get_node("ContentScroll/Content/Inventory")
	inventory.visible = _inventory_open
	if _inventory_open: inventory.configure(item_snapshot, choices, facts, appearance, context, _editable)
	get_node("Footer/Manage").text = t.text("Back to workshop" if _completed else "Back to patterns") if _inventory_open else t.text("Apply now" if _completed else "Manage card modifiers")
	get_node("Footer/Manage").theme_type_variation = &"PrimaryAction" if _completed and not _inventory_open else &"SecondaryAction"
	get_node("Footer/Continue").text = t.text("Apply later and continue") if _completed else t.text("Leave workshop")
	get_node("Footer/Continue").disabled = not _editable or not _choices.any(func(choice): return str(choice.get("type", "")) == "RESOLVE_NODE")
	_refresh_offers()
	context.styles.apply_view_preferences(self)

func _refresh_offers() -> void:
	var label: Label = get_node("Footer/Selection")
	label.text = context.translator.text("Choose a card rune." if _is_alisyum() else "Choose a pattern to acquire.") if not _offers.is_empty() and not _inventory_open else ""
	var parameters: Dictionary = presenter.current_node().get("activity", {}).get("parameters", {})
	var slots := _ensure_offer_slots(_offers.size())
	for i in slots.size():
		var offer: PanelContainer = slots[i]
		if i >= _offers.size(): continue
		var price_known: bool = _offers[i].has("costs") or parameters.has("costs")
		var costs: Array = _offers[i].get("costs", parameters.get("costs", []))
		var balance_known := true
		var can_afford := true
		for cost in costs:
			var pool = _run.get("resources", {}).get(str(cost.get("resourceId", "")), {})
			if not pool is Dictionary or not pool.has("current"): balance_known = false; continue
			if float(pool.current) < float(cost.get("amount", 0)): can_afford = false
		var model: Dictionary = presenter.offer(_offers[i])
		var cost_text: String = (context.translator.text("Cost: %s") % str(model.cost)) if not str(model.cost).is_empty() else context.translator.text("Free" if price_known and costs.is_empty() else "Cost unavailable")
		if price_known and balance_known and not can_afford: cost_text += " · " + context.translator.text("Insufficient resources")
		elif price_known and not balance_known: cost_text += " · " + context.translator.text("Balance unavailable; affordability cannot be verified.")
		offer.get_node("Column/Cost").text = cost_text
		offer.get_node("Column/Acquire").disabled = not _editable or not price_known or (balance_known and not can_afford)

func _toggle_inventory() -> void:
	_inventory_open = not _inventory_open
	present(_run, _choices, presenter, context, _items, _facts, _appearance, _editable)
	if _inventory_open:
		get_node("ContentScroll/Content/Inventory/ItemsColumn/Items").grab_focus()
	else:
		get_node("Footer/Manage").grab_focus()

func _is_alisyum() -> bool:
	return str(_run.get("settingId", "")) == "alisyum"
var _items: Dictionary = {}
var _facts: Array = []
var _appearance: Dictionary = {}
func update_inventory(items: Dictionary, facts: Array, appearance: Dictionary) -> void:
	_items = items.duplicate(true); _facts = facts.duplicate(true); _appearance = appearance.duplicate(true)

func _confirm_purchase(index: int) -> void:
	if index < 0 or index >= _offers.size(): return
	if get_node("ContentScroll/Content/Offers/Offer%d/Column/Acquire" % (index + 1)).disabled: return
	_selected = index
	var model: Dictionary = presenter.offer(_offers[index])
	var dialog: ConfirmationDialog = get_node("PurchaseConfirm")
	dialog.title = context.translator.text("Confirm choice")
	dialog.ok_button_text = context.translator.text("Buy card rune" if _is_alisyum() else "Confirm purchase")
	dialog.cancel_button_text = context.translator.text("CANCEL")
	dialog.dialog_text = str(model.name) + "\n" + str(model.summary) + ("\n" + context.translator.text("Cost: %s") % str(model.cost) if not str(model.cost).is_empty() else "") + "\n" + get_node("Balance").text
	if _is_alisyum(): dialog.dialog_text += "\n\n" + context.translator.text("The rune goes to your card modifiers inventory until applied.")
	dialog.popup_centered_clamped(Vector2i(500, 260), 0.9)

func _confirm_leave() -> void:
	if get_node("Footer/Continue").disabled: return
	var dialog: ConfirmationDialog = get_node("LeaveConfirm")
	dialog.title = context.translator.text("Leave workshop")
	dialog.ok_button_text = context.translator.text("CONTINUE" if _completed else "Leave workshop")
	dialog.cancel_button_text = context.translator.text("CANCEL")
	dialog.dialog_text = context.translator.text("Continue from the workshop?") if _completed else context.translator.text("Leave without buying a pattern?")
	dialog.popup_centered_clamped(Vector2i(440, 220), 0.9)

func show_comparison(reference: String, models: Array) -> void:
	_pending_apply = reference
	var dialog: ConfirmationDialog = get_node("Compare")
	dialog.title = context.translator.text("Confirm choice")
	dialog.ok_button_text = context.translator.text("Bind rune" if _is_alisyum() else "Apply pattern")
	dialog.cancel_button_text = context.translator.text("CANCEL")
	var content = dialog.get_node("Content"); content.show()
	var inventory = get_node("ContentScroll/Content/Inventory")
	content.get_node("Description").text = context.translator.text("Pattern: %s · Card: %s\nReview the changes before confirming.") % [inventory.selected_pattern_name, inventory.selected_card_name]
	content.get_node("Description").text += "\n" + context.translator.text("This binding is permanent.")
	var changes: Array[String] = []
	if str(models[0].get("summary", "")) != str(models[1].get("summary", "")): changes.append(context.translator.text("effects"))
	if models[0].get("costs", []) != models[1].get("costs", []): changes.append(context.translator.text("costs"))
	if not changes.is_empty(): content.get_node("Description").text += "\n" + (context.translator.text("Changes shown: %s") % ", ".join(changes))
	for i in 2:
		var column = content.get_node("Scroll/Comparison/" + ("Before" if i == 0 else "After"))
		column.get_node("Title").text = context.translator.text("Before" if i == 0 else "After")
		var card = column.get_node("Card"); card.visual_context = context; card.configure(models[i])
	dialog.popup_centered_clamped(Vector2i(700, 680), 0.92)
