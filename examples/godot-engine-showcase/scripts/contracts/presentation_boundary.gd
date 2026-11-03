extends RefCounted
## Boundary validator and DTO-to-model adapter for presentation boundaries.
## Validates required fields, types, UUIDs/identifiers, and revision context without fabricating
## synthetic valid actions, while preserving optional/unknown extension fields and the last valid state.

const ViewModels = preload("res://scripts/presentation/view_models.gd")
const SnapshotProjection = preload("res://scripts/contracts/snapshot_projection.gd")

static func is_valid_id(value, allow_empty := false) -> bool:
	if not (value is String):
		return false
	var text := str(value).strip_edges()
	if text.is_empty():
		return allow_empty
	return text == str(value) and not text.contains("\n") and not text.contains("\r")

static func is_valid_uuid(value) -> bool:
	if not (value is String):
		return false
	var s := str(value)
	if s.length() != 36:
		return false
	for i in 36:
		var ch := s.unicode_at(i)
		if i in [8, 13, 18, 23]:
			if ch != 45: # '-'
				return false
		else:
			var is_digit := ch >= 48 and ch <= 57
			var is_lower := ch >= 97 and ch <= 102
			var is_upper := ch >= 65 and ch <= 70
			if not (is_digit or is_lower or is_upper):
				return false
	return true

static func validate_session_identity(run_dto, combat_dto = {}, fallback_revision := "", fallback_setting := "") -> Dictionary:
	var errors: Array[String] = []
	if not (run_dto is Dictionary):
		errors.append("run snapshot must be a Dictionary")
		return {"ok": false, "errors": errors, "identity": ViewModels.SessionIdentity.new({})}
	if not (combat_dto is Dictionary):
		errors.append("combat snapshot must be a Dictionary")
		return {"ok": false, "errors": errors, "identity": ViewModels.SessionIdentity.new({})}
	if not run_dto.is_empty():
		if not is_valid_id(run_dto.get("runId")):
			errors.append("run snapshot requires a non-empty String runId")
		if run_dto.has("sequence"):
			var seq = run_dto.get("sequence")
			if not (seq is int or seq is float) or int(seq) < 0:
				errors.append("run snapshot sequence must be a non-negative number")
	if not combat_dto.is_empty():
		if combat_dto.has("combatId") and combat_dto.get("combatId") != null and not is_valid_id(combat_dto.get("combatId"), true):
			errors.append("combat snapshot combatId must be a valid String")
		if combat_dto.has("runId") and run_dto.has("runId") and is_valid_id(run_dto.get("runId")):
			if str(combat_dto.get("runId", "")) != str(run_dto.get("runId", "")):
				errors.append("combat snapshot runId does not match active runId")
	var identity := ViewModels.SessionIdentity.from_snapshots(run_dto, combat_dto, fallback_revision, fallback_setting)
	return {"ok": errors.is_empty(), "errors": errors, "identity": identity}

static func matches_revision_context(identity: ViewModels.SessionIdentity, incoming_dto) -> Dictionary:
	if identity == null:
		return {"ok": false, "reason": "missing session identity"}
	if not (incoming_dto is Dictionary):
		return {"ok": false, "reason": "incoming version payload is not a Dictionary"}
	var version: Dictionary = incoming_dto.get("version", incoming_dto) if incoming_dto.get("version") is Dictionary else incoming_dto
	if not identity.matches_version(version):
		return {"ok": false, "reason": "version mismatch against active run/sequence/combat/revision context"}
	return {"ok": true, "reason": ""}

static func adapt_actors(raw_actors) -> Dictionary:
	var errors: Array[String] = []
	var models: Array = []
	var items: Array = []
	if not (raw_actors is Array):
		errors.append("actors collection must be an Array")
		return {"ok": false, "errors": errors, "models": models, "items": items}
	for index in raw_actors.size():
		var entry = raw_actors[index]
		if not (entry is Dictionary):
			errors.append("actor[%d] is not a Dictionary" % index)
			continue
		if not is_valid_id(entry.get("instanceId")):
			errors.append("actor[%d] is missing a valid String instanceId" % index)
			continue
		var sanitized: Dictionary = entry.duplicate(true)
		if sanitized.has("resources") and not (sanitized.get("resources") is Dictionary):
			errors.append("actor[%d].resources is not a Dictionary" % index)
			sanitized["resources"] = {}
		if sanitized.has("statuses") and not (sanitized.get("statuses") is Array):
			errors.append("actor[%d].statuses is not an Array" % index)
			sanitized["statuses"] = []
		var model := ViewModels.Actor.new(sanitized)
		models.append(model)
		items.append(model.to_dictionary())
	return {"ok": errors.is_empty(), "errors": errors, "models": models, "items": items}

static func adapt_cards(raw_cards) -> Dictionary:
	var errors: Array[String] = []
	var models: Array = []
	var items: Array = []
	if not (raw_cards is Array):
		errors.append("cards collection must be an Array")
		return {"ok": false, "errors": errors, "models": models, "items": items}
	for index in raw_cards.size():
		var entry = raw_cards[index]
		if not (entry is Dictionary):
			errors.append("card[%d] is not a Dictionary" % index)
			continue
		if not is_valid_id(entry.get("cardInstanceId")):
			errors.append("card[%d] is missing a valid String cardInstanceId" % index)
			continue
		var model := ViewModels.Card.new(entry)
		models.append(model)
		items.append(model.to_dictionary())
	return {"ok": errors.is_empty(), "errors": errors, "models": models, "items": items}

