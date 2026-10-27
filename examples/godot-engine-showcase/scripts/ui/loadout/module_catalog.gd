extends RefCounted
## Presentation modules, not gameplay policies or inventories.
static func entries() -> Array:
	return [{"id": "overview", "label": "Overview"}, {"id": "inscriptions", "label": "Inscriptions"},
		{"id": "deck", "label": "Deck"}, {"id": "relics", "label": "Relics"},
		{"id": "companions", "label": "Companions · Planned"}]

