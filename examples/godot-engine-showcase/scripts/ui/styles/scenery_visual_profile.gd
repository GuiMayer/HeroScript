extends Resource
## Independent visual configuration. Never contains game rules or command state.
@export var profile_id := "preserved-scenery"
@export var ambient_style := "ember"
@export var art_manifest := "res://data/art_manifest.json"
@export var setting_art_manifests: Dictionary = {}
@export var mappings := "res://data/scenery.json"
@export var colors: Dictionary = {}
@export var palette: Resource
@export var palette_aliases: Dictionary = {}
const DEFAULTS := {
	"scenery.base": "#080b18", "scenery.gradient_start": "#171228", "scenery.gradient_end": "#0d2430",
	"scenery.far": "#121526", "scenery.near": "#171a28"
}
func color(role: String) -> Color:
	if not colors.has(role) and palette != null and palette_aliases.has(role):
		return palette.color(str(palette_aliases[role]))
	return Color.from_string(str(colors.get(role, DEFAULTS.get(role, "#ffffff"))), Color.WHITE)
func manifest_for(setting: String) -> String:
	return str(setting_art_manifests.get(setting, art_manifest))
func presentation_key() -> String:
	return JSON.stringify([profile_id, ambient_style, art_manifest, setting_art_manifests, mappings, colors, palette.presentation_key() if palette != null else "", palette_aliases])
