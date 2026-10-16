extends RefCounted
## Local reading state only. Edges never grant permission to travel.
var nodes: Dictionary = {}
var current := ""
var selected := ""
var hovered := ""
var focused := ""

func update(projection: Array) -> void:
	nodes.clear(); current = ""
	for item in projection:
		var id := str(item.get("id", ""))
		if id.is_empty() or nodes.has(id): continue
		nodes[id] = item.duplicate(true)
		if item.get("current", false): current = id
	if not nodes.has(selected): selected = current
	if not nodes.has(hovered): hovered = ""
	if not nodes.has(focused): focused = ""

func alternatives() -> Array:
	var result: Array = []
	for id in nodes:
		if not nodes[id].get("travel", {}).is_empty(): result.append(id)
	result.sort()
	return result

func decision_ids() -> Array:
	var result := alternatives()
	if nodes.has(current) and current not in result: result.append(current)
	return result

func inspect(id: String) -> void:
	if nodes.has(id): selected = id
