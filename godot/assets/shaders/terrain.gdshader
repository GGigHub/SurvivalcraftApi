shader_type spatial;
render_mode unshaded, cull_disabled;

uniform float sun : hint_range(0.0, 1.0) = 1.0;
uniform sampler2D atlas : filter_nearest, repeat_disable;

void fragment() {
	vec4 tex = texture(atlas, UV);
	if (tex.a < 0.15) {
		discard;
	}
	float daylight = COLOR.a * mix(0.35, 1.0, sun);
	ALBEDO = tex.rgb * COLOR.rgb * max(max(daylight, UV2.x), 0.08);
}
