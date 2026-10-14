extends RefCounted
## Presentation only. The lab supplies the final card list; burning implies no engine rule.
const View = preload("res://scripts/ui/card_burn_view.gd")
const DEFAULT_DURATION := 1.0
const MAX_DURATION := 30.0

static func cycle_seconds(value: float) -> float:
	return clampf(value, 0, MAX_DURATION) if is_finite(value) else DEFAULT_DURATION

static func enabled(context, preset: String, seconds := DEFAULT_DURATION) -> bool:
	return cycle_seconds(seconds) > 0 and preset != "instant" and not context.reduced_motion

static func execute(id: String, direction: String, hand, registry, context, overlay: Control, handle, preset := "normal", seconds := DEFAULT_DURATION) -> void:
	var source = hand.card_for(id)
	if not enabled(context, preset, seconds) or not is_instance_valid(source):
		handle.hit(); handle.finish(); return
	var ghost: CardView = registry.acquire(id, source.model, true, source.get_global_rect().get_center())
	var burn = View.new()
	overlay.add_child(burn)
	burn.setup(ghost, direction)
	var burn_ref: WeakRef = weakref(burn)
	var ghost_ref: WeakRef = weakref(ghost)
	handle.cleanup(func():
		var live = burn_ref.get_ref()
		if is_instance_valid(live): live.hide(); live.queue_free()
		registry.release(id, ghost_ref.get_ref(), false))
	var tween := burn.create_tween()
	tween.tween_method(burn.set_progress, 0.0, 1.0, cycle_seconds(seconds)).set_trans(Tween.TRANS_LINEAR)
	tween.pause()
	handle.own(tween)
	# Keep the original visible until the full face has actually rendered.
	burn.snapshot_ready.connect(func():
		if handle.done: return
		var live = burn_ref.get_ref()
		var original = ghost_ref.get_ref()
		if not is_instance_valid(live) or not is_instance_valid(original): handle.finish("cancelled"); return
		original.hide()
		live.output.show()
		tween.play(), CONNECT_ONE_SHOT)
	tween.finished.connect(func(): handle.hit(); handle.finish(), CONNECT_ONE_SHOT)
