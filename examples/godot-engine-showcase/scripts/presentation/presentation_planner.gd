extends RefCounted
## Pure ordered beats. Shares privately-owned immutable traces, never reorders facts.
static func plan(engine_frames: Array) -> Array:
	var result: Array = []
	for frame_index in engine_frames.size():
		var frame: Dictionary = engine_frames[frame_index].duplicate(true)
		var condensed := {}; var parents := {}
		for step in frame.get("effectSteps", []):
			var identity: Dictionary = step.get("identity", {}) if step.get("identity") is Dictionary else {}
			var proc_id := str(identity.get("procId", ""))
			parents[proc_id] = str(identity.get("parentProcId", "")) if identity.get("parentProcId") != null else ""
			if step.get("condensation") != null and not proc_id.is_empty(): condensed[proc_id] = true
		var groups: Array = []
		for application in frame.get("applications", []):
			var identity: Dictionary = application.get("identity", {}) if application.get("identity") is Dictionary else {}
			var proc_id := str(identity.get("procId", ""))
			var root := proc_id
			for _depth in 32:
				if condensed.has(root) or not parents.has(root) or str(parents[root]).is_empty(): break
				root = str(parents[root])
			var key := "condense:" + root if condensed.has(root) else "proc:" + proc_id
			if proc_id.is_empty(): key = "application:" + str(groups.size())
			# Only contiguous applications may share a beat. Never pull A2 past B.
			if groups.is_empty() or groups[-1].key != key: groups.append({"key": key, "applications": [], "condensed": condensed.has(root), "procId": root})
			groups[-1].applications.append(application)
		if groups.is_empty(): groups.append({"key": "transition", "applications": [], "condensed": false, "procId": ""})
		for group_index in groups.size():
			var group: Dictionary = groups[group_index]
			var beat := frame.duplicate(false)
			beat["applications"] = group.applications
			beat["presentationProcId"] = group.procId
			beat["condensed"] = group.condensed
			beat["baseFrameIndex"] = frame_index
			beat["beatId"] = "%s/%s/%s" % [frame.get("frameId", frame_index), group_index, group.key]
			# This snapshot is after the entire engine frame, not an earlier impact.
			if group_index != groups.size() - 1:
				beat.erase("stateAfter"); beat["cardZoneSteps"] = []
			beat["hasVisibleFacts"] = not group.applications.is_empty() or not beat.get("cardZoneSteps", []).is_empty()
			result.append(beat)
	return result
