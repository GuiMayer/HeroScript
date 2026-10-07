extends SceneTree
## Offline shared-component regression. Does not connect, start a run or save preferences.
var failures := 0
const Layout = preload("res://tests/layout_inspector.gd")

func _init() -> void: call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func settle() -> void:
	for _frame in 4: await process_frame

func _run() -> void:
	var preferences = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var saved := FileAccess.get_file_as_string(preferences.SAVE_PATH) if FileAccess.file_exists(preferences.SAVE_PATH) else ""
	var prior := {"scale": preferences.text_scale, "locale": i18n.locale, "run": JSON.stringify(session.run), "combat": JSON.stringify(session.combat)}
	var hand_script = load("res://scripts/ui/card_hand_view.gd")
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate()
	root.add_child(lab)
	await settle()
	check(lab.hand.get_script() == hand_script, "laboratory uses the shared combat hand")
	check(lab.catalog.cards.size() >= 16 and lab.catalog.presets.size() == 4, "varied familiar, volatile, scaling and custom-content fixtures")
	var screen = load("res://scripts/ui/combat_screen.gd").new()
	screen.gameplay_column = VBoxContainer.new()
	screen.add_child(screen.gameplay_column)
	var snapshot := {"zones": [{"zoneId": "anything", "allowsCardPlay": true, "contentsVisible": true, "cards": [{"cardInstanceId": "sample", "definitionId": "basic_attack"}]}]}
	screen.zone_presenter = preload("res://scripts/presentation/card_zone_presenter.gd").new(snapshot, i18n)
	screen.presenter = preload("res://scripts/presentation/combat_presenter.gd").new({}, {}, [], i18n, {}, snapshot)
	screen._build_hand()
	check(screen.find_child("CardHandView", true, false).get_script() == lab.hand.get_script(), "normal combat instantiates the exact same hand component")
	check(screen.card_buttons.sample.get_script() == lab.card_controls[0].get_script(), "normal combat and lab use the exact same CardView")
	screen.free()
	for dimensions in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = dimensions
		for locale in ["en", "pt_BR"]:
			lab.set_language(locale)
			for text_scale in [1.0, 1.2]:
				lab.set_text_scale(text_scale)
				for preset in lab.catalog.presets.size():
					lab.set_preset(preset)
					await settle()
					check(Layout.vertical_text_issues(lab).is_empty(), "horizontal readable layout: %s / %s / %s / %s" % [dimensions, locale, text_scale, preset])
					check(lab.hand.get_global_rect().end.y <= root.get_visible_rect().end.y + 1, "hand remains within the viewport")
	for amount in [0, 1, 5, 12, 40]:
		lab.set_count(amount)
		await settle()
		check(lab.card_controls.size() == amount, "hand population: %s" % amount)
		check(lab.hand.scroll.horizontal_scroll_mode == ScrollContainer.SCROLL_MODE_AUTO, "large hands retain horizontal scrolling without shrinking card fonts")
		if amount == 0:
			var message: Label = lab.hand.find_child("EmptyHandMessage", true, false)
			check(message.size.x >= 280 and message.get_line_count() == 1, "shared empty hand never becomes vertical text")
	lab.card_controls[-1].grab_focus()
	await settle()
	check(lab.hand.scroll.scroll_horizontal > 0, "keyboard/controller focus reveals offscreen cards in large hands")
	lab.set_preset(3)
	var card = lab.card_controls[0]
	var font_before: int = card.find_child("Rules", true, false).get_theme_font_size("font_size")
	lab.select_card(str(card.model.labId))
	check(lab.card_controls[0] == card and card.find_child("Rules", true, false).get_theme_font_size("font_size") == font_before, "selection does not rebuild the hand or change fonts")
	lab.select_card(str(lab.models[1].labId))
	check(lab.details.text.contains(str(lab.models[1].effects[-1])), "long rule text is available in full without pretending it fits the current card face")
	var count_before: int = lab.models.size()
	lab.remove_selected()
	check(lab.models.size() == count_before - 1, "removal is local to visual fixtures")
	check(JSON.stringify(session.run) == prior.run and JSON.stringify(session.combat) == prior.combat, "laboratory never changes authoritative session snapshots")
	lab.queue_free()
	await settle()
	check(preferences.text_scale == prior.scale and i18n.locale == prior.locale, "temporary visual preferences restore on exit")
	check((FileAccess.get_file_as_string(preferences.SAVE_PATH) if FileAccess.file_exists(preferences.SAVE_PATH) else "") == saved, "laboratory never writes player preferences")
	root.get_node("GameAudio").shutdown()
	print("SHOWCASE_CARD_HAND_LAB failures=", failures)
	quit(0 if failures == 0 else 1)
