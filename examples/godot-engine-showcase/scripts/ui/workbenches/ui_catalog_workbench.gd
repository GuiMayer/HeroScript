@tool
class_name UICatalogWorkbench
extends Control

## Visual Workbench for testing and fine-tuning UI components in isolation (F6 or Editor)

@onready var hero_panel = get_node_or_null("%HeroActorPanel")
@onready var boss_panel = get_node_or_null("%BossActorPanel")
@onready var card_attack = get_node_or_null("%AttackCard")
@onready var card_skill = get_node_or_null("%SkillCard")
@onready var card_power = get_node_or_null("%PowerCard")
@onready var health_view = get_node_or_null("%SampleHealthView")
@onready var armor_view = get_node_or_null("%SampleArmorView")
@onready var status_container = get_node_or_null("%StatusBadgesContainer")

func _ready() -> void:
	_populate_workbench()

func _populate_workbench() -> void:
	var visual_ctx = preload("res://scripts/ui/visual_context.gd").new()
	
	# Populate sample actor panels
	if hero_panel and hero_panel.has_method("configure"):
		hero_panel.configure({
			"actorId": "hero",
			"name": "Valerius (Hero)",
			"team": "player",
			"active": true,
			"resources": {
				"health": {"current": 82, "max": 100, "resourceId": "health"},
				"armor": {"current": 15, "max": 50, "resourceId": "armor"},
				"energy": {"current": 3, "max": 3, "resourceId": "energy"}
			},
			"statuses": [
				{"statusId": "strength", "stacks": 2, "name": "Strength"},
				{"statusId": "focus", "stacks": 1, "name": "Focus"},
				{"statusId": "burn", "stacks": 3, "name": "Burn"}
			]
		})
	
	if boss_panel and boss_panel.has_method("configure"):
		boss_panel.configure({
			"actorId": "boss",
			"name": "Ignis Archon (Boss)",
			"team": "enemy",
			"active": false,
			"resources": {
				"health": {"current": 240, "max": 300, "resourceId": "health"},
				"armor": {"current": 40, "max": 100, "resourceId": "armor"}
			},
			"statuses": [
				{"statusId": "rage", "stacks": 4, "name": "Rage"},
				{"statusId": "vulnerable", "stacks": 2, "name": "Vulnerable"}
			]
		})
		
	# Populate sample cards
	if card_attack and card_attack.has_method("configure"):
		card_attack.visual_context = visual_ctx
		card_attack.configure({
			"cardInstanceId": "c_flame_strike",
			"definitionId": "flame_strike",
			"name": "Flame Strike",
			"tone": "attack",
			"rarityId": "uncommon",
			"rarity": "Uncommon",
			"cost": {"energy": 2},
			"summary": "Deal 12 fire damage.\nApply 3 Burn."
		})
		
	if card_skill and card_skill.has_method("configure"):
		card_skill.visual_context = visual_ctx
		card_skill.configure({
			"cardInstanceId": "c_iron_defense",
			"definitionId": "iron_defense",
			"name": "Iron Defense",
			"tone": "skill",
			"rarityId": "common",
			"rarity": "Common",
			"cost": {"energy": 1},
			"summary": "Gain 14 Armor.\nRetain 1 card."
		})
		
	if card_power and card_power.has_method("configure"):
		card_power.visual_context = visual_ctx
		card_power.configure({
			"cardInstanceId": "c_ascendance",
			"definitionId": "ascendance",
			"name": "Ascendance",
			"tone": "power",
			"rarityId": "rare",
			"rarity": "Rare",
			"cost": {"energy": 3},
			"summary": "At start of turn, draw 1 card and gain 1 Energy."
		})
		
	# Populate standalone resources
	if health_view and health_view.has_method("set_resource"):
		health_view.set_resource("health", 75, 100)
	if armor_view and armor_view.has_method("set_resource"):
		armor_view.set_resource("armor", 25, 50)
		
	# Populate status badges container
	if status_container and status_container.get_child_count() == 0:
		var sample_badges = [
			{"id": "strength", "stacks": 3, "name": "Strength"},
			{"id": "focus", "stacks": 1, "name": "Focus"},
			{"id": "burn", "stacks": 4, "name": "Burn"},
			{"id": "poison", "stacks": 2, "name": "Poison"},
			{"id": "vulnerable", "stacks": 1, "name": "Vulnerable"},
			{"id": "shield_wall", "stacks": 5, "name": "Shield Wall"}
		]
		var badge_scene = preload("res://scenes/ui/components/status_badge.tscn")
		for badge_data in sample_badges:
			var badge = badge_scene.instantiate()
			badge.apply_status({"statusId": badge_data.id, "stacks": badge_data.stacks, "name": badge_data.name}, visual_ctx)
			status_container.add_child(badge)
