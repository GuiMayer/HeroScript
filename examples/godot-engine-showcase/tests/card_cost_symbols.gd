extends SceneTree
## Cost artwork is presentation data, not resource or payment semantics.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func passive(node: Node) -> bool:
	if node is Control and (node.mouse_filter != Control.MOUSE_FILTER_IGNORE or node.focus_mode != Control.FOCUS_NONE): return false
	for child in node.get_children():
		if not passive(child): return false
	return true
func _run() -> void:
	root.get_node("I18n").set_locale("en")
	var original_profile := AppTheme.profile
	var profile = load("res://data/themes/ivory_theme.tres")
	AppTheme.set_profile(profile)
	var Context = load("res://scripts/ui/visual_context.gd")
	var context = Context.new(root.get_node("I18n"), {"reduced_motion": true}, profile)
	var presentation: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	context.set_resource_styles(presentation.resources)
	var card := CardView.new(context)
	card.animate_entry = false
	var model := {"name": "Cost symbols", "rarityId": "common", "costs": [
		{"resourceId": "energy", "name": "Energy", "symbol": "E", "amount": 1},
		{"resourceId": "mana", "name": "Mana", "symbol": "M", "amount": 2}], "costsKnown": true}
	card.configure(model)
	root.add_child(card)
	await settle()
	var zone = card.find_child("Cost", true, false)
	var energy = zone.get_child(0)
	var mana = zone.get_child(2)
	check(energy.icon.texture.resource_path.ends_with("cost-energy.svg") and mana.icon.texture.resource_path.ends_with("cost-mana.svg"), "presentation JSON selects distinct energy/mana vector silhouettes")
	for path in ["cost-energy.svg", "cost-mana.svg", "cost-neutral.svg"]:
		var svg := FileAccess.get_file_as_string("res://assets/ivory/" + path)
		check(svg.count("<path ") == 1 and not svg.contains("<g ") and not svg.contains("#b83e25"), "resource symbol has only one contour and no internal decoration: " + path)
	check(energy.amount_label.text == "1" and mana.amount_label.text == "2", "native cost labels contain only the published numeral")
	check(zone.get_child(1).text == "+", "combined costs keep their additive separator")
	check(energy.tooltip_text == "Energy: 1" and mana.tooltip_text == "Mana: 2" and card.tooltip_text.contains("Energy"), "resource names remain accessible in cost/card details")
	check(passive(energy) and passive(mana), "symbols cannot intercept hand input or focus")
	for text_scale in [1.0, 1.2]:
		context._options["text_scale"] = text_scale
		for amount in [0, 12, 100, 0.5, 123.456]:
			model.costs = [{"resourceId": "energy", "name": "Energy", "amount": amount}]
			card.configure(model)
			await settle()
			var symbol = card.find_child("Cost", true, false).get_child(0)
			var numeral: Label = symbol.amount_label
			var glyph_width := numeral.get_theme_font("font").get_string_size(numeral.text, HORIZONTAL_ALIGNMENT_LEFT, -1, numeral.get_theme_font_size("font_size")).x
			check(numeral.text == context.translator.number(amount) and glyph_width <= numeral.size.x + 1 and numeral.autowrap_mode == TextServer.AUTOWRAP_OFF, "exact, unclipped cost %s at scale %s" % [amount, text_scale])
			check(numeral.get_global_rect().get_center().distance_to(symbol.get_global_rect().get_center()) <= 1, "cost numeral remains centered at %s / %s" % [amount, text_scale])
	context._options["high_contrast"] = true
	model.costs = [{"resourceId": "custom", "symbol": "Q", "name": "Charge", "amount": 0.5}]
	card.configure(model)
	await settle()
	var generic = card.find_child("Cost", true, false).get_child(0)
	check(generic.icon.texture == generic.NEUTRAL and generic.find_child("ResourceMnemonic", true, false).text == "Q" and generic.amount_label.text == "0.5", "unknown resources use a neutral symbol plus configured mnemonic")
	var mnemonic: Label = generic.find_child("ResourceMnemonic", true, false)
	var mnemonic_width := mnemonic.get_theme_font("font").get_string_size(mnemonic.text, HORIZONTAL_ALIGNMENT_LEFT, -1, mnemonic.get_theme_font_size("font_size")).x
	check(mnemonic.get_global_rect().end.x - mnemonic_width >= generic.amount_label.get_global_rect().end.x, "unknown-resource mnemonic stays outside the centered numeral field")
	check(generic.amount_label.get_theme_color("font_color") == generic.INK, "high contrast preserves ink numerals on the printed paper field")
	context.set_resource_styles({"custom": {"cost_icon": "res://does-not-exist.svg"}})
	card.configure(model)
	check(card.find_child("Cost", true, false).get_child(0).icon.texture == generic.NEUTRAL, "missing artwork falls back without losing the published amount")
	context.set_resource_styles({"custom": {"cost_icon": "res://assets/ivory/cost-mana.svg"}})
	card.configure(model)
	check(card.find_child("Cost", true, false).get_child(0).icon.texture.resource_path.ends_with("cost-mana.svg"), "any custom resource can opt into artwork without resource-ID branches")
	model.costs = []
	model.costOptions = [[{"resourceId": "custom", "amount": 2}], [{"resourceId": "custom", "amount": 3}]]
	card.configure(model)
	check(card.find_child("Cost", true, false).get_child(1).text == context.translator.text("OR"), "alternative payments retain OR rather than implying additive cost")
	context.profile = original_profile
	AppTheme.set_profile(original_profile)
	model.erase("costOptions")
	model.costs = [{"resourceId": "energy", "symbol": "E", "amount": 1}]
	card.configure(model)
	check(card.find_child("Cost", true, false).get_child(0) is PanelContainer, "production profile keeps its existing cost presentation")
	card.queue_free()
	await settle()
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
