extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 10: await process_frame
func run() -> void:
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json"))
	var translator = root.get_node("I18n")
	var previous: String = translator.locale
	for profile in [preload("res://data/themes/ivory_theme.tres"), preload("res://data/themes/ivory_run.tres")]:
		for language in ["en", "pt_BR"]:
			translator.set_locale(language)
			for factor in [1.0, 1.2]:
				var context = preload("res://scripts/ui/visual_context.gd").new(translator, {"text_scale": factor, "reduced_motion": true}, profile)
				var reference := Vector2.ZERO
				for key in fixture.cards:
					var model: Dictionary = fixture.cards[key].duplicate(true)
					if language == "pt_BR": model.merge(model.get(language, {}), true)
					var original := JSON.stringify(model)
					var card := CardView.create(context)
					card.animate_entry = false; card.hand_managed = true
					root.add_child(card)
					card.configure(model)
					card.size = card.custom_minimum_size
					await settle()
					var title: Label = card.find_child("CardName", true, false)
					var art: Control = card.find_child("Art", true, false)
					var line: Control = card.find_child("CostLine", true, false)
					check(line.get_parent().name == "CardHeader" and line.position.x > title.get_parent().position.x, "cost sits at the right of the title: " + key)
					check(card.find_child("IdentityTags", true, false).get_index() < art.get_index(), "identity tags precede the artwork: " + key)
					var rows := Vector2(title.get_parent().size.y, art.position.y)
					if reference == Vector2.ZERO: reference = rows
					var label := "%s/%s/%s/%s" % [profile.profile_id, language, factor, key]
					check(rows.is_equal_approx(reference) and is_equal_approx(art.size.y, context.card_style.art_height), "title/one resource row/art align: " + label)
					var cost: Control = card.find_child("Cost", true, false)
					var visible_width := cost.size.x * cost.scale.x
					check(cost is HBoxContainer and visible_width <= line.size.x + .1 and cost.size.y * cost.scale.y <= line.size.y + .1 and is_equal_approx(cost.scale.x, cost.scale.y), "all costs stay on a single bounded line: " + label)
					check(title.max_lines_visible in [1, 2] and is_equal_approx(title.get_parent().size.y, ceilf(context.card_style.title_line_height * factor)) and JSON.stringify(model) == original, "fixed title area and immutable model: " + label)
					# Reconciliation must retain the same line component after a cost changes.
					model.costOptions = [[{"resourceId": "energy", "amount": 2}, {"resourceId": "mana", "amount": 3}], [{"resourceId": "heat", "amount": 123}, {"resourceId": "focus", "amount": 100}]]
					card.configure(model)
					await settle()
					cost = card.find_child("Cost", true, false)
					check(card.find_child("CostLine", true, false) == line and cost.size.x * cost.scale.x <= line.size.x + .1 and is_equal_approx(art.position.y, reference.y), "alternative cost refresh preserves row and art: " + label)
					card.queue_free()
					await process_frame
	translator.set_locale(previous)
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
