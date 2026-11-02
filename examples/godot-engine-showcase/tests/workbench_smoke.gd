extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, message: String) -> void:
    print(("[PASS] " if ok else "[FAIL] ") + message)
    if not ok: failures += 1
func _run() -> void:
    var scene = load("res://scenes/ui/workbenches/ui_catalog_workbench.tscn")
    var instance = scene.instantiate()
    root.add_child(instance)
    for frame in 5: await process_frame
    var hero = instance.get_node("%HeroActorPanel")
    var boss = instance.get_node("%BossActorPanel")
    var card = instance.get_node("%AttackCard")
    var health = instance.get_node("%SampleHealthView")
    var armor = instance.get_node("%SampleArmorView")
    check(hero.presentation_model().actor.instanceId == "hero", "Hero receives authored preview data")
    check(boss.presentation_model().hostile and boss.presentation_model().intent == "Attack 12", "Boss receives authored affiliation and intent")
    check(card.model.name == "Flame Strike" and card.model.costs[0].amount == 2, "Card receives valid presentation costs")
    check(card.get_node("%Rules").get_parsed_text().contains("12"), "Card sample displays its effects")
    check(health.bar.value == 75 and health.bar.max_value == 100, "Health sample uses actual resource bounds")
    check(armor.bar.value == 25 and armor.bar.max_value == 50, "Armor sample uses actual resource bounds")
    check(instance.get_node("%StatusBadgesContainer").get_child_count() == 6, "All six badges are authored scene instances")
    check(not card.animate_entry and card.hand_managed, "Workbench never starts card entry animations")
    var title = card.get_node("%CardName")
    var target = hero.target_button
    instance._populate_workbench()
    check(card.get_node("%CardName") == title and hero.target_button == target, "Refreshing preview preserves authored controls")
    check(card.visual_context == hero.visual_context and card.visual_context.reduced_motion, "Components share an isolated reduced-motion context")
    instance.free()
    quit(1 if failures else 0)
