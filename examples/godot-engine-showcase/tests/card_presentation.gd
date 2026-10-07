extends SceneTree
## Offline contracts: presentation consumes facts, never evaluates gameplay.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func settle() -> void:
	for _frame in 6: await process_frame

func passive_tooltip(node: Node) -> bool:
	if node is ScrollContainer: return false
	if node is Control and (node.focus_mode != Control.FOCUS_NONE or node.mouse_filter != Control.MOUSE_FILTER_IGNORE):
		print("Tooltip input mismatch: ", node.get_path(), " focus=", node.focus_mode, " mouse=", node.mouse_filter)
		return false
	if node is RichTextLabel and (node.scroll_active or node.selection_enabled): return false
	for child in node.get_children():
		if not passive_tooltip(child): return false
	return true

func label_texts(node: Node) -> String:
	var result: String = node.text if node is Label else ""
	for child in node.get_children(): result += label_texts(child)
	return result

func inspection(current: float, baseline: float, type := "DAMAGE", resource := "mana") -> Dictionary:
	var effect := {"effectId": "definition.effect", "type": type, "targetResource": resource, "flatValue": baseline, "formulaValue": null, "condition": null, "chainedEffects": null}
	return {"version": {"runSequence": 1}, "baseContainer": {"rarity": "Rare", "tags": ["fire"]},
		"compiledContainer": {"components": [{"componentId": "impact", "type": "effect", "effect": effect}]},
		"effectiveBase": {"components": [{"componentId": "impact", "type": "effect", "effect": effect}]},
		"evaluation": {"cardInstanceId": "card", "costs": []},
		"previewScope": {"hasExecutablePreview": true},
		"previewSteps": [{"applied": true, "effectInstanceId": "runtime:compound:execution:id", "provenance": {"componentId": "impact"},
			"identity": {"parentProcId": null}, "calculation": {"baseValue": baseline, "value": current}, "parameters": []}]}

