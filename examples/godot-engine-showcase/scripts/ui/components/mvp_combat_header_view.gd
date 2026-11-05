@tool
extends Control
## Three independent visual regions; receives published display data only.
signal log_requested
signal inspection_requested
signal pause_requested
const ContextType = preload("res://scripts/ui/visual_context.gd")
const Chrome = preload("res://scripts/ui/combat_chrome.gd")
@export var preview_model: Dictionary = {"actorName": "Ember Adept", "actorDefinitionId": "spire_adept", "round": "ROUND 1", "turn": "Turn: Ember Adept", "support": "", "supportHint": "", "log": "Combat log", "pause": "PAUSE [Escape]", "inspect": "Inspect card [V]"}
var visual_context = ContextType.new()
var actor_name_label: Label
var round_label: Label
var active_turn_label: Label
var support_label: Label
var portrait: TextureRect
var log_button: Button
var inspect_button: Button
var pause_button: Button
var _model: Dictionary = {}
var _context_key := ""
func _bind_nodes() -> void:
	if is_instance_valid(round_label): return
	actor_name_label = get_node("%ActorName")
	round_label = get_node("%Round")
	active_turn_label = get_node("%ActiveTurn")
	support_label = get_node("%SupportStatus")
	portrait = get_node("%Portrait")
	log_button = get_node("%LogButton")
	inspect_button = get_node("%InspectButton")
	pause_button = get_node("%PauseButton")
	for path in ["ActiveIdentity", "RoundAndTurn", "HeaderActions"]:
		get_node(path).minimum_size_changed.connect(update_minimum_size)
func _get_minimum_size() -> Vector2:
	var height := 0.0
	for path in ["ActiveIdentity", "RoundAndTurn", "HeaderActions"]:
		var region: Control = get_node_or_null(path)
		if region != null: height = maxf(height, region.get_combined_minimum_size().y)
	return Vector2(0, height)
func _ready() -> void:
	_bind_nodes()
	if _model.is_empty() and (Engine.is_editor_hint() or get_tree().current_scene == self):
		apply_model(preview_model)
func apply_model(model: Dictionary, context = null) -> void:
	_bind_nodes()
	if context != null: visual_context = context
	var context_key: String = visual_context.presentation_key()
	if _model == model and _context_key == context_key: return
	_model = model.duplicate(true); _context_key = context_key
	theme = visual_context.theme
	actor_name_label.text = str(model.get("actorName", ""))
	actor_name_label.tooltip_text = actor_name_label.text
	round_label.text = str(model.get("round", "")); round_label.tooltip_text = round_label.text
	active_turn_label.text = str(model.get("turn", "")); active_turn_label.tooltip_text = active_turn_label.text
	active_turn_label.visible = not active_turn_label.text.is_empty()
	support_label.text = str(model.get("support", "")); support_label.tooltip_text = str(model.get("supportHint", ""))
	support_label.visible = not support_label.text.is_empty()
	portrait.rebind_context(visual_context)
	portrait.setup("actors", str(model.get("actorDefinitionId", "")))
	log_button.text = str(model.get("log", "")); log_button.tooltip_text = log_button.text; log_button.accessibility_name = log_button.text
	Chrome.apply(inspect_button, "inspect", str(model.get("inspect", "")), visual_context, false, true)
	Chrome.apply(pause_button, "pause", str(model.get("pause", "")), visual_context)
	visual_context.styles.apply_view_preferences(self)
	update_minimum_size()
func set_inspection_available(value: bool) -> void:
	_bind_nodes(); inspect_button.disabled = not value
func presentation_model() -> Dictionary: return _model.duplicate(true)
func _on_log_pressed() -> void: log_requested.emit()
func _on_inspect_pressed() -> void:
	if not inspect_button.disabled: inspection_requested.emit()
func _on_pause_pressed() -> void: pause_requested.emit()
