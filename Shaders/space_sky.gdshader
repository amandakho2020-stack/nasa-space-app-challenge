// Airless lunar sky: pitch black, procedural non-twinkling stars, a faint noise-based
// galactic band, and a hard-edged sun disk that follows the first DirectionalLight3D.
// The radiance cubemap pass is forced black so the sky never adds light to the scene
// (ambient comes only from the Environment's Ambient Light color).
shader_type sky;

group_uniforms Stars;
uniform float star_density : hint_range(0.0, 1.0) = 0.12;  // fraction of grid cells holding a star
uniform float star_grid = 160.0;                           // higher = smaller, more numerous stars
uniform float star_size : hint_range(0.01, 0.5) = 0.12;
uniform float star_brightness = 2.5;

group_uniforms Galaxy;
uniform sampler2D band_noise : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float band_strength : hint_range(0.0, 1.0) = 0.07;
uniform vec3 band_axis = vec3(0.3, 0.8, 0.52);

group_uniforms Sun;
uniform float sun_disk_degrees = 0.6;   // the real sun is ~0.53 degrees across
uniform float sun_disk_energy = 16.0;
uniform float sun_glare = 0.8;

vec3 hash33(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.xxy + p.yxx) * p.zyx);
}

vec3 star_layer(vec3 dir, float grid, float seed, float intensity) {
	vec3 p = dir * grid;
	vec3 cell = floor(p);
	vec3 rnd = hash33(cell + seed);
	if (rnd.x > star_density) {
		return vec3(0.0);
	}
	vec3 center = cell + 0.25 + hash33(cell + seed + 11.0) * 0.5;
	float s = smoothstep(star_size, 0.0, length(p - center));
	vec3 tint = mix(vec3(0.75, 0.85, 1.0), vec3(1.0, 0.88, 0.72), rnd.y);
	return tint * s * pow(rnd.z, 3.0) * intensity;
}

void sky() {
	if (AT_CUBEMAP_PASS) {
		COLOR = vec3(0.0);
	} else {
		vec3 dir = normalize(EYEDIR);
		vec3 col = vec3(0.0);

		// No atmosphere -> no twinkle: stars are static points.
		col += star_layer(dir, star_grid, 0.0, star_brightness);
		col += star_layer(dir, star_grid * 2.3, 17.0, star_brightness * 0.5);

		// Faint galactic band modulated by noise.
		float band = pow(1.0 - abs(dot(dir, normalize(band_axis))), 8.0);
		vec2 uv = vec2(atan(dir.x, dir.z) / 6.28318 + 0.5, dir.y * 0.5 + 0.5);
		float neb = texture(band_noise, uv * vec2(2.0, 1.0)).r;
		col += vec3(0.55, 0.6, 0.8) * band * neb * band_strength;

		// Hard sun disk + tight glare.
		if (LIGHT0_ENABLED) {
			float sd = dot(dir, LIGHT0_DIRECTION);
			float edge = cos(radians(sun_disk_degrees * 0.5));
			col += LIGHT0_COLOR * sun_disk_energy * smoothstep(edge - 0.00004, edge, sd);
			col += LIGHT0_COLOR * pow(max(sd, 0.0), 900.0) * sun_glare;
		}

		COLOR = col;
	}
}
