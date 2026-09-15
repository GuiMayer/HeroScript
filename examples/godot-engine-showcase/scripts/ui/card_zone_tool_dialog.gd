extends ConfirmationDialog
## A generic zone-flow form. The engine owns validity and transaction semantics.

const Zones = preload("res://scripts/presentation/card_zone_presenter.gd")
signal flow_requested(payload: Dictionary)

var _flows: Array = []
var _selector: OptionButton
var _summary: Label
var _definitions: LineEdit
var _instances: LineEdit
var _actor: LineEdit

func setup(flows: Array, selected_instance_id := "") -> void:
	_flows = flows.duplicate(true)
	title = I18n.text("Card zone tools")
	ok_button_text = I18n.text("Run flow")
	min_size = Vector2i(600, 320)
	var form := VBoxContainer.new()
	form.add_theme_constant_override("separation", 10)
	add_child(form)
	form.add_child(_label("Configured flow"))
	_selector = OptionButton.new()
	_selector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for flow in _flows:
		_selector.add_item(str(flow.get("flowId", "")))
	form.add_child(_selector)
	_summary = _label("")
	_summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	form.add_child(_summary)
	form.add_child(_label("Card definition IDs (comma-separated)"))
	_definitions = LineEdit.new()
	_definitions.placeholder_text = "basic_attack, defend"
	form.add_child(_definitions)
	form.add_child(_label("Card instance IDs (comma-separated)"))
	_instances = LineEdit.new()
	_instances.text = selected_instance_id
	form.add_child(_instances)
	form.add_child(_label("Actor ID (optional)"))
	_actor = LineEdit.new()
	form.add_child(_actor)
	_selector.item_selected.connect(func(_index): _show_flow())
	confirmed.connect(_submit)
	confirmed.connect(queue_free)
	canceled.connect(queue_free)
	_show_flow()

func _show_flow() -> void:
	if _flows.is_empty(): return
	var flow: Dictionary = _flows[_selector.selected]
	var steps: Array = flow.get("steps", [])
	var routes: Array[String] = []
	var needs_definition := false
	var needs_instance := false
	for step in steps:
		if not step is Dictionary: continue
		var operation := str(step.get("operation", ""))
		var source := str(step.get("sourceZoneId", ""))
		var target := str(step.get("targetZoneId", ""))
		routes.append("%s: %s → %s" % [operation, source if not source.is_empty() else "∅", target if not target.is_empty() else "∅"])
		needs_definition = needs_definition or (operation == "Create" and str(step.get("cardDefinitionId", "")) == "$input")
		needs_instance = needs_instance or (operation == "Move" and str(step.get("selection", {}).get("strategy", "")) == "Explicit")
	_summary.text = "\n".join(routes)
	_definitions.visible = needs_definition
	_definitions.get_parent().get_child(_definitions.get_index() - 1).visible = needs_definition
	_instances.visible = needs_instance
	_instances.get_parent().get_child(_instances.get_index() - 1).visible = needs_instance

func _submit() -> void:
	if _flows.is_empty(): return
	flow_requested.emit(Zones.tool_payload(_flows[_selector.selected], _definitions.text, _instances.text, _actor.text))

func _label(value: String) -> Label:
	var label := Label.new()
	label.text = I18n.text(value)
	return label
