// Procedural lunar regolith.
// All detail comes from seamless NoiseTexture2D (FastNoiseLite) inputs sampled in WORLD space,
// so any CSG/mesh using this material gets continuous texturing with no UV work.
//
//   macro_noise    : low-frequency FBM      -> large patches of lighter/darker dust
//   grain_noise    : high-frequency value   -> fine speckle / grain
//   rock_normal    : FBM, "As Normal Map"   -> rough rocky bump detail
//   crater_normal  : Cellular distance, "As Normal Map" -> micro-crater bowls
//   crater_mask    : Cellular distance, inverted        -> darkens crater floors
shader_type spatial;
render_mode diffuse_burley, specular_disabled;

group_uniforms Color;
uniform vec4 base_color : source_color = vec4(0.353, 0.353, 0.353, 1.0); // #5a5a5a
uniform vec4 dark_color : source_color = vec4(0.22, 0.22, 0.225, 1.0);
uniform vec4 light_color : source_color = vec4(0.5, 0.495, 0.48, 1.0);
uniform float macro_contrast : hint_range(0.0, 1.0) = 0.6;
uniform float grain_strength : hint_range(0.0, 1.0) = 0.3;
uniform float crater_darkening : hint_range(0.0, 1.0) = 0.18;

group_uniforms Surface;
uniform float roughness_value : hint_range(0.0, 1.0) = 1.0; // moon dust is not shiny
uniform float specular_value : hint_range(0.0, 1.0) = 0.0;
uniform float rock_normal_strength : hint_range(0.0, 4.0) = 0.8;
uniform float crater_normal_strength : hint_range(0.0, 4.0) = 0.8;

group_uniforms Tiling;
uniform float macro_scale = 0.012;       // texture repeats per metre
uniform float grain_scale = 0.45;
uniform float rock_normal_scale = 0.22;
uniform float crater_scale = 0.06;

group_uniforms Textures;
uniform sampler2D macro_noise : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grain_noise : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D crater_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D crater_mask : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec2 unpack_normal_xy(vec4 s) {
	return s.xy * 2.0 - 1.0;
}

void fragment() {
	vec2 p = world_pos.xz;

	// --- Albedo: dusty grays with large-scale variation, grain and darker crater floors ---
	float macro_a = texture(macro_noise, p * macro_scale).r;
	float macro_b = texture(macro_noise, p * macro_scale * 0.27 + vec2(0.37, 0.71)).r; // 2nd scale hides tiling
	float macro = mix(macro_a, macro_b, 0.5);
	float grain = texture(grain_noise, p * grain_scale).r;
	// Two crater sizes (offset + rescaled) so the cell pattern does not read as a grid.
	float crater_a = texture(crater_mask, p * crater_scale).r;
	float crater_b = texture(crater_mask, p * crater_scale * 2.7 + vec2(0.5, 0.21)).r * 0.75;
	float crater = smoothstep(0.55, 1.0, max(crater_a, crater_b)); // only the bowl floors darken

	float lo = 0.5 - macro_contrast * 0.5;
	float hi = 0.5 + macro_contrast * 0.5;
	vec3 col = mix(dark_color.rgb, light_color.rgb, smoothstep(lo, hi, macro));
	col = mix(col, base_color.rgb, 0.5);
	col *= 1.0 + (grain - 0.5) * 2.0 * grain_strength;
	col *= 1.0 - crater * crater_darkening;

	ALBEDO = col;
	ROUGHNESS = roughness_value;
	SPECULAR = specular_value;
	METALLIC = 0.0;

	// --- Normals: fractal rock bumps + cellular micro-craters ---
	// Perturb the geometric normal in world XZ. Independent of mesh tangents, so it works on
	// CSG shapes; ideal for ground-like surfaces (top faces), acceptable on rocks and rims.
	vec2 n_rock = unpack_normal_xy(texture(rock_normal, p * rock_normal_scale)) * rock_normal_strength;
	vec2 n_crater = (unpack_normal_xy(texture(crater_normal, p * crater_scale))
		+ unpack_normal_xy(texture(crater_normal, p * crater_scale * 2.7 + vec2(0.5, 0.21))) * 0.6) * crater_normal_strength;
	vec2 d = n_rock + n_crater;
	vec3 wn = normalize(normalize(world_normal) + vec3(d.x, 0.0, d.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
}
