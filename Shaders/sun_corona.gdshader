// Корона солнца: светящийся ореол с медленно шевелящимися лучами.
// Вешается на QuadMesh - дочерний узел солнца. Квадрат всегда повёрнут к камере (billboard).
// Размер квадрата = 2 * extent * радиус солнца (например, радиус 2000 и extent 4 -> QuadMesh 16000 x 16000).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/noise.gdshaderinc"

uniform vec3 corona_color : source_color = vec3(1.0, 0.62, 0.28);
uniform float intensity : hint_range(0.0, 20.0) = 2.5;
uniform float extent = 4.0;                                   // половина стороны квадрата в радиусах солнца
uniform float falloff : hint_range(0.5, 8.0) = 2.2;          // как быстро гаснет свечение: больше = плотнее к диску
uniform float ray_strength : hint_range(0.0, 1.0) = 0.45;    // насколько заметны лучи
uniform float ray_count : hint_range(2.0, 60.0) = 18.0;      // сколько лучей по кругу
uniform float ray_speed : hint_range(0.0, 1.0) = 0.05;

void vertex() {
	// Billboard: берём оси камеры вместо своих, но сохраняем масштаб узла (важно для уменьшения далёких тел)
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(
		vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
		vec4(0.0, 0.0, 0.0, 1.0));
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;         // -1..1 от центра квадрата
	float d = length(p) * extent;     // расстояние от центра в радиусах солнца (1 = край диска)

	// Основное свечение: ярко у края диска, дальше быстро гаснет
	float glow = pow(1.0 / max(d, 1.0), falloff);

	// Лучи: шум по углу. Берём cos/sin угла, чтобы не было шва там, где угол перескакивает с -180 на 180
	float a = atan(p.y, p.x);
	vec3 q = vec3(cos(a), sin(a), 0.0) * ray_count * 0.3 + vec3(0.0, 0.0, TIME * ray_speed);
	float rays = fbm3(q, 3) * 2.0 - 1.0;
	glow *= 1.0 + ray_strength * rays * smoothstep(1.0, 1.6, d); // у самого диска лучей нет

	// Плавно в ноль к краю квадрата, чтобы не было видно его границ
	glow *= 1.0 - smoothstep(extent * 0.55, extent, d);

	ALBEDO = corona_color * glow * intensity;
}
