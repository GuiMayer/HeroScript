extends Resource
## Presentation-only resource: optional setting-to-art mapping, never gameplay state.
@export var profile_id := "preserved"
@export var colors: Dictionary = {}
@export var palette: Resource
@export var font_sizes: Dictionary = {}
@export var setting_art_manifests: Dictionary = {}
@export var screen_margin := Vector2(48, 24)
@export var corner_radius := 9
@export var body_font_size := 17
@export var body_font: Font
@export var title_font: Font
@export var printed_surfaces := false
@export var resource_color_overrides: Dictionary = {}
@export var actor_art_max_height := 100.0
@export var compact_actor_hud := false
@export var card_style: CardVisualStyle
@export var art_manifest := "res://data/art_manifest.json"
const DEFAULTS := {
	"text.primary": "#f5ecd8",
	"text.secondary": "#a9a0a8",
	"text.inverse": "#ffffff",
	"accent.warm": "#ee8653",
	"accent.primary": "#e7b75d",
	"accent.positive": "#53c9b5",
	"accent.danger": "#ce5269",
	"surface.canvas": "#111323",
	"surface.panel": "#202236",
	"surface.raised": "#2d2d43",
	"text.disabled": "#656275",
	"stroke.control": "#4b4961",
	"surface.hover": "#3b3546",
	"surface.pressed": "#181927",
	"surface.disabled": "#191a27",
	"stroke.disabled": "#313142",
	"surface.panel_translucent": "#1b1d2edb",
	"stroke.panel": "#3a3b55",
	"surface.input": "#131522",
	"stroke.input": "#44465d",
	"surface.inset": "#11131f",
	"stroke.editor": "#3e4058",
	"text.code": "#d9e6dd",
	"surface.popup": "#171927",
	"text.high_contrast_disabled": "#b7b7bf",
	"surface.high_contrast": "#070911",
	"surface.high_contrast_panel": "#090b15",
	"surface.journey": "#151727d9",
	"surface.target_hover": "#e7b75d18",
	"surface.actor": "#161723bc",
	"accent.power": "#9a86d8",
	"surface.card_art": "#121421",
	"stroke.art_text": "#0b0d16d9",
	"surface.cost_unknown": "#303243",
	"stroke.cost_unknown": "#77798c",
	"resource.unknown": "#666879",
	"change.upgrade": "#79d58c",
	"change.buff": "#62d6bf",
	"change.debuff": "#ee7b78",
	"change.modified": "#b39ce6",
	"resource.block": "#7e9fbd",
	"resource.mana": "#8c75dc",
	"surface.primary_action": "#705033",
	"surface.primary_hover": "#8a6241",
	"surface.primary_pressed": "#4e3522",
	"surface.toast": "#281e2be8",
	"surface.menu": "#171929e8",
	"surface.modal_scrim": "#080912dd",
	"surface.route_current": "#242133",
	"surface.route_locked": "#141623",
	"stroke.route_locked": "#343448",
	"stroke.route": "#575267",
	"surface.settings_navigation": "#171927e8",
	"surface.settings_content": "#202236e8",
	"surface.settings_row": "#171927a8",
	"stroke.settings_row": "#34364d",
	"scenery.base": "#080b18",
	"scenery.gradient_start": "#171228",
	"scenery.gradient_end": "#0d2430",
	"scenery.far": "#121526",
	"scenery.near": "#171a28"
}
func color(role: String) -> Color:
	if not colors.has(role) and palette != null and palette.colors.has(role):
		return palette.color(role)
	return Color.from_string(str(colors.get(role, DEFAULTS.get(role, "#ffffff"))), Color.WHITE)
func role_for(value: Color) -> String:
	for role in DEFAULTS:
		if color(role).is_equal_approx(value): return str(role)
	return ""
func presentation_key() -> String:
	return JSON.stringify([profile_id, colors, palette.presentation_key() if palette != null else "", font_sizes, setting_art_manifests, screen_margin, corner_radius, body_font_size, body_font, title_font, art_manifest, printed_surfaces, resource_color_overrides, actor_art_max_height, compact_actor_hud, card_style.presentation_key() if card_style else ""])


func font_size(role: String, fallback: float) -> float: return float(font_sizes.get(role, fallback))
func manifest_for(setting_id: String) -> String: return str(setting_art_manifests.get(setting_id, art_manifest))
