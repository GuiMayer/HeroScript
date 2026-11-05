extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(root.get_node("I18n"), {}, load("res://data/themes/ivory_run.tres"))
	var policy = context.card_style.duplicate()
	policy.cost_symbol_size = Vector2(24, 18)
	for amount in [1, 12, 123]:
		var chip = load("res://scripts/ui/card_cost_symbol.gd").create({"resourceId": "custom", "amount": amount}, {}, context, policy)
		root.add_child(chip)
		# Deliberately hostile parent allocation and non-square requested policy.
		chip.position = Vector2(20, 20)
		chip.size = Vector2(160, 50)
		context.styles.apply_view_preferences(chip, 1.2, true)
		for _frame in 4: await process_frame
		check(is_equal_approx(chip.icon.size.x, chip.icon.size.y) and chip.icon.size.x > 0, "icon is 1:1 even with a wide parent and amount %s: %s" % [amount, chip.icon.size])
		var pixels: Image = chip.icon.texture.get_image()
		check(chip.find_child("SolidBacking", true, false) == null and pixels.get_pixel(pixels.get_width() / 2, pixels.get_height() / 2).a == 1 and pixels.get_pixel(0, 0).a == 0, "only the filled resource silhouette is painted, with transparent surroundings " + str(amount))
		check(chip.amount_label.get_theme_color("font_color").is_equal_approx(Color.WHITE) and chip.amount_label.get_global_rect().get_center().distance_to(chip.icon.get_global_rect().get_center()) < 1, "white number remains centered over the fill " + str(amount))
		check(chip.icon.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED, "artwork cannot be stretched " + str(amount))
		chip.queue_free()
		await process_frame
	for resource in ["energy", "mana"]:
		var chip = load("res://scripts/ui/card_cost_symbol.gd").create({"resourceId": resource, "amount": 1}, {"cost_icon": "res://assets/ivory/cost-%s.svg" % resource}, context, policy)
		var image: Image = chip.icon.texture.get_image()
		check(chip.find_child("SolidBacking", true, false) == null and image.get_pixel(image.get_width() / 2, image.get_height() / 2).is_equal_approx(chip.INK), resource + " uses its own filled vector symbol, not a circular background")
		chip.free()
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
