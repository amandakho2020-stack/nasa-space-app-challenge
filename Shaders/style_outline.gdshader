// Ink outline for the Lunar Outpost art kit (inverted-hull technique, used as next_pass).
// The hull is pushed out in SCREEN space along the mesh's smoothed normal, so every model gets
// the same line weight in pixels regardless of its size or distance - the key to a consistent
// "illustrated" look across props. Smoothed normals are baked into CUSTOM0 by MeshKit; meshes
// without them fall back to NORMAL.
shader_type spatial;
render_mode unshaded, cull_front, shadows_disabled, depth_draw_opaque;

uniform vec4 outline_color : source_color = vec4(0.07, 0.065, 0.075, 1.0);
uniform float outline_px : hint_range(0.0, 8.0) = 2.2;

void vertex() {
	vec3 n = CUSTOM0.xyz;
	if (dot(n, n) < 0.01) {
		n = NORMAL;
	}
	n = normalize(n);
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec3 view_n = normalize(mat3(MODELVIEW_MATRIX) * n);
	vec2 dir = (PROJECTION_MATRIX * vec4(view_n, 0.0)).xy;
	float len = length(dir);
	if (len > 0.0001) {
		dir /= len;
	}
	clip.xy += dir * outline_px * 2.0 / VIEWPORT_SIZE * clip.w;
	POSITION = clip;
}

void fragment() {
	ALBEDO = outline_color.rgb;
}
