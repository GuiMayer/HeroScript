@tool
class_name UICatalogWorkbench
extends Control
## The editor owns all nodes and sample values. This adapter only populates them.
@export var visual_profile: Resource = preload("res://data/themes/preserved.tres"):
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
var _refresh_queued := false
func _ready() -> void: _populate_workbench()
func _queue_preview() -> void:
    if not is_node_ready() or _refresh_queued: return
    _refresh_queued = true
    _populate_workbench.call_deferred()
func _populate_workbench() -> void:
    _refresh_queued = false
    var context = preload("res://scripts/ui/visual_context.gd").new(null,
        {"text_scale": preview_text_scale, "high_contrast": preview_high_contrast, "reduced_motion": true}, visual_profile)
    theme = context.theme
    for key in ["HeroActorPanel", "BossActorPanel"]:
        var actor = get_node("%" + key)
        actor.visual_context = context
        actor.setup(actor.preview_actor, actor.preview_hostile, {}, actor.preview_intent, actor.preview_compact, actor.preview_battlefield)
    for key in ["AttackCard", "SkillCard", "PowerCard"]:
        var card = get_node("%" + key)
        card.visual_context = context
        card.configure(card.preview_data)
    for key in ["SampleHealthView", "SampleArmorView"]:
        var resource = get_node("%" + key)
        resource.visual_context = context
        resource.setup(resource.preview_resource_id, resource.preview_state)
    for badge in get_node("%StatusBadgesContainer").get_children():
        badge.apply_status(badge.preview_status, context)
