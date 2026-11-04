extends Node
## Development process lifecycle only. No game rules or session writes.
signal progress_changed(phase: String)
var starting := false
var launcher_pid := -1
var startup_timeout_seconds := 120.0
var _demo_root := ""
var _player_data_root := ""
var _arguments: PackedStringArray

func _init(demo_root := "", player_data_root := "", arguments_override = null) -> void:
	_demo_root = demo_root
	_player_data_root = player_data_root
	_arguments = OS.get_cmdline_user_args() if arguments_override == null else PackedStringArray(arguments_override)

static func launch_plan(url: String, arguments: PackedStringArray, demo_root: String, has_sources: bool, windows: bool) -> Dictionary:
	if not windows or not has_sources or "--layout-smoke" in arguments or "--no-auto-server" in arguments: return {}
	var pattern := RegEx.new()
	pattern.compile("^http://(?:127\\.0\\.0\\.1|localhost):([0-9]{1,5})/?$")
	var parsed := pattern.search(url)
	if parsed == null: return {}
	var port := int(parsed.get_string(1))
	if port < 1024 or port > 65535: return {}
	return {"port": port, "script": demo_root.path_join("Start-Demo.ps1")}

func ensure_started(url: String) -> Dictionary:
	var demo_root := _demo_root if not _demo_root.is_empty() else ProjectSettings.globalize_path("res://").simplify_path()
	var sources := FileAccess.file_exists(demo_root.path_join("../../src/API/API.csproj")) and FileAccess.file_exists(demo_root.path_join("Start-Demo.ps1"))
	var plan := launch_plan(url, _arguments, demo_root, sources, OS.has_feature("windows"))
	# Remote servers, exports without a bundled host and offline tests keep the
	# ordinary connection behavior. Never spawn processes for an arbitrary URL.
	if plan.is_empty(): return {"ok": true}
	if starting: return {"ok": false, "errorKey": "The local engine is already starting."}
	starting = true
	progress_changed.emit("connecting")
	if await _ready_at(url):
		starting = false
		return {"ok": true}
	var attempt := "%s-%s" % [OS.get_process_id(), Time.get_ticks_usec()]
	var progress_path := ProjectSettings.globalize_path("user://startup-%s.json" % attempt)
	progress_changed.emit("preparing_data")
	var arguments := PackedStringArray([
		"-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", plan.script,
		"-ServerOnly", "-AutoStart", "-ClientProcessId", str(OS.get_process_id()), "-Port", str(plan.port), "-ProgressPath", progress_path, "-ProgressAttempt", attempt])
	if not _player_data_root.is_empty(): arguments.append_array(["-PlayerDataRoot", _player_data_root])
	launcher_pid = OS.create_process("powershell.exe", arguments, false)
	if launcher_pid <= 0:
		starting = false
		return {"ok": false, "errorKey": "Could not start the local engine. Check the launcher and .NET installation."}
	var deadline := Time.get_ticks_msec() + int(startup_timeout_seconds * 1000)
	var last_phase := ""
	while is_inside_tree() and Time.get_ticks_msec() < deadline:
		if FileAccess.file_exists(progress_path):
			var progress = JSON.parse_string(FileAccess.get_file_as_string(progress_path))
			if progress is Dictionary and progress.get("attemptId", "") == attempt:
				var phase := str(progress.get("phase", ""))
				if phase == "failed":
					DirAccess.remove_absolute(progress_path)
					starting = false
					return {"ok": false, "errorKey": "Could not start the local engine. Check the launcher and .NET installation."}
				if phase != last_phase and phase in ["preparing_data", "building_server", "starting_server", "loading_settings", "connecting"]:
					last_phase = phase
					progress_changed.emit(phase)
		if await _ready_at(url):
			DirAccess.remove_absolute(progress_path)
			starting = false
			return {"ok": true}
		await get_tree().create_timer(0.5, true).timeout
	DirAccess.remove_absolute(progress_path)
	starting = false
	return {"ok": false, "errorKey": "The local engine did not become ready. Check the engine logs in AppData/HeroScript."}

func _ready_at(url: String) -> bool:
	var request := HTTPRequest.new()
	request.use_threads = true
	request.timeout = 1.0
	add_child(request)
	var status := request.request(url.trim_suffix("/") + "/api/v1/health/ready")
	if status != OK:
		request.queue_free()
		return false
	var result: Array = await request.request_completed
	request.queue_free()
	if result[0] != HTTPRequest.RESULT_SUCCESS or result[1] != 200: return false
	var body = JSON.parse_string(result[3].get_string_from_utf8())
	return body is Dictionary and body.get("status") == "ready"
