extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var motion = preload("res://scripts/ui/hand_retarget_motion.gd")
	var card := Control.new(); root.add_child(card)
	var tween := card.create_tween()
	motion.append(tween, card, "position", Vector2(100, -30), .2)
	await create_timer(.06).timeout
	var current := card.position; tween.kill()
	tween = card.create_tween(); motion.append(tween, card, "position", Vector2(140, -20), .1)
	check(card.position == current, "retargeting starts at the current visual pose without snapping")
	await tween.finished
	check(card.position.distance_to(Vector2(140, -20)) < .01 and (card.get_meta("hand_velocity_position") as Vector2).length() < .01, "finite retargeting reaches exact destination with zero exit velocity")
	tween = card.create_tween(); motion.append(tween, card, "rotation", .15, .05); await tween.finished
	check(absf(card.rotation - .15) < .001, "same retargeter handles card orientation")
	card.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
