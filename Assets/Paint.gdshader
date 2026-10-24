shader_type canvas_item;
uniform vec4 paint : source_color = vec4(0.52,0.73,0.95,1.0);
// App color: the backdrop around the car (where car_mask is black) is turned to the theme's hue, the
// same way as the app's own colors. No mask, or no shift, leaves the picture as it is.
uniform float hue_shift = 0.0; // in turns
uniform float vivid = 1.0;
uniform sampler2D car_mask : hint_default_white, filter_linear;
vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1.0e-10)), d / (q.x + 1.0e-10), q.x);
}
vec3 hsv2rgb(vec3 c) {
    vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}
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
    vec3 color = mix(src.rgb, tinted, select_body);
    float backdrop = 1.0 - texture(car_mask, UV).r;
    if (backdrop > 0.0 && (hue_shift != 0.0 || vivid != 1.0)) {
        vec3 hsv = rgb2hsv(color);
        color = mix(color, hsv2rgb(vec3(fract(hsv.x + hue_shift), clamp(hsv.y * vivid, 0.0, 1.0), hsv.z)), backdrop);
    }
    COLOR = vec4(color, src.a);
}
