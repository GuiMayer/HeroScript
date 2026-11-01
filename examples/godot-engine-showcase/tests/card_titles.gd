extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func settle() -> void:
	for frame in 8: await process_frame
func run() -> void:
	var translator = root.get_node("I18n")
	var previous: String = translator.locale
	var shrunk := false
	var wrapped := false
	for language in ["en", "pt_BR"]:
		translator.set_locale(language)
		for scale in [1.0, 1.2, 1.6]:
			var context = preload("res://scripts/ui/visual_context.gd").new(translator, {"text_scale": scale, "reduced_motion": true}, preload("res://data/themes/ivory_theme.tres"))
			var card := CardView.create(context)
			card.animate_entry = false; card.hand_managed = true
			root.add_child(card)
			var reference := Vector2.ZERO
			for value in ["Strike", "Unrelenting Calamity", "The Burning Heart of the Mountain", "Convergência Volátil do Guardião", "Unbroken".repeat(25)]:
				var model := {"name": value, "definitionId": "basic_attack", "costs": [{"resourceId": "energy", "amount": 1}], "effects": ["Test"]}
				card.configure(model); card.size = card.custom_minimum_size
				await settle()
				var title: Label = card.find_child("CardName", true, false)
				var slot: Control = card.find_child("CardTitle", true, false)
				var art: Control = card.find_child("Art", true, false)
				var size_used := title.get_theme_font_size("font_size")
				var metrics := Vector2(slot.size.y, art.position.y)
				if reference == Vector2.ZERO: reference = metrics
				var label := "%s/%.1f/%s" % [language, scale, value.left(25)]
				check(metrics.is_equal_approx(reference) and is_equal_approx(slot.size.y, ceilf(context.card_style.title_line_height * scale)), "fixed title slot and art position: " + label)
				check(context.card_style.title_line_height == 24.0 and absf(card.find_child("CardHeader", true, false).size.y - 24.0 * scale) <= 1.0, "original 24-unit header retained: " + label)
				var minimum: int = context.card_style.title_min_font_size if title.max_lines_visible == 1 else context.card_style.title_min_two_line_font_size
				check(size_used >= roundi(minimum * scale) and size_used <= roundi(context.card_style.title_font_size * scale), "font bounds for chosen line count: " + label)
				check(title.max_lines_visible in [1, 2] and title.size.y <= slot.size.y + .1, "at most two lines in the same slot: " + label)
				if value == "Strike": check(size_used == roundi(context.card_style.title_font_size * scale) and title.max_lines_visible == 1, "short name keeps full size on one line")
				shrunk = shrunk or size_used < roundi(context.card_style.title_font_size * scale)
				wrapped = wrapped or (title.max_lines_visible == 2 and title.get_line_count() == 2)
				card.configure(model.merged({"name": "Strike"}, true))
				await settle()
				check(card.find_child("CardName", true, false) == title and title.text == "Strike" and title.max_lines_visible == 1 and title.get_theme_font_size("font_size") == roundi(context.card_style.title_font_size * scale), "same label refits upward after refresh")
			card.queue_free(); await process_frame
	check(shrunk and wrapped, "fixtures exercise both shrinking and two-line fitting")
	translator.set_locale(previous)
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
