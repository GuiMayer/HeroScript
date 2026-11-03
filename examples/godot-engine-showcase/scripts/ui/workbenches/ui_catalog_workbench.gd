@tool
class_name UICatalogWorkbench
extends Control
## The editor owns all nodes and sample values. This adapter only populates them.
@export var visual_profile: Resource = preload("res://data/themes/ivory_run.tres"):
	set(value):
		visual_profile = value
		_queue_preview()
@export_range(0.8, 2.0, 0.1) var preview_text_scale := 1.0:
	set(value):
		preview_text_scale = value
		_queue_preview()
@export var preview_high_contrast := false:
	set(value):
		preview_high_contrast = value
		_queue_preview()
@export_enum("full", "empty", "error", "long_text") var preview_state_mode := "full":
	set(value):
		preview_state_mode = value
		_queue_preview()
var _refresh_queued := false
func _ready() -> void: _populate_workbench()
func _queue_preview() -> void:
	if not is_node_ready() or _refresh_queued: return
	_refresh_queued = true
	_populate_workbench.call_deferred()
func apply_fixture_mode(mode: String) -> void:
	preview_state_mode = mode
	_populate_workbench()
func _populate_workbench() -> void:
	_refresh_queued = false
	var context = preload("res://scripts/ui/visual_context.gd").new(null,
		{"text_scale": preview_text_scale, "high_contrast": preview_high_contrast, "reduced_motion": true}, visual_profile)
	theme = context.theme
	get_node("Background").color = context.styles.NIGHT
	for key in ["HeroActorPanel", "BossActorPanel"]:
		var actor = get_node("%" + key)
		actor.visual_context = context
		var actor_data: Dictionary = actor.preview_actor.duplicate(true)
		var intent_text: String = actor.preview_intent
		if preview_state_mode == "empty":
			actor_data["resources"] = {}
			actor_data["statuses"] = []
			intent_text = ""
		elif preview_state_mode == "error":
			intent_text = "Unavailable"
		elif preview_state_mode == "long_text":
			actor_data["displayName"] = str(actor_data.get("displayName", key)) + " — Extended Chronicle Title"
			intent_text = "Channeling Multi-Stage Sovereign Confluence (12×3)"
		actor.setup(actor_data, actor.preview_hostile, {}, intent_text, actor.preview_compact, actor.preview_battlefield)
	for key in ["AttackCard", "SkillCard", "PowerCard"]:
		var card = get_node("%" + key)
		card.visual_context = context
		var card_data: Dictionary = card.preview_data.duplicate(true)
		if preview_state_mode == "empty":
			card_data["description"] = ""
			card_data["tags"] = []
		elif preview_state_mode == "error":
			card_data["playable"] = false
			card_data["unavailableReason"] = "Insufficient resources for preview action."
		elif preview_state_mode == "long_text":
			card_data["name"] = str(card_data.get("name", key)) + " of the Unbroken Firmament"
			card_data["description"] = str(card_data.get("description", "")) + " When resolved while Sovereign Resonance is active, retain all auxiliary wards and draw two additional inscriptions."
		card.configure(card_data)
	for key in ["SampleHealthView", "SampleArmorView"]:
		var resource = get_node("%" + key)
		resource.visual_context = context
		var res_state: Dictionary = resource.preview_state.duplicate(true)
		if preview_state_mode == "empty":
			res_state["current"] = 0
		resource.setup(resource.preview_resource_id, res_state)
	for badge in get_node("%StatusBadgesContainer").get_children():
		var status_data: Dictionary = badge.preview_status.duplicate(true)
		if preview_state_mode == "long_text":
			status_data["name"] = str(status_data.get("name", "Status")) + " (Extended)"
		badge.apply_status(status_data, context)
