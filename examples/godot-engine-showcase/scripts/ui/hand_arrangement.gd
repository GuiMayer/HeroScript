extends RefCounted
## Presentation-only identities. Never changes a zone, model or engine command.
var canonical: Array[String] = []
var confirmed: Array[String] = []
var preview: Array[String] = []
var manual := false
var dragged := ""
static func reconcile(present: Array, preferred: Array) -> Array[String]:
	var result: Array[String] = []
	for id in preferred:
		if str(id) in present and str(id) not in result: result.append(str(id))
	for id in present:
		if not str(id).is_empty() and str(id) not in result: result.append(str(id))
	return result
static func insert(ids: Array, id: String, index: int) -> Array[String]:
	var result: Array[String] = reconcile(ids, ids)
	if id not in result: return result
	result.erase(id); result.insert(clampi(index, 0, result.size()), id)
	return result
func sync(ids: Array) -> bool:
	var changed := canonical != ids
	if changed: cancel()
	canonical = reconcile(ids, ids)
	confirmed = reconcile(canonical, confirmed) if manual else canonical.duplicate()
	return changed
func project(ids: Array) -> Array[String]:
	return reconcile(ids, confirmed) if manual else reconcile(ids, ids)
func begin(id: String) -> void:
	dragged = id if id in confirmed else ""
	preview = confirmed.duplicate()
func move(index: int) -> bool:
	if dragged.is_empty(): return false
	var next := insert(confirmed, dragged, index)
	if next == preview: return false
	preview = next
	return true
func commit() -> bool:
	var changed := not dragged.is_empty() and preview != confirmed
	if changed: confirmed = preview.duplicate(); manual = true
	cancel()
	return changed
func cancel() -> void: dragged = ""; preview.clear()
