extends RefCounted
## Cosmetic overlay on an existing profile: fonts, dimensions and motion are inherited.
static func create(base: Resource) -> Resource:
	var profile := base.duplicate()
	profile.colors = base.colors.duplicate(true)
	profile.colors.merge({
		"surface.canvas": "#f2ebdd", "surface.panel": "#faf5eb",
		"surface.panel_translucent": "#faf5eb", "surface.popup": "#faf5eb",
		"surface.raised": "#eee4d1", "surface.hover": "#e8dfcf",
		"surface.pressed": "#d8cbb7", "surface.card_art": "#f2ebdd",
		"stroke.control": "#5b574f", "stroke.panel": "#5b574f",
		"stroke.input": "#5b574f", "stroke.editor": "#5b574f",
		"stroke.art_text": "#faf5eb", "surface.inset": "#e5dbc8"
	}, true)
	profile.card_style = base.card_style.duplicate()
	profile.card_style.keyline_style = "registration"
	# Asset catalogue is selected by the lab, independently of this palette.
	return profile