static func adapt_legal_actions(raw_actions) -> Dictionary:
	var errors: Array[String] = []
	var models: Array = []
	var items: Array = []
	if not (raw_actions is Array):
		errors.append("legal actions collection must be an Array")
		return {"ok": false, "errors": errors, "models": models, "items": items}
	for index in raw_actions.size():
		var entry = raw_actions[index]
		if not (entry is Dictionary):
			errors.append("legal_action[%d] is not a Dictionary" % index)
			continue
		var model := ViewModels.LegalAction.new(entry)
		if not model.is_valid():
			errors.append("legal_action[%d] has invalid command or missing actionType/cardInstanceId" % index)
			continue
		models.append(model)
		items.append(model.to_dictionary())
	return {"ok": errors.is_empty(), "errors": errors, "models": models, "items": items}

static func adapt_zones(raw_zone_snapshot) -> Dictionary:
	var errors: Array[String] = []
	var models: Array = []
	var zones: Array = []
	if not (raw_zone_snapshot is Dictionary):
		errors.append("zone snapshot must be a Dictionary")
		return {"ok": false, "errors": errors, "models": models, "zones": zones}
	var raw_list = raw_zone_snapshot.get("zones", [])
	if not (raw_list is Array):
		errors.append("zone snapshot 'zones' must be an Array")
		return {"ok": false, "errors": errors, "models": models, "zones": zones}
	for index in raw_list.size():
		var entry = raw_list[index]
		if not (entry is Dictionary):
			errors.append("zone[%d] is not a Dictionary" % index)
			continue
		if not is_valid_id(entry.get("zoneId")):
			errors.append("zone[%d] is missing a valid String zoneId" % index)
			continue
		var sanitized: Dictionary = entry.duplicate(true)
		var count_raw = sanitized.get("count", 0)
		if count_raw != null and not (count_raw is int or count_raw is float):
			errors.append("zone[%d] count is not numeric" % index)
			continue
		if int(count_raw if count_raw != null else 0) < 0:
			errors.append("zone[%d] count cannot be negative" % index)
			continue
		if sanitized.has("cards"):
			if not (sanitized.get("cards") is Array):
				errors.append("zone[%d].cards is not an Array" % index)
				sanitized["cards"] = []
			else:
				var card_adaptation := adapt_cards(sanitized.get("cards"))
				if not card_adaptation.ok:
					errors.append_array(card_adaptation.errors)
				sanitized["cards"] = card_adaptation.items
		var model := ViewModels.Zone.new(sanitized)
		models.append(model)
		zones.append(model.to_dictionary())
	return {"ok": errors.is_empty(), "errors": errors, "models": models, "zones": zones}

class StateKeeper extends RefCounted:
	## Preserves the last valid presentation snapshot when an incoming response is malformed or stale.
	var last_snapshot: Dictionary = {}
	var last_identity: ViewModels.SessionIdentity = ViewModels.SessionIdentity.new({})
	var last_errors: Array[String] = []

	func accept_snapshot(incoming: Dictionary, allow_branch_switch := false) -> Dictionary:
		last_errors.clear()
		if not (incoming is Dictionary):
			last_errors.append("incoming snapshot must be a Dictionary")
			return {"accepted": false, "errors": last_errors.duplicate(), "snapshot": last_snapshot.duplicate(true)}
		var run_dto = incoming.get("run", {})
		var combat_dto = incoming.get("combat", {})
		var actions_dto = incoming.get("actions", [])
		var zones_dto = incoming.get("zones", {})
		var id_check := preload("res://scripts/contracts/presentation_boundary.gd").validate_session_identity(run_dto, combat_dto)
		if not id_check.ok:
			last_errors.append_array(id_check.errors)
			return {"accepted": false, "errors": last_errors.duplicate(), "snapshot": last_snapshot.duplicate(true)}
		var next_id: ViewModels.SessionIdentity = id_check.identity
		if last_identity.is_valid_run() and next_id.is_valid_run():
			if next_id.run_id == last_identity.run_id and next_id.branch_id == last_identity.branch_id and not allow_branch_switch:
				if next_id.run_sequence < last_identity.run_sequence:
					last_errors.append("stale runSequence %d < current %d" % [next_id.run_sequence, last_identity.run_sequence])
					return {"accepted": false, "errors": last_errors.duplicate(), "snapshot": last_snapshot.duplicate(true)}
				if next_id.run_sequence == last_identity.run_sequence and not next_id.combat_id.is_empty() and next_id.combat_id == last_identity.combat_id:
					if next_id.combat_step < last_identity.combat_step:
						last_errors.append("stale combatStep %d < current %d" % [next_id.combat_step, last_identity.combat_step])
						return {"accepted": false, "errors": last_errors.duplicate(), "snapshot": last_snapshot.duplicate(true)}
		var actions_check := preload("res://scripts/contracts/presentation_boundary.gd").adapt_legal_actions(actions_dto)
		if not actions_check.ok:
			last_errors.append_array(actions_check.errors)
		var zones_check := preload("res://scripts/contracts/presentation_boundary.gd").adapt_zones(zones_dto)
		if not zones_check.ok:
			last_errors.append_array(zones_check.errors)
		var projected_run := SnapshotProjection.run(run_dto)
		var projected_combat := SnapshotProjection.combat(combat_dto)
		var accepted_snapshot := incoming.duplicate(true)
		accepted_snapshot["run"] = projected_run
		accepted_snapshot["combat"] = projected_combat
		accepted_snapshot["actions"] = actions_check.items
		accepted_snapshot["zones"] = {"zones": zones_check.zones} if zones_dto is Dictionary and zones_dto.has("zones") else zones_dto.duplicate(true)
		last_snapshot = accepted_snapshot.duplicate(true)
		last_identity = next_id
		return {"accepted": true, "errors": last_errors.duplicate(), "snapshot": last_snapshot.duplicate(true)}
