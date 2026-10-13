extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var classifier = preload("res://scripts/presentation/semantic_cues.gd")
	var damage := {"effectType": "DAMAGE", "resourceId": "arbitrary", "previousValue": 10, "currentValue": 4}
	check(classifier.kind(damage) == "impact", "damage semantics do not depend on health")
	var cost := damage.duplicate(true); cost.provenance = {"sourceId": "card", "componentId": "cost"}
	var frame := {"payments": [{"sourceId": "card", "componentId": "cost"}], "applications": [cost]}
	check(classifier.kind(cost, frame) == "payment", "exact published provenance identifies payment")
	cost.provenance.sourceId = "other"
	check(classifier.kind(cost, frame) == "impact", "same component name on another source is not a payment")
	cost.provenance.sourceId = "card"
	check(preload("res://scripts/presentation/combat_audio_presenter.gd").cues(frame).is_empty(), "cost settlement cannot play a damage sound")
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var view = preload("res://scripts/ui/resource_view.gd").new(context); root.add_child(view)
	var state := {"current": 10, "minimum": 0, "maximum": 10}
	view.setup("arbitrary", state)
	view.present_change(damage, 0)
	check(view.bar.value == 4 and state.current == 10, "visual resource consumes published endpoints without modifying source state")
	context.motion = preload("res://scripts/presentation/motion_policy.gd").capture("default", {"animation_preset": "instant"})
	var feedback = preload("res://scripts/ui/components/combat_feedback.gd").new(context); root.add_child(feedback)
	var panel = preload("res://scripts/ui/actor_panel.gd").new(context); root.add_child(panel)
	panel.setup({"instanceId": "hero", "resources": {"arbitrary": state}}, false, {}, "")
	var handle = preload("res://scripts/presentation/presentation_handle.gd").new()
	var presenter = preload("res://scripts/presentation/combat_presenter.gd").new({}, {}, [], context.translator)
	preload("res://scripts/ui/effect_motion.gd").execute({"applications": [damage]}, handle, context, feedback, presenter, {"hero": panel.portrait_view}, {"hero": panel}, 0, 1)
	check(handle.done and feedback.effects.get_child_count() == 0, "instant real effects publish without an empty tween or cosmetic delay")
	feedback.queue_free(); panel.queue_free()
	view.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
