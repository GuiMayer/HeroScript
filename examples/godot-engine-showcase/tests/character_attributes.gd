extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").from_nodes(root.get_node("Preferences"), root.get_node("I18n"))
	var actor := {"instanceId": "hero", "definitionId": "world-one-hero", "resources": {}, "statuses": [], "controllerBinding": {"kind": "Player"}}
	var attributes := {"actorId": "hero", "criticalRules": [{"ruleId": "direct", "guaranteedTiers": 1, "nextTierChancePercent": 50}], "attributes": [{"attributeId": "armor", "displayName": "Armor", "unit": "points", "baseValue": 0, "effectiveValue": 25},
		{"attributeId": "critical_damage", "displayName": "Critical damage bonus per tier", "unit": "percent", "baseValue": 100, "effectiveValue": 100}]}
	var model := {"presentation": {}, "run": {}, "combat": {}, "actors": [actor], "input_actor": "hero", "zones": {}, "setting": {}, "setting_id": "ascendant", "attributes": attributes}
	var panel = load("res://scripts/ui/components/character_sidebar_view.gd").new()
	panel.visual_context = context
	root.add_child(panel); panel.apply_model(model, context.translator); await settle()
	var armor: Label = panel.find_child("CharacterAttribute_armor", true, false)
	var critical: Label = panel.find_child("CharacterAttribute_critical_damage", true, false)
	check(armor.text.ends_with("25") and critical.text.ends_with("100%"), "panel formats engine values without recalculating attributes")
	check(armor.get_theme_color("font_color") == context.card_style.higher_value_color, "increased attribute uses the shared configurable blue")
	check(critical.get_theme_color("font_color") == context.card_style.base_value_color, "base attribute uses the shared white")
	check(panel.find_child("CharacterCriticalRule_direct", true, false).text.contains("50%"), "tier summary displays the engine's published probability")
	panel.apply_model(model, context.translator); await settle()
	check(panel.find_child("CharacterAttribute_armor", true, false) == armor, "identical projections preserve attribute nodes")
	model.attributes.attributes[0].baseValue = 25; model.attributes.attributes[0].effectiveValue = 10
	panel.apply_model(model, context.translator); await settle()
	armor = panel.find_child("CharacterAttribute_armor", true, false)
	check(armor.text.ends_with("10") and armor.get_theme_color("font_color") == context.card_style.lower_value_color, "new projected values update and reduced attributes use red")
	model.attributes.actorId = "another-owner"
	panel.apply_model(model, context.translator); await settle()
	check(panel.find_child("CharacterAttribute_armor", true, false) == null, "another owner's attributes are never presented as the player")
	panel.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
