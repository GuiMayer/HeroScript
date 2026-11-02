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
	var panel = load("res://scripts/ui/components/combat_attributes_drawer.gd").create()
	panel.mount(context)
	root.add_child(panel); panel.present(model, context); await settle()
	var armor: Label = panel.attributes_view._rows["armor"].equipped
	var critical: Label = panel.attributes_view._rows["critical_damage"].equipped
	check(armor.text.ends_with("25") and critical.text.ends_with("100%"), "panel formats engine values without recalculating attributes")
	check(armor.get_theme_color("font_color") == context.styles.color("accent.positive"), "increased attribute uses the shared positive theme role")
	check(critical.get_theme_color("font_color") == context.styles.color("text.primary"), "base attribute uses the shared neutral theme role")
	check(panel.attributes_view.scene_file_path.ends_with("character_attributes_view.tscn"), "combat attributes use the exact shared loadout scene")
	panel.present(model, context); await settle()
	check(panel.attributes_view._rows["armor"].equipped == armor, "identical projections preserve attribute nodes")
	model.attributes.attributes[0].baseValue = 25; model.attributes.attributes[0].effectiveValue = 10
	panel.present(model, context); await settle()
	armor = panel.attributes_view._rows["armor"].equipped
	check(armor.text.ends_with("10") and armor.get_theme_color("font_color") == context.styles.color("accent.danger"), "new projected values update and reduced attributes use red")
	model.attributes.actorId = "another-owner"
	panel.present(model, context); await settle()
	check(not panel.attributes_view._rows.has("armor"), "another owner's attributes are never presented as the player")
	panel.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
