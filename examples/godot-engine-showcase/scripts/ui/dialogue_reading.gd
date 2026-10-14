extends RefCounted
## Local reading only; pages are not engine nodes and cannot execute choices.
var scope := ""
var pages: Array[String] = []
var page := 0
var revealed := 0.0
var gradual := false
func bind(model: Dictionary, locale: String, budget := 650) -> void:
	var key := JSON.stringify([model.get("runId"), model.get("branchId"), model.get("dialogueInstanceId"), model.get("nodeId"), model.get("contentRevision"), locale, model.get("text")])
	if key == scope: return
	scope = key; pages.clear(); page = 0; revealed = 0.
	var current := ""
	for word in str(model.get("text", "")).split(" "):
		if not current.is_empty() and current.length() + word.length() > budget:
			pages.append(current); current = ""
		current += (" " if not current.is_empty() else "") + word
	pages.append(current)
func text() -> String: return pages[page] if not pages.is_empty() else ""
func fully_revealed() -> bool: return not gradual or revealed >= text().length()
func last_page() -> bool: return page >= pages.size() - 1
func advance_reading() -> bool:
	if not fully_revealed(): revealed = text().length(); return true
	if not last_page(): page += 1; revealed = 0.; return true
	return false
func capture() -> Dictionary: return {"scope": scope, "page": page, "revealed": revealed}
func restore(state: Dictionary) -> void:
	if str(state.get("scope", "")) != scope: return
	page = clampi(int(state.get("page", 0)), 0, maxi(0, pages.size() - 1))
	revealed = maxf(0., float(state.get("revealed", 0.)))
