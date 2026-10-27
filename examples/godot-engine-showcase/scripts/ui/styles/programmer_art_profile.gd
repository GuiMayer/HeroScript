extends RefCounted
## Cosmetic overlay on an existing profile: fonts, dimensions and motion are inherited.
static func create(base: Resource) -> Resource:
	var profile := base.duplicate()
	profile.colors = base.colors.duplicate(true)
	profile.card_style = base.card_style.duplicate()
	profile.card_style.keyline_style = "registration"
	# Asset catalogue is selected by the lab, independently of this palette.
	return profile