func _run() -> void:
	var i18n = root.get_node("I18n")
	var prefs = root.get_node("Preferences")
	var previous := {"locale": i18n.locale, "scale": prefs.text_scale, "contrast": prefs.high_contrast}
	i18n.set_locale("en")
	prefs.text_scale = 1.0
	prefs.high_contrast = false
	var sections = load("res://scripts/presentation/card_section_presenter.gd").new(i18n, {"card_tag_labels": {"fire": "Fire"}}, [])
	var style = load("res://data/default_card_visual_style.tres")
	check(style.value_color(6, 6) == Color.WHITE, "unchanged values are white")
	check(style.value_color(3, 6) == style.lower_value_color, "lower values are red")
	check(style.value_color(9, 6) == style.higher_value_color, "higher values are blue")
	check(style.value_color(9, null) == Color.WHITE and style.value_color(NAN, 6) == Color.WHITE, "missing or invalid comparisons stay neutral")
	var model: Dictionary = sections.sections(inspection(9, 6), {})
	check(model.effectRows[0].segments[1].value == 9 and model.effectRows[0].segments[1].baseValue == 6, "published root calculation uses typed identity, not definition/execution ID equality")
	check(model.rarityId == "rare" and model.identityTags == ["Fire"], "canonical rarity and configured identity are independent of localization")
	var numeric_rarity := inspection(9, 6)
	numeric_rarity.baseContainer.rarity = 2
	check(sections.sections(numeric_rarity, {}).rarityId == "rare", "numeric contract enums normalize to the same rarity palette")
	var damage_text: String = load("res://scripts/ui/card_effect_text.gd").plain_text(model.effectRows)
	check(damage_text.to_lower().contains("mana") and not damage_text.to_lower().contains("health"), "resource decrease is not assumed to be health damage")
	var child_trace := inspection(9, 6)
	child_trace.previewSteps.append({"applied": true, "provenance": {"componentId": "impact"}, "identity": {"parentProcId": "parent"}, "calculation": {"value": 99, "baseValue": 99}})
	check(sections.sections(child_trace, {}).effectRows[0].segments[1].value == 9, "child calculations do not contaminate their parent's value")
	var variable_trace := inspection(9, 6)
	variable_trace.previewSteps.append({"applied": true, "provenance": {"componentId": "impact"}, "identity": {"parentProcId": null}, "calculation": {"value": 4, "baseValue": 6}})
	check(not sections.sections(variable_trace, {}).effectRows[0].segments[1].has("value"), "different impact/target values are declared variable instead of inventing a single value")
	var formula := inspection(9, 6)
	formula.compiledContainer.components[0].effect.formulaValue = "player.strength + 6"
	check(sections.sections(formula, {}).effectRows[0].segments[1].baseValue == 6, "formula baseline comes from the published calculation")
	formula.previewSteps = []
	check(not sections.sections(formula, {}).effectRows[0].segments[1].has("value"), "client never evaluates a formula without a preview")
	var unavailable := inspection(9, 6)
	unavailable.previewSteps = []
	unavailable.previewScope.hasExecutablePreview = false
	unavailable.effectiveBase.components[0].effect.flatValue = 8
	unavailable.compiledContainer.components[0].effect = unavailable.compiledContainer.components[0].effect.duplicate(true)
	unavailable.compiledContainer.components[0].effect.flatValue = 6
	check(sections.sections(unavailable, {}).effectRows[0].segments[1].value == 8, "effective base remains readable when the card has no legal preview")
	var run := {"sequence": 1, "deck": {"cardInstances": [{"cardInstanceId": "card", "definitionId": "definition"}]}}
	var quote_a := {"command": {"cardInstanceId": "card", "targetIds": ["one"]}, "costs": [{"resourceId": "energy", "amount": 1}]}
	var quote_b := {"command": {"cardInstanceId": "card", "targetIds": ["two"]}, "costs": [{"resourceId": "energy", "amount": 1}]}
	var quote_c := {"command": {"cardInstanceId": "card", "targetIds": ["one"]}, "costs": [{"resourceId": "mana", "amount": 2}]}
	var presenter = load("res://scripts/presentation/combat_presenter.gd").new(run, {}, [quote_a, quote_b, quote_c], i18n)
	check(presenter.card_view_model("card").costOptions.size() == 2, "cost alternatives deduplicate targets without merging different payments")
	var unknown_quote := inspection(9, 6)
	unknown_quote.effectiveBase.components.append({"componentId": "price", "type": "cost", "costs": {"alternativeCosts": [{"optionId": "mana"}]}})
	var unavailable_presenter = load("res://scripts/presentation/combat_presenter.gd").new(run, {}, [], i18n)
	unavailable_presenter.accept_evaluations([unknown_quote])
	check(not unavailable_presenter.card_view_model("card").costsKnown, "missing alternative quotes are never presented as a known free cost")
	model.merge({"name": "Presentation contract", "cardType": "ATTACK", "rarity": "RARE", "costs": [], "availability": "SELECT TO PLAY"})
	model.effectRows.append({"segments": [{"text": "Lower "}, {"text": "3", "value": 3, "baseValue": 6}, {"text": " · Base "}, {"text": "6", "value": 6, "baseValue": 6}]})
	var card = load("res://scripts/ui/card_view.gd").new()
	card.animate_entry = false
	card.configure(model)
	root.add_child(card)
	for _frame in 4: await process_frame
	var rules = card.find_child("Rules", true, false)
	check(rules.numeric_colors == [style.higher_value_color, style.lower_value_color, style.base_value_color], "numeric colors are applied per value, never to the whole description")
	check(card.find_child("Cost", true, false).position.y > card.find_child("CardName", true, false).position.y, "costs occupy their own strip below the name")
	for state in ["normal", "hover", "pressed", "disabled", "focus"]:
		check(card.get_theme_stylebox(state).border_color == style.rarity_color("rare"), "rarity is preserved in " + state)
	var tooltip = card._make_custom_tooltip(card.tooltip_text)
	root.add_child(tooltip)
	check(tooltip.get_theme_stylebox("panel").border_color == card.get_theme_stylebox("normal").border_color, "detailed tooltip shares the rarity border")
	check(tooltip.find_child("DetailedEffects", true, false).numeric_colors == rules.numeric_colors, "tooltip shares the numeric palette and typed values")
	check(passive_tooltip(tooltip), "hover summary has no scroll container, focus, selection or nested tooltip interaction")
	check(not tooltip.find_child("DetailedEffects", true, false).get_v_scroll_bar().visible, "hover summary never displays a rich-text scrollbar")
	check(card._make_custom_tooltip("") == null, "an empty tooltip does not create a popup")
	tooltip.free()
	var dialog = load("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup("Inspection", "fallback", inspection(9, 6), model, style)
	root.add_child(dialog)
	check(dialog.find_child("CardDetails", true, false).get_theme_stylebox("panel").border_color == style.rarity_color("rare"), "keyboard/controller inspection uses the same rarity panel")
	check(dialog.find_child("CardDetailsScroll", true, false).focus_mode == Control.FOCUS_ALL, "only persistent inspection exposes a focusable scroll container")
	dialog.free()
	var verbose_model := model.duplicate(true)
	verbose_model.name = "Long card name ".repeat(50)
	verbose_model["inspectionText"] = "ENGINE_JOURNAL_ONLY ".repeat(500)
	verbose_model["effectRows"] = []
	for index in 30:
		verbose_model.effectRows.append({"segments": [{"text": "Effect %s " % index + "A deliberately long effect description. ".repeat(15)}, {"text": "9", "value": 9, "baseValue": 6}]})
	verbose_model["requirements"] = [{"text": "Requirement ".repeat(60)}, {"text": "SECOND_REQUIREMENT_ONLY"}]
	var model_before := JSON.stringify(verbose_model)
	for locale in ["en", "pt_BR"]:
		i18n.set_locale(locale)
		for text_scale in [1.0, 1.2]:
			prefs.text_scale = text_scale
			card.configure(verbose_model)
			var summary = card._make_custom_tooltip(card.tooltip_text)
			root.add_child(summary)
			await settle()
			check(passive_tooltip(summary) and summary.find_child("DetailedEffects", true, false).rows.size() == style.tooltip_effect_rows,
				"long content stays a bounded passive summary: %s / %s" % [locale, text_scale])
			check(summary.size.x <= style.tooltip_width + 1 and summary.size.y < 600, "long content cannot expand the tooltip indefinitely")
			check(not label_texts(summary).contains("ENGINE_JOURNAL_ONLY") and not label_texts(summary).contains("SECOND_REQUIREMENT_ONLY"), "hover does not include the full diagnostic journal or all requirements")
			check(summary.find_child("InspectionHint", true, false).text == i18n.text("Summary — open card inspection for full details."), "summary directs the player to persistent inspection in the current language")
			summary.free()
	i18n.set_locale("en")
	prefs.text_scale = 1.0
	var complete = load("res://scripts/ui/card_details_panel.gd").new()
	complete.configure(verbose_model, style)
	complete.position = Vector2(600, 100)
	root.add_child(complete)
	await settle()
	var complete_scroll: ScrollContainer = complete.find_child("CardDetailsScroll", true, false)
	check(complete.find_child("DetailedEffects", true, false).rows.size() == 30 and label_texts(complete).contains("ENGINE_JOURNAL_ONLY") and label_texts(complete).contains("SECOND_REQUIREMENT_ONLY"),
		"persistent inspection retains every effect, requirement and diagnostic detail")
	check(complete_scroll.get_v_scroll_bar().visible and complete_scroll.get_v_scroll_bar().max_value > complete_scroll.get_v_scroll_bar().page, "large inspection content has a usable scroll range")
	var wheel := InputEventMouseButton.new()
	wheel.button_index = MOUSE_BUTTON_WHEEL_DOWN
	wheel.pressed = true
	wheel.position = complete_scroll.get_global_rect().get_center()
	var pointer := InputEventMouseMotion.new()
	pointer.position = wheel.position
	root.push_input(pointer, true)
	root.push_input(wheel, true)
	await settle()
	check(complete_scroll.scroll_vertical > 0, "mouse wheel reaches the persistent inspector instead of the hovered card")
	complete_scroll.grab_focus()
	check(complete_scroll.has_focus(), "persistent description can receive keyboard/controller focus")
	check(JSON.stringify(verbose_model) == model_before, "summarizing never truncates or mutates the source model")
	complete.free()
	card.configure(model)
	rules = card.find_child("Rules", true, false)
	prefs.high_contrast = true
	load("res://scripts/ui/app_theme.gd").apply_view_preferences(card)
	check(card.get_theme_stylebox("normal").border_color == style.rarity_color("rare") and rules.numeric_colors[0] == style.higher_value_color, "high contrast preserves semantic colors")
	prefs.text_scale = 1.2
	card.configure(model)
	check(card.find_child("Rules", true, false).get_theme_font_size("normal_font_size") == roundi(style.rules_font_size * 1.2), "rich text respects configured font scale on asynchronous enrichment")
	var custom_style = style.duplicate(true)
	custom_style.higher_value_color = Color("#22ffff")
	custom_style.rarity_colors["rare"] = Color("#ff55cc")
	card.visual_style = custom_style
	card.configure(model)
	check(card.get_theme_stylebox("normal").border_color == custom_style.rarity_color("rare") and card.find_child("Rules", true, false).numeric_colors[0] == custom_style.higher_value_color, "component policy can be overridden without changing card or engine definitions")
	card.free()
	i18n.set_locale("pt_BR")
	check(sections.sections(inspection(3, 6), {}).effectRows[0].segments[0].text.contains("Reduza"), "structured prose is localized while values remain typed")
	i18n.set_locale(previous.locale)
	prefs.text_scale = previous.scale
	prefs.high_contrast = previous.contrast
	root.get_node("GameAudio").shutdown()
	print("SHOWCASE_CARD_PRESENTATION failures=", failures)
	quit(0 if failures == 0 else 1)
