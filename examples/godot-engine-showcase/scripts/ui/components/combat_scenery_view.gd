extends Control
## Artwork only. Actor positions follow UI anchors without owning targets or rules.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var portraits: Dictionary = {}
var _anchors: Dictionary = {}
var _backdrop
var _stage: Control
func bind_stage(stage: Control) -> void:
	_stage = stage

func apply_model(actors: Array, background_id: String, context: ContextType) -> void:
	visual_context = context
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	if not is_instance_valid(_backdrop):
		_backdrop = preload("res://scripts/ui/art_slot.gd").new(context)
		add_child(_backdrop)
		_backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_backdrop.rebind_context(context)
	_backdrop.setup("backgrounds", background_id)
	var live: Array = []
	for item in actors:
		var actor: Dictionary = item.actor
		var id := str(actor.get("instanceId", ""))
		live.append(id)
		if not portraits.has(id):
			portraits[id] = preload("res://scripts/ui/art_slot.gd").new(context)
			portraits[id].name = "ActorArtwork_" + id.validate_node_name()
			add_child(portraits[id])
		portraits[id].rebind_context(context)
		portraits[id].setup("actors", str(actor.get("definitionId", "")))
	for id in portraits.keys():
		if id not in live:
			portraits[id].queue_free()
			portraits.erase(id)

func bind_anchors(anchors: Dictionary) -> void:
	_anchors = anchors.duplicate()
	_sync_anchors()

func _process(_delta: float) -> void: _sync_anchors()
func _sync_anchors() -> void:
	if is_instance_valid(_stage):
		global_position = _stage.global_position
		size = _stage.size
		visible = _stage.is_visible_in_tree()
	clip_contents = true
	for id in portraits:
		var art: Control = portraits[id]
		var anchor = _anchors.get(id)
		art.visible = is_instance_valid(anchor) and anchor.is_visible_in_tree()
		if not art.visible: continue
		var rect: Rect2 = anchor.get_global_rect()
		art.global_position = rect.position
		art.size = rect.size
