extends RefCounted
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
static func sample_pose(start: Dictionary, target: Dictionary, raw: float, policy, key: String) -> Dictionary:
	var p: float = policy.progress(key, raw)
	var result := Pose.blend(start, target, p)
	var shape: Dictionary = policy.shape(key)
	var distance: float = (start.center as Vector2).distance_to(target.center)
	var lift := minf(float(start.get("size", Vector2(180, 270)).y) * .18, distance * float(shape.get("lift_ratio", 0.0)))
	result.center.y -= sin(p * PI) * lift
	result.rotation += sin(p * PI) * deg_to_rad(float(shape.get("tilt_degrees", 0.0)))
	result.scale *= preload("res://scripts/presentation/motion_curves.gd").settle(raw, float(shape.get("settle_scale", 0.0)))
	return result
## Consumes published retention/transfer facts. Shared by combat and the lab.
static func execute(id: String, retained: bool, continued: bool, hand, registry, context, staging: Callable, handle, skip_departure := false) -> void:
	var card = hand.card_for(id)
	if not is_instance_valid(card): handle.finish(); return
	var ghost: Control = registry.acquire(id, card.model, true, card.get_global_rect().get_center())
	ghost.modulate.a = 1.0
	registry.mark_staged(id)
	handle.completed.connect(func(result):
		if not is_instance_valid(hand): return
		if not retained and not continued:
			hand.sync_models(hand.ordered_cards().filter(func(item): return str(item.model.cardInstanceId) != id).map(func(item): return item.model), hand.scope)
		if not continued or result != "completed": registry.release(id, ghost, retained), CONNECT_ONE_SHOT)
	var duration: float = 0.0 if skip_departure else context.motion.duration("card_flight", true)
	var retain_duration: float = context.motion.duration("retain", true) if retained and not continued else 0.0
	if duration <= 0 and retain_duration <= 0: handle.hit(); handle.finish(); return
	var pose := Pose.read(ghost)
	var base_scale: Vector2 = hand.surface.get_global_transform().get_scale()
	var tween := ghost.create_tween(); handle.own(tween)
	if duration > 0:
		tween.tween_method(func(progress: float):
			Pose.write(ghost, sample_pose(pose, {"center": staging.call(), "rotation": 0.0, "scale": base_scale * .82}, progress, context.motion, "card_flight")),
			0.0, 1.0, duration)
	if retain_duration > 0:
		var return_pose := {}
		tween.tween_callback(func(): return_pose.merge(Pose.read(ghost), true))
		tween.tween_method(func(progress: float):
			var target: Dictionary = hand.destination_pose(id)
			if not target.is_empty(): Pose.write(ghost, sample_pose(return_pose, target, progress, context.motion, "retain")),
			0.0, 1.0, retain_duration)
	tween.finished.connect(func(): handle.hit(); handle.finish(), CONNECT_ONE_SHOT)
