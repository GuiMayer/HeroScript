extends "res://tests/fixtures/deck_composition_session.gd"
## API-shaped disabled optional sections, including explicit JSON null fields.
var compartments_enabled := false
var composition_enabled := false
var last_compartments := {}
func read_deck_compartments() -> Dictionary:
	if compartments_enabled: return super.read_deck_compartments()
	var data := snapshot()
	data.merge({"enabled": false, "canChange": false, "unavailableReason": "Character or mode has no deck compartments", "profile": null, "allocations": [], "cards": []}, true)
	last_compartments = data.duplicate(true)
	return {"ok": true, "data": data}
func read_deck_loadout() -> Dictionary:
	if composition_enabled: return super.read_deck_loadout()
	var data := snapshot()
	data.merge({"enabled": false, "canChange": false, "unavailableReason": "This mode has no deck composition", "policy": null, "selection": null, "cards": []}, true)
	return {"ok": true, "data": data}
