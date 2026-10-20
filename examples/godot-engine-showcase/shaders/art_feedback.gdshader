shader_type canvas_item;
render_mode unshaded;

uniform float impact_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec4 impact_color : source_color = vec4(0.72, 0.22, 0.12, 1.0);

void fragment() {
	vec4 source = texture(TEXTURE, UV) * COLOR;
	source.rgb = mix(source.rgb, impact_color.rgb, impact_amount);
	COLOR = source;
}
