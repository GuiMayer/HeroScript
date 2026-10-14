extends Control
## GPU-only snapshot of the shared CardView. Never captures the screen or reads pixels to CPU.
signal snapshot_ready
const BurnShader = preload("res://shaders/card_burn.gdshader")
const BurnNoisePath := "res://data/card_burn_noise.tres"
var capture_viewport: SubViewport
var output: TextureRect
var burn_material: ShaderMaterial
var progress := 0.0
var _headless_pending := false

func setup(source: CardView, direction: String) -> void:
	name = "CardBurnView"
	process_mode = Node.PROCESS_MODE_PAUSABLE
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	size = source.size
	pivot_offset = size * .5
	rotation = source.rotation
	scale = source.scale
	global_position = source.global_position
	z_index = source.z_index
	capture_viewport = SubViewport.new()
	capture_viewport.name = "CardSnapshot"
	capture_viewport.transparent_bg = true
	capture_viewport.disable_3d = true
	capture_viewport.gui_disable_input = true
	# Oversample the short-lived snapshot to keep text sharp during scaling.
	capture_viewport.size = Vector2i(size.ceil()) * 2
	capture_viewport.size_2d_override = Vector2i(size.ceil())
	capture_viewport.size_2d_override_stretch = true
	capture_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	add_child(capture_viewport)
	var face := CardView.new(source.visual_context)
	face.name = "CapturedCardFace"
	face.visual_style = source.visual_style
	face.hand_managed = true
	face.animate_entry = false
	face.focus_mode = Control.FOCUS_NONE
	face.mouse_filter = Control.MOUSE_FILTER_IGNORE
	face.configure(source.model)
	capture_viewport.add_child(face)
	face.size = size
	output = TextureRect.new()
	output.name = "BurnedFace"
	output.mouse_filter = Control.MOUSE_FILTER_IGNORE
	output.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	output.texture = capture_viewport.get_texture()
	burn_material = ShaderMaterial.new()
	burn_material.shader = BurnShader
	# Dummy/headless rendering cannot use noise pixels. Do not start its threaded
	# texture generator there; rendered callers reuse the lab's warmed resource.
	if DisplayServer.get_name() != "headless":
		burn_material.set_shader_parameter("burn_noise", load(BurnNoisePath))
	burn_material.set_shader_parameter("card_size", size)
	burn_material.set_shader_parameter("direction", 1 if direction == "edges" else 0)
	set_progress(0.0)
	output.material = burn_material
	add_child(output)
	output.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	output.hide()
	if DisplayServer.get_name() == "headless": _headless_pending = true
	else: RenderingServer.frame_post_draw.connect(_on_snapshot_ready, CONNECT_ONE_SHOT)

func _process(_delta: float) -> void:
	if _headless_pending:
		_headless_pending = false
		_on_snapshot_ready()

func _on_snapshot_ready() -> void:
	set_process(false)
	snapshot_ready.emit()

func set_progress(value: float) -> void:
	progress = clampf(value, 0, 1)
	burn_material.set_shader_parameter("progress", progress)

func _exit_tree() -> void:
	if RenderingServer.frame_post_draw.is_connected(_on_snapshot_ready):
		RenderingServer.frame_post_draw.disconnect(_on_snapshot_ready)
