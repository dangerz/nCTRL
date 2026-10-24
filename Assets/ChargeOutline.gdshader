shader_type canvas_item;
// Charging: a yellow line just outside the car's shape (the mask's red channel is 1 inside the car),
// with a bright light sweeping clockwise around it.
uniform vec4 glow : source_color = vec4(1.0, 0.82, 0.16, 1.0);
uniform vec2 center = vec2(0.5, 0.55);
uniform float speed = 0.3;
// Line width in screen pixels.
uniform float width = 2.0;
void fragment() {
	float inside = texture(TEXTURE, UV).r;
	vec2 step_uv = fwidth(UV) * width;
	float near = 0.0;
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.7853982;
		near = max(near, texture(TEXTURE, UV + vec2(cos(a), sin(a)) * step_uv).r);
	}
	float edge = smoothstep(0.3, 0.7, near) * (1.0 - smoothstep(0.3, 0.7, inside));
	vec2 d = (UV - center) * vec2(textureSize(TEXTURE, 0));
	// Screen y points down, so a growing angle runs clockwise; the head leads and a tail fades behind it.
	float angle = atan(d.y, d.x) / 6.2831853;
	float behind = fract(TIME * speed - angle);
	float light = 0.35 + 0.65 * pow(1.0 - behind, 6.0);
	COLOR = vec4(glow.rgb, glow.a * edge * light);
}
