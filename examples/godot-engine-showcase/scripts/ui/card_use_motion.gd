extends RefCounted
## Consumes published retention/transfer facts. Shared by combat and the lab.
static func execute(id: String, retained: bool, continued: bool, hand, registry, context, staging: Callable, handle) -> void:
	var card = hand.card_for(id)
	if not is_instance_valid(card): handle.finish(); return
	var ghost: Control = registry.acquire(id, card.model, true, card.get_global_rect().get_center())
	registry.mark_staged(id)
	handle.completed.connect(func(result):
		if not is_instance_valid(hand): return
		if not retained and not continued:
			hand.sync_models(hand.ordered_cards().filter(func(item): return str(item.model.cardInstanceId) != id).map(func(item): return item.model), hand.scope)
		if not continued or result != "completed": registry.release(id, ghost, retained), CONNECT_ONE_SHOT)
	var duration: float = context.motion.duration("card_flight", true)
	if duration <= 0: handle.hit(); handle.finish(); return
	var pose := {"center": ghost.global_position + ghost.size * .5, "rotation": ghost.rotation, "scale": ghost.scale.x}
	var tween := ghost.create_tween(); handle.own(tween)
	tween.tween_method(func(progress: float):
		var center: Vector2 = pose.center.lerp(staging.call(), progress)
		ghost.global_position = center - ghost.size * .5
		ghost.rotation = lerpf(float(pose.rotation), 0, progress)
		ghost.scale = Vector2.ONE * lerpf(float(pose.scale), .82, progress), 0.0, 1.0, duration).set_trans(context.motion.curve("card_flight")).set_ease(context.motion.easing("card_flight"))
	if retained and not continued:
		var return_pose := {"center": Vector2.ZERO}
		tween.tween_callback(func(): return_pose.center = ghost.global_position + ghost.size * .5)
		tween.tween_method(func(progress: float):
			ghost.global_position = return_pose.center.lerp(hand.destination_center(id), progress) - ghost.size * .5
			ghost.scale = Vector2.ONE * lerpf(.82, 1, progress), 0.0, 1.0, context.motion.duration("retain", true)).set_trans(context.motion.curve("retain")).set_ease(context.motion.easing("retain"))
	tween.finished.connect(func(): handle.hit(); handle.finish(), CONNECT_ONE_SHOT)
