extends SceneTree
## Mixed stock keeps all seventeen published offers and the stage exit reachable.
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func capture(file: String) -> void:
	if DisplayServer.get_name() == "headless": return
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--capture="):
			var directory := arg.trim_prefix("--capture=")
			DirAccess.make_dir_recursive_absolute(directory)
			await RenderingServer.frame_post_draw
			check(root.get_texture().get_image().save_png(directory.path_join(file)) == OK, "capture " + file)
func run() -> void:
	root.content_scale_size = Vector2i(1280, 720); root.size = Vector2i(1280, 720)
	var translator = root.get_node("I18n")
	translator.set_locale("pt_BR")
	var context = preload("res://scripts/ui/visual_context.gd").new(translator, {"reduced_motion":true}, preload("res://data/themes/ivory_run.tres"))
	var appearance := {"cards": {}}
	var catalog: Array = []
	var items: Array = []
	var choices: Array = []
	for index in 17:
		var id := "stock-%d" % index
		var definition := "definition-%d" % index
		var item := {"itemId":id, "costs":[{"resourceId":"gold", "amount":15}]}
		if index < 5:
			item["cardId"] = definition
			appearance.cards[definition] = {"name":"Carta %d" % (index + 1), "text":"Cause dano a um alvo."}
		else:
			item["modifierItemDefinitionId"] = definition
			catalog.append({"itemDefinitionId":definition, "presentation":{"name":"Runa %d" % (index - 4), "description":"Modifica a carta."}})
		items.append(item)
		choices.append({"type":"BUY_SHOP_ITEM","subjectId":id,"payload":{"shopInstanceId":"shop"}})
	choices.append({"type":"RESOLVE_NODE","subjectId":"bazaar","payload":{"currentNodeId":"bazaar"}})
	var state := {"runId":"stock-fixture", "settingId":"alisyum", "currentNodeId":"bazaar", "resources":{"gold":{"current":100}}, "map":{"nodes":[{"nodeId":"bazaar","activity":{"type":"Shop"}}]}, "shops":[{"shopInstanceId":"shop","items":items}]}
	var original := JSON.stringify([state, choices])
	var presenter = preload("res://scripts/presentation/activity_presenter.gd").new(state,choices,appearance,translator,[],{"catalog":catalog})
	var surface := PanelContainer.new(); root.add_child(surface); surface.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); surface.theme = context.theme
	var view = preload("res://scripts/ui/journey_view.gd").create()
	surface.add_child(view); view.apply_projection(state, choices, {}, appearance, presenter, context)
	await settle()
	var options = view.action_panel
	var filter: OptionButton = view.find_child("CategoryFilter", true, false)
	var offers: Control = options.find_child("Primary", true, false)
	var footer: Control = view.find_child("FixedFooter", true, false)
	var scroll: ScrollContainer = view.find_child("ActionScroll", true, false)
	check(filter.visible and offers.get_child_count() == 17, "5 cards and 12 runes in one published stock")
	check(filter.get_item_text(1).contains("(5)") and filter.get_item_text(2).contains("(12)"), "filter shows authoritative category counts")
	await capture("all-stock.png")
	var fixed_rect := footer.get_global_rect()
	var filter_rect := filter.get_global_rect()
	filter.select(2); filter.item_selected.emit(2); await settle()
	check(offers.get_children().filter(func(entry): return entry.visible).size() == 12, "rune filter preserves twelve offers")
	scroll.scroll_vertical = int(scroll.get_v_scroll_bar().max_value); await settle()
	check(footer.get_global_rect().is_equal_approx(fixed_rect) and footer.get_global_rect().end.y <= root.size.y, "exit stays pinned while scrolling mixed stock")
	check(filter.get_global_rect().is_equal_approx(filter_rect), "category filter stays pinned while scrolling mixed stock")
	await capture("runes-bottom.png")
	filter.select(1); filter.item_selected.emit(1); await settle()
	check(offers.get_children().filter(func(entry): return entry.visible).size() == 5, "card filter preserves five offers")
	check(JSON.stringify([state,choices]) == original, "filter and scroll leave all published state unchanged")
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
