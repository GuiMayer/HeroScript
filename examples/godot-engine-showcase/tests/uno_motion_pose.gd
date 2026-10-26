extends SceneTree
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var parent := Control.new(); root.add_child(parent)
	parent.position = Vector2(53, 27); parent.rotation = .12; parent.scale = Vector2(1.3, 1.3)
	var card := Control.new(); parent.add_child(card); card.size = Vector2(180, 270)
	var expected := {"center": Vector2(322, 287), "rotation": -.18, "scale": Vector2(.8, .8)}
	for top_level in [false, true]:
		card.set_as_top_level(top_level); Pose.write(card, expected)
		var actual := Pose.read(card)
		check(actual.center.distance_to(expected.center) < .01 and absf(actual.rotation - expected.rotation) < .001 and actual.scale.distance_to(expected.scale) < .001,
			"complete pose survives transformed parent, top_level=" + str(top_level))
	var finish := {"center": Vector2(720, 490), "rotation": .23, "scale": Vector2.ONE}
	check(Pose.blend(expected, finish, 0).center == expected.center and Pose.blend(expected, finish, 1).center == finish.center, "pose interpolation preserves exact endpoints")
	parent.queue_free(); await process_frame
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 5: await process_frame
	var id := str(lab.models[0].cardInstanceId)
	var before: Dictionary = Pose.read(lab.hand.card_for(id))
	var overlay := Control.new(); root.add_child(overlay)
	var borrowed: Control = lab.hand.borrow_card(id, overlay)
	check(Pose.read(borrowed).center.distance_to(before.center) < .01 and absf(Pose.read(borrowed).rotation - before.rotation) < .001, "borrowing preserves the exact visible pose")
	lab.hand.return_card(id, borrowed, true)
	var target: Dictionary = lab.hand.destination_pose(id)
	check(Pose.read(borrowed).center.distance_to(target.center) < 1 and absf(Pose.read(borrowed).rotation - target.rotation) < .001, "return restores fan rotation without a zero-angle frame")
	lab.queue_free(); overlay.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
