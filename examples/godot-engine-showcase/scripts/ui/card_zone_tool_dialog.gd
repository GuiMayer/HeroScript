class_name CardZoneToolDialogView
extends ConfirmationDialog
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
## A generic zone-flow form. The engine owns validity and transaction semantics.

const Zones = preload("res://scripts/presentation/card_zone_presenter.gd")
signal flow_requested(payload: Dictionary)

var _flows: Array = []
var _selector: OptionButton
var _summary: Label
var _definitions: LineEdit
var _instances: LineEdit
var _actor: LineEdit

static func create() -> CardZoneToolDialogView:
	return (load("res://scenes/ui/overlays/card_zone_tools.tscn") as PackedScene).instantiate()
func setup(flows: Array, selected_instance_id := "") -> void:
	theme = visual_context.theme; _flows = flows.duplicate(true)
	title = visual_context.translator.text("Card zone tools")
	ok_button_text = visual_context.translator.text("Run flow")
	_selector = get_node("%FlowSelector"); _summary = get_node("%FlowSummary")
	_definitions = get_node("%Definitions"); _instances = get_node("%Instances"); _actor = get_node("%Actor")
	for child in get_node("Form").get_children():
		if child.has_meta("translation_key"): child.text = visual_context.translator.text(str(child.get_meta("translation_key")))
	_selector.clear()
	for flow in _flows: _selector.add_item(str(flow.get("flowId","")))
	_instances.text = selected_instance_id
	if not _selector.item_selected.is_connected(_on_flow): _selector.item_selected.connect(_on_flow)
	if not confirmed.is_connected(_submit): confirmed.connect(_submit)
	if not confirmed.is_connected(queue_free): confirmed.connect(queue_free)
	if not canceled.is_connected(queue_free): canceled.connect(queue_free)
	_show_flow()
func _on_flow(_index: int) -> void: _show_flow()
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
