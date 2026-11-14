// Emissive surface for the Lunar Outpost art kit: screens, indicator LEDs, grow lights, beacons.
// The colour comes from the mesh's vertex colours (authored in sRGB, converted here), so one
// material serves every glowing part. Energy is kept moderate so colours stay readable under ACES
// instead of clipping to white.
shader_type spatial;

uniform float energy : hint_range(0.0, 16.0) = 1.4;

void fragment() {
	vec3 c = pow(COLOR.rgb, vec3(2.2));
	ALBEDO = c * 0.35;
	EMISSION = c * energy;
	ROUGHNESS = 0.35;
}
