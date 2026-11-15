extends SceneTree
## Validates the published MVP card catalog without evaluating combat rules.
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, message: String) -> void:
	print("[PASS] " if value else "[FAIL] ", message)
	if not value: failures += 1
func read_json(path: String) -> Dictionary:
	var file = FileAccess.open(ProjectSettings.globalize_path("res://../../data/configs/prototype-test/Resources/" + path), FileAccess.READ)
	if file == null: return {}
	var data = JSON.parse_string(file.get_as_text())
	return data if data is Dictionary else {}
func run() -> void:
	var cards := read_json("cards.json"); cards.merge(read_json("thin-slice-cards.json"))
	var policy := read_json("deck-composition-policies.json"); var modules := read_json("deck-modules.json")
	var pools := read_json("card-pools.json"); var ids: Array = pools.get("prototype_rewards", {}).get("explicitCardIds", []).duplicate()
	for id in policy.get("prototype_build", {}).get("groups", [])[0].get("cardIds", []):
		if id not in ids: ids.append(id)
	for module in modules.values():
		for id in module.get("cardIds", []):
			if id not in ids: ids.append(id)
	var themes := ["fire", "void", "shadow", "divine"]
	check(not ids.is_empty(), "MVP catalog is available")
	for id in ids:
		if not cards.has(id): check(false, "missing card: %s" % id); continue
		var card: Dictionary = cards[id]; var tags: Array = card.get("tags", [])
		var selected: Array = tags.filter(func(tag): return tag in themes)
		check(selected.size() >= 1 and selected.size() <= 3 and selected.size() == selected.duplicate().reduce(func(acc, tag): if tag not in acc: acc.append(tag); return acc, []).size(), "one to three unique thematic tags: %s" % id)
		for component in card.get("components", []):
			var effect: Dictionary = component.get("effect", {})
			if effect.get("type") == "HEAL" or (effect.get("type") == "MODIFY_RESOURCE" and effect.get("targetResource") == "block" and effect.get("operation") == "ADD"):
				check("divine" in selected and effect.get("calculationPipelineId") == "prototype_direct_amount", "divine scaling on defense or healing: %s" % id)
	for id in ["prototype_guarded_insight", "prototype_cinder_recall", "prototype_final_spark"]:
		check(id in pools.get("prototype_rewards", {}).get("explicitCardIds", []), "new card is in reward pool: %s" % id)
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
