extends ConfirmationDialog
## Passive intent editor. Only a version-matched server preview enables Play.
signal preview_requested(command: Dictionary, generation: int)
signal play_requested(preview: Dictionary)
var visual_context
var appearance := {}
var actor_names := {}
var command := {}
var snapshot := {}
var _generation := 0
var _accepted := -1
var _updating := false
var _chosen := {}
var _groups := {}
var _cards := {}
var _alive := true

func _ready() -> void:
	get_ok_button().disabled = true
	confirmed.connect(func():
		if _accepted == _generation and snapshot.get("isLegal", false): play_requested.emit(snapshot.duplicate(true))
		_dismiss())
	canceled.connect(_dismiss)
	get_node("Content/Choices/Cost").item_selected.connect(func(index):
		if _updating: return
		command.erase("costOptionId"); _chosen.clear()
		var id = get_node("Content/Choices/Cost").get_item_metadata(index)
		if id != null: command["costOptionId"] = str(id)
		refresh_preview())
	get_node("Content/Choices/Targets").multi_selected.connect(func(_index, _selected):
		if _updating: return
		var targets: ItemList = get_node("Content/Choices/Targets"); command["targetIds"] = []
		for index in targets.get_selected_items(): command.targetIds.append(str(targets.get_item_metadata(index)))
		refresh_preview())

func mount(context, styles := {}) -> void:
	visual_context = context; appearance = styles.duplicate(true); theme = context.theme
	title = context.translator.text("Choose payment"); get_ok_button().text = context.translator.text("Play card")
	get_cancel_button().text = context.translator.text("Cancel")

func intent() -> Dictionary:
	var result := command.duplicate(true); result["paymentSelections"] = []
	for requirement in snapshot.get("evaluation", {}).get("paymentRequirements", []):
		if requirement.get("costOptionId") != null and requirement.costOptionId != result.get("costOptionId"): continue
		var key := _key(requirement); var ids: Array = _chosen.get(key, [])
		if not ids.is_empty(): result.paymentSelections.append({"componentId": requirement.componentId, "paymentId": requirement.paymentId, "cardInstanceIds": ids.duplicate()})
	return result

func refresh_preview() -> void:
	if not _alive: return
	_generation += 1; get_ok_button().disabled = true
	get_node("Content/Feedback").text = visual_context.translator.text("Validating payment…")
	preview_requested.emit(intent(), _generation)

func accept(response: Dictionary, generation: int) -> void:
	if not _alive or generation != _generation: return
	if not response.get("ok", false) or not response.get("data") is Dictionary:
		get_ok_button().disabled = true; get_node("Content/Feedback").text = str(response.get("error", "Payment preview unavailable.")); return
	snapshot = response.data.duplicate(true); _accepted = generation; _render()
	get_ok_button().disabled = not snapshot.get("isLegal", false)
	var reasons: Array = snapshot.get("evaluation", {}).get("failureReasons", [])
	get_node("Content/Feedback").text = visual_context.translator.text("Ready to play") if snapshot.get("isLegal", false) else "\n".join(reasons.map(func(reason): return visual_context.translator.text("Choose the required cards.") if str(reason).begins_with("PAYMENT_SELECTION_REQUIRED:") else visual_context.translator.text(str(reason))))
	var outcomes: Array[String] = []
	for application in snapshot.get("applications", []):
		if application.get("previousValue") != null and application.get("currentValue") != null:
			outcomes.append("%s · %s: %s → %s" % [actor_names.get(application.get("targetEntityId", ""), application.get("targetEntityId", "")), visual_context.translator.content_name(str(application.get("resourceId", ""))), visual_context.translator.number(application.previousValue), visual_context.translator.number(application.currentValue)])
	get_node("Content/Result").text = "" if outcomes.is_empty() else visual_context.translator.text("Possible outcome" if snapshot.get("outcomeUncertain", false) else "Preview") + "\n" + "\n".join(outcomes.slice(0, 6))

func _key(requirement: Dictionary) -> String: return str(requirement.componentId) + ":" + str(requirement.paymentId)
func _render() -> void:
	_updating = true
	var evaluation: Dictionary = snapshot.get("evaluation", {})
	var costs: OptionButton = get_node("Content/Choices/Cost"); costs.clear()
	costs.add_item(visual_context.translator.text("Choose cost option")); costs.set_item_metadata(0, null)
	for option in evaluation.get("affordableCostOptionIds", []):
		costs.add_item(visual_context.translator.content_name(str(option))); costs.set_item_metadata(costs.item_count - 1, str(option))
		if option == command.get("costOptionId"): costs.select(costs.item_count - 1)
	costs.visible = costs.item_count > 1
	var targets: ItemList = get_node("Content/Choices/Targets"); targets.clear()
	for id in evaluation.get("legalTargetIds", []):
		targets.add_item(str(actor_names.get(id, id))); targets.set_item_metadata(targets.item_count - 1, str(id))
		if id in command.get("targetIds", []): targets.select(targets.item_count - 1, false)
	targets.visible = targets.item_count > 0
	var presenter = preload("res://scripts/presentation/card_facts_presenter.gd").new(visual_context.translator, appearance)
	var facts := {}
	for fact in snapshot.get("cards", []): facts[str(fact.get("identity", {}).get("cardInstanceId", ""))] = fact
	var resolving_id := str(command.get("cardInstanceId", ""))
	if facts.has(resolving_id): _face("resolving", resolving_id, facts[resolving_id], get_node("Content/Columns/Resolving"), presenter)
	for group in _groups.values(): group.hide()
	for requirement in evaluation.get("paymentRequirements", []):
		if requirement.get("costOptionId") != null and requirement.costOptionId != command.get("costOptionId"): continue
		var key := _key(requirement)
		var group = _groups.get(key)
		if not is_instance_valid(group):
			group = preload("res://scenes/ui/components/card_payment_group.tscn").instantiate()
			get_node("Content/Columns/Scroll/Groups").add_child(group); _groups[key] = group
		group.show()
		group.get_node("Title").text = "%s · %s · %s/%s" % [visual_context.translator.content_name(str(requirement.paymentId)), visual_context.translator.content_name(str(requirement.sourceZoneId)), _chosen.get(key, []).size(), int(requirement.count)]
		for child in group.get_node("Cards").get_children(): child.hide()
		for id in requirement.get("eligibleCardIds", []):
			if not facts.has(str(id)): continue
			var card = _face(key, str(id), facts[str(id)], group.get_node("Cards"), presenter)
			card.show(); card.select_card(id in _chosen.get(key, []))
			if not card.has_meta("payment_bound"):
				card.set_meta("payment_bound", true)
				card.pressed.connect(func():
					var selected: Array = _chosen.get(key, []).duplicate()
					if id in selected: selected.erase(id)
					else: selected.append(id)
					_chosen[key] = selected; card.select_card(id in selected); refresh_preview())
	_updating = false

func _face(key: String, id: String, fact: Dictionary, parent: Node, presenter):
	var identity := key + ":" + id; var card = _cards.get(identity)
	if not is_instance_valid(card):
		card = preload("res://scripts/ui/card_view.gd").create(visual_context); card.animate_entry = false
		parent.add_child(card); _cards[identity] = card
	card.configure(presenter.project(fact)); return card

func cancel_selection() -> void:
	if _alive: canceled.emit()
func _dismiss() -> void:
	_alive = false; _generation += 1; _chosen.clear(); queue_free()
func _exit_tree() -> void: _alive = false; _generation += 1
