shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform sampler2D burn_noise : repeat_disable, filter_linear;
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform int direction : hint_enum("Center", "Edges") = 0;
uniform vec2 card_size = vec2(248.0, 328.0);
uniform float irregularity : hint_range(0.0, 0.4) = 0.14;
uniform float edge_width : hint_range(0.001, 0.1) = 0.025;
uniform float softness : hint_range(0.001, 0.05) = 0.008;
uniform vec4 ember_color : source_color = vec4(0.9, 0.2, 0.055, 1.0);
uniform vec4 hot_color : source_color = vec4(1.0, 0.71, 0.3, 1.0);
uniform vec4 char_color : source_color = vec4(0.09, 0.055, 0.035, 1.0);

void fragment() {
	// A transparent SubViewport produces premultiplied color. Keep this contract
	// for its anti-aliased card outline and for the disappearing burn frontier.
	vec4 source = texture(TEXTURE, UV);
	if (progress <= 0.0) {
		COLOR = source;
	} else if (progress >= 1.0) {
		COLOR = vec4(0.0);
	} else {
		vec2 pixels = (UV - vec2(0.5)) * card_size;
		float field = length(pixels) / max(length(card_size * 0.5), 1.0);
		if (direction == 1) {
			vec2 edge = min(UV, vec2(1.0) - UV) * card_size;
			field = min(edge.x, edge.y) / max(min(card_size.x, card_size.y) * 0.5, 1.0);
		}
		float noise = texture(burn_noise, UV).r;
		field = clamp(field + (noise - 0.5) * irregularity, 0.0, 1.0);
		float distance = field - progress;
		float remaining = smoothstep(-softness, softness, distance);
		float charred = 1.0 - smoothstep(edge_width, edge_width * 3.0, distance);
		float ember = 1.0 - smoothstep(0.0, edge_width, abs(distance));
		vec3 paper = source.rgb / max(source.a, 0.0001);
		paper = mix(paper, char_color.rgb, charred * 0.95);
		vec3 rim = mix(ember_color.rgb, hot_color.rgb, ember * ember);
		paper = mix(paper, rim, ember);
		float alpha = source.a * remaining;
		COLOR = vec4(paper * alpha, alpha);
	}
}
