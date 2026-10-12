extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(null, {}, load("res://data/themes/ivory_run.tres"))
	context.set_scenery_profile(load("res://data/themes/ivory_scenery.tres"))
	check(context.profile.printed_surfaces and context.card_style.printed_face, "run reuses the approved printed card anatomy")
	check(context.styles.color("surface.canvas") == Color("#f2ebdd") and context.scenery_profile.ambient_style == "paper", "UI and independent scenery opt into paper explicitly")
	var actors := ["spire_adept", "spire_wisp", "spire_sentinel", "unknown-mod-actor"]
	for id in actors:
		var slot: Dictionary = context.resolve_art("actors", id)
		var texture = load("res://scripts/presentation/art_catalog.gd").texture_for(slot)
		check(texture != null and str(slot.path).begins_with("res://assets/ivory/"), "native printed actor/fallback art resolves: " + id)
	var prior: Resource = context.scenery_profile
	var expected_cards := {
		"basic_attack": "card_strike",
		"arcane_bolt": "card_strike",
		"ascendant_strike": "card_strike",
		"ascendant_fracture": "card_strike",
		"expose_weakness": "card_strike",
		"execution": "card_strike",
		"cull_weakest": "card_strike",
		"w1_strike": "card_strike",
		"w1_sweep": "card_strike",
		"w1_shield_bash": "card_strike",
		"w1_fracture": "card_strike",
		"w1_riposte": "card_strike",
		"w1_pierce": "card_strike",
		"defend": "card_defend",
		"ascendant_kinetic_guard": "card_defend",
		"fading_guard": "card_defend",
		"equilibrium": "card_defend",
		"w1_guard": "card_defend",
		"w1_fortify": "card_defend",
		"w1_bulwark": "card_defend",
		"w1_reserve": "card_defend",
		"w1_dampen": "card_defend",
		"fireball": "card_fire",
		"ascendant_elemental_burst": "card_fire",
		"prismatic_burst": "card_fire",
		"w1_scorch": "card_fire",
		"w1_inferno": "card_fire",
		"heal": "card_heal",
		"cleanse": "card_heal",
		"renewal": "card_heal",
		"w1_mend": "card_heal",
		"w1_renewal": "card_heal",
		"w1_release": "card_condense",
		"w1_release_guard": "card_condense",
		"w1_catalyst": "card_condense",
		"w1_detonate": "card_condense",
		"venom_cut": "card_poison",
		"w1_poison": "card_poison",
		"chain_lightning": "card_multihit",
		"ascendant_support_cascade": "card_multihit",
		"w1_flurry": "card_multihit",
		"w1_barrage": "card_multihit",
		"w1_venom_flurry": "card_multihit",
		"piercing_ray": "card_precision",
		"ascendant_critical_lance": "card_precision",
		"battle_focus": "card_precision",
		"w1_precision": "card_precision",
		"w1_focus": "card_precision",
		"w1_lance": "card_precision",
		"tactical_recall": "card_recall",
		"w1_recall": "card_recall",
		"w1_insight": "card_recall",
		"w1_rehearse": "card_recall",
		"conjure_spark": "card_spark",
		"w1_momentum": "card_spark",
		"core_strike": "core_strike",
		"core_guard": "core_guard",
		"core_heal": "core_heal",
		"core_charge_card": "core_charge_card",
		"core_release": "core_release",
		"core_recover": "core_recover",
		"core_empower_card": "core_empower_card",
	}
	var textures: Dictionary = {}
	for id in expected_cards:
		var slot: Dictionary = context.resolve_art("cards", id)
		check(slot.key == expected_cards[id] and not str(slot.path).ends_with("/unknown.svg") and load("res://scripts/presentation/art_catalog.gd").texture_for(slot) != null, "authored card has imported art: " + id)
		textures[str(slot.path)] = true
	check(textures.size() >= 10, "catalog offers distinct visual families rather than one shared placeholder")
	var lab_manifest := "res://data/labs/ivory_art_manifest.json"
	for id in ["basic_attack", "defend", "fireball", "heal", "arcane_bolt", "core_strike", "core_guard", "core_heal", "core_charge_card", "core_release", "core_recover", "core_empower_card"]:
		var lab: Dictionary = load("res://scripts/presentation/art_catalog.gd").resolve("cards", id, lab_manifest)
		check(context.resolve_art("cards", id).path == lab.path, "run preserves laboratory art: " + id)
	check(str(context.resolve_art("cards", "unknown-mod-card").path).ends_with("/unknown.svg"), "unknown mod content retains safe generic fallback")
	check(context.resource_appearance("energy", {"color": "#eeee00"}).color == "#7e4b15", "profile resource palette overrides presentation tone without changing resource semantics")
	check(context.resource_appearance("custom", {"color": "#123456", "display": "counter"}) == {"color": "#123456", "display": "counter"}, "unknown resources preserve authored presentation")
	context.profile = load("res://data/themes/preserved.tres")
	check(context.scenery_profile == prior, "changing UI does not implicitly change scene art")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
