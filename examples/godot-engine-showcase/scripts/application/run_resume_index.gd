extends RefCounted
## Local bookmarks only. Authoritative lifecycle, rules and permanent progress stay in REST.
var runs: Dictionary = {}
var counters: Dictionary = {}

static func key(api: String, player: String, setting: String, mode: String) -> String:
	return JSON.stringify([api.trim_suffix("/"), player, setting, mode])

func candidate(api: String, player: String, setting: String, mode: String) -> String:
	return str(runs.get(key(api, player, setting, mode), ""))

func has_scope(api: String, player: String, setting: String, mode: String) -> bool:
	return runs.has(key(api, player, setting, mode))

func remember(api: String, run: Dictionary) -> bool:
	var setting := str(run.get("settingId", run.get("configName", "")))
	var player := str(run.get("playerEntityId", ""))
	var mode := str(run.get("modeId", ""))
	var id := str(run.get("runId", ""))
	if setting.is_empty() or player.is_empty() or mode.is_empty() or id.is_empty(): return false
	var scope := key(api, player, setting, mode)
	if str(run.get("lifecycle", "")).to_lower() == "active":
		if str(runs.get(scope, "")) == id: return false
		runs[scope] = id
		return true
	if not runs.has(scope) or str(runs.get(scope, "")) == id:
		# A known terminal bookmark must not resurrect an older unfinished run.
		runs[scope] = ""
		return true
	return false

func forget(api: String, player: String, setting: String, mode: String, id: String) -> void:
	var scope := key(api, player, setting, mode)
	if str(runs.get(scope, "")) == id: runs[scope] = ""

func next_counter(api: String, player: String, setting: String, mode: String, legacy_floor: int) -> int:
	var scope := key(api, player, setting, mode)
	counters[scope] = int(counters.get(scope, legacy_floor)) + 1
	return int(counters[scope])

func load_config(config: ConfigFile) -> void:
	var saved_runs = config.get_value("session", "resume_runs", {})
	var saved_counters = config.get_value("session", "campaign_counters", {})
	runs = saved_runs.duplicate(true) if saved_runs is Dictionary else {}
	counters = saved_counters.duplicate(true) if saved_counters is Dictionary else {}

func write_config(config: ConfigFile) -> void:
	config.set_value("session", "resume_runs", runs.duplicate(true))
	config.set_value("session", "campaign_counters", counters.duplicate(true))
