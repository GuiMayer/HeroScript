extends SceneTree

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	print("[TEST] Loading ui_catalog_workbench.tscn...")
	var scene = load("res://scenes/ui/workbenches/ui_catalog_workbench.tscn")
	if scene == null:
		printerr("[FAIL] Could not load ui_catalog_workbench.tscn")
		quit(1)
		return
	
	var instance = scene.instantiate()
	if instance == null:
		printerr("[FAIL] Could not instantiate ui_catalog_workbench.tscn")
		quit(1)
		return
	
	root.add_child(instance)
	for frame in 5:
		await process_frame
		
	var hero = instance.find_child("HeroActorPanel", true, false)
	var boss = instance.find_child("BossActorPanel", true, false)
	var attack_card = instance.find_child("AttackCard", true, false)
	var badges = instance.find_child("StatusBadgesContainer", true, false)
	
	if hero == null or boss == null or attack_card == null or badges == null:
		printerr("[FAIL] One or more workbench components could not be located in tree")
		quit(1)
		return
		
	print("[PASS] Workbench instantiated and components populated successfully")
	print("[PASS] Hero ActorPanel: ", hero.name)
	print("[PASS] Boss ActorPanel: ", boss.name)
	print("[PASS] Attack Card: ", attack_card.name)
	print("[PASS] Badges count: ", badges.get_child_count())
	instance.free()
	quit(0)
