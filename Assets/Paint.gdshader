shader_type canvas_item;
uniform vec4 paint : source_color = vec4(0.52,0.73,0.95,1.0);
// The source art is pale blue. Only bright blue body pixels are selected;
// neutral lamps, red trim, dark glass and wheels retain their source color.
void fragment() {
    vec4 src = texture(TEXTURE, UV);
    float brightness = max(src.r, max(src.g, src.b));
    float blue = src.b - src.r;
    float select_body = smoothstep(0.065, 0.13, blue)
        * smoothstep(0.28, 0.48, brightness)
        * (1.0 - smoothstep(0.05, 0.13, src.g - src.b));
    float shade = clamp(brightness / 0.94, 0.0, 1.15);
    vec3 tinted = paint.rgb * shade;
    // Specular highlights remain readable, especially on dark paint.
    tinted = mix(tinted, vec3(0.92), smoothstep(0.91, 1.0, brightness) * 0.27);
    COLOR = vec4(mix(src.rgb, tinted, select_body), src.a);
}
