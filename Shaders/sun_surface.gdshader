// Поверхность солнца: "кипящие" гранулы плазмы и потемнение к краю диска.
// Вешается материалом на сферу солнца. Яркость > 1 даёт свечение (Glow в WorldEnvironment).
shader_type spatial;
render_mode unshaded, shadows_disabled, fog_disabled;

#include "res://Shaders/noise.gdshaderinc"

uniform vec3 color_hot : source_color = vec3(1.0, 0.92, 0.7);   // горячие яркие гранулы
uniform vec3 color_cool : source_color = vec3(1.0, 0.42, 0.08); // остывающая плазма между ними
uniform float brightness : hint_range(0.0, 30.0) = 6.0;         // общая яркость (больше 1 = светится)
uniform float granule_scale : hint_range(0.5, 30.0) = 7.0;      // размер гранул: больше = мельче
uniform float flow_speed : hint_range(0.0, 1.0) = 0.04;         // скорость "кипения"
uniform float contrast : hint_range(0.0, 1.0) = 0.6;            // насколько заметны гранулы
uniform float limb_darkening : hint_range(0.0, 1.0) = 0.55;     // край диска темнее центра, как у настоящих звёзд

varying vec3 sphere_dir; // точка на единичной сфере: шум "приклеен" к поверхности и не зависит от размера солнца

void vertex() {
	sphere_dir = normalize(VERTEX);
}

void fragment() {
	float t = TIME * flow_speed;
	vec3 p = sphere_dir * granule_scale;

	// "Искривление" шума другим шумом: пятна текут и закручиваются, а не просто мерцают
	vec3 warp = vec3(fbm3(p + vec3(0.0, t, 0.0), 3),
	                 fbm3(p + vec3(5.2, -t, 1.3), 3),
	                 fbm3(p + vec3(2.8, 4.1, t), 3));
	float n = fbm3(p * 1.5 + warp * 2.0 + vec3(t), 5);

	float granules = smoothstep(0.5 - contrast * 0.35, 0.5 + contrast * 0.25, n);
	vec3 col = mix(color_cool, color_hot, granules);

	// Потемнение к краю: NORMAL и VIEW в пространстве камеры, их скалярное произведение = 1 в центре диска и 0 на краю
	float mu = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	col *= mix(1.0 - limb_darkening, 1.0, sqrt(mu));

	ALBEDO = col * brightness;
}
