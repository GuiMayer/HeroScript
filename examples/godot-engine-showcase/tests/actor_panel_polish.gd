extends SceneTree
var failures := 0

func _init() -> void: call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func _run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var panel = preload("res://scripts/ui/actor_panel.gd").create(context)
	root.add_child(panel)

	var actor_data := {
		"instanceId": "golem",
		"definitionId": "stone_golem",
		"name": "Stone Golem",
		"relationship": "Enemy",
		"resources": {
			"health": {"current": 80, "minimum": 0, "maximum": 100},
			"shield": {"current": 15, "minimum": 0, "maximum": 15}
		},
		"statuses": [
			{"statusId": "burn", "stacks": 4, "duration": 2, "isActive": true},
			{"statusId": "wet", "stacks": 1, "duration": 1, "isActive": true},
			{"statusId": "shield", "stacks": 1, "duration": 0, "isActive": true}
		]
	}

	panel.setup(actor_data, true, {}, "Attack 12", false, true)

	# 1. Verify health bar Resource_health exists
	var health_bar = panel.find_child("Resource_health", true, false)
	check(health_bar != null, "health bar exists in actor panel")

	# 2. Verify status badges container exists
	var status_container = panel.find_child("ActorStatusBadges", true, false)
	check(status_container != null and status_container.visible, "status badges container exists and is visible")

	# 3. Verify individual status badges
	var burn_badge = panel.find_child("StatusBadge_burn", true, false)
	var wet_badge = panel.find_child("StatusBadge_wet", true, false)
	var shield_badge = panel.find_child("StatusBadge_shield", true, false)
	check(burn_badge != null and wet_badge != null and shield_badge != null, "all active status badges created")

	# Check burn badge stacks
	var labels: Array[Node] = burn_badge.find_children("*", "Label", true, false)
	var has_stack_4 := labels.any(func(l): return l.text == "4")
	check(has_stack_4, "burn badge displays stack count 4")

	# Check tooltip
	check(burn_badge.tooltip_text.contains("Burn") and burn_badge.tooltip_text.contains("4"), "burn badge tooltip contains name and stacks")

	# 4. Verify relative layout order: Health Bar is ABOVE Target Button (Portrait)
	var content = panel.find_child("ActorContent", true, false)
	check(content != null, "actor content container found")
	var health_idx = content.get_children().find(health_bar.get_parent())
	var status_idx = content.get_children().find(status_container)
	var target_btn = panel.find_child("ActorTarget", true, false)
	var target_idx = content.get_children().find(target_btn)

	check(health_idx >= 0 and status_idx > health_idx and target_idx > status_idx, 
		"layout hierarchy is ordered: Health Bar -> Status Badges -> Target/Portrait (Health is at top)")

	# 5. Test reconciliation when status is removed
	actor_data.statuses = [
		{"statusId": "burn", "stacks": 3, "duration": 1, "isActive": true}
	]
	panel.setup(actor_data, true, {}, "Attack 12", false, true)

	var wet_after = panel.find_child("StatusBadge_wet", true, false)
	check(wet_after == null, "reconciler cleanly removed inactive status badge")

	var burn_after = panel.find_child("StatusBadge_burn", true, false)
	check(burn_after != null, "burn badge retained across reconciliation")

	# 6. Test fit_battlefield_height
	panel.fit_battlefield_height(300.0)
	check(target_btn.custom_minimum_size.y >= 60.0, "battlefield height fits within bounds")

	panel.queue_free()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
