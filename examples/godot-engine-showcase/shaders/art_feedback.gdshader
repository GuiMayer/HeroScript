shader_type canvas_item;
render_mode unshaded;

uniform float impact_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec4 impact_color : source_color = vec4(0.72, 0.22, 0.12, 1.0);
uniform vec2 quad_size = vec2(240.0);
uniform float outline_width : hint_range(0.0, 4.0) = 0.0;
uniform vec4 outline_color : source_color = vec4(0.12, 0.12, 0.11, 1.0);
uniform bool expand_outline = true;
uniform float sheen_progress = -1.0;
uniform float sheen_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec4 sheen_color : source_color = vec4(0.84, 0.71, 0.46, 1.0);
varying vec4 vertex_tint;

void vertex() {
	vertex_tint = COLOR;
	if (expand_outline && outline_width > 0.0) {
		vec2 padding = vec2(outline_width + 1.0);
		VERTEX += (UV * 2.0 - 1.0) * padding;
		UV = UV * (vec2(1.0) + padding * 2.0 / quad_size) - padding / quad_size;
	}
}

float inside_art(vec2 uv) {
	return step(0.0, uv.x) * step(0.0, uv.y) * step(uv.x, 1.0) * step(uv.y, 1.0);
}

void fragment() {
	vec4 modulation = vertex_tint;
	vec4 source = texture(TEXTURE, clamp(UV, vec2(0.0), vec2(1.0))) * inside_art(UV);
	source.rgb = mix(source.rgb, impact_color.rgb, impact_amount);
	if (sheen_progress >= 0.0 && sheen_amount > 0.0) {
		float diagonal = (UV.x + UV.y * 0.35) / 1.35;
		float center = mix(-0.2, 1.2, sheen_progress);
		float band = 1.0 - smoothstep(0.0, 0.13, abs(diagonal - center));
		source.rgb = mix(source.rgb, sheen_color.rgb, band * sheen_amount * sin(sheen_progress * 3.14159265));
	}
	if (outline_width > 0.0) {
		vec2 offset = vec2(outline_width) / quad_size;
		float ring = 0.0;
		for (int y = -1; y <= 1; y++) {
			for (int x = -1; x <= 1; x++) {
				vec2 neighbor = UV + vec2(float(x), float(y)) * offset;
				ring = max(ring, texture(TEXTURE, clamp(neighbor, vec2(0.0), vec2(1.0))).a * inside_art(neighbor));
			}
		}
		float edge = clamp(ring - source.a, 0.0, 1.0) * outline_color.a;
		float alpha = source.a + edge * (1.0 - source.a);
		source.rgb = (source.rgb * source.a + outline_color.rgb * edge * (1.0 - source.a)) / max(alpha, 0.0001);
		source.a = alpha;
	}
	COLOR = source * modulation;
}
