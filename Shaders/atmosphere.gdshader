// Атмосфера планеты: рассеяние солнечного света в воздухе.
//
// Как это работает. Вокруг планеты - сфера чуть больше неё (оболочка атмосферы).
// Для каждого пикселя пускаем луч из камеры и смотрим, какой отрезок луча проходит через воздух.
// Идём по этому отрезку шагами (ray marching) и в каждой точке считаем:
//   - насколько там плотный воздух (у земли плотнее, к верху экспоненциально реже);
//   - сколько солнечного света дошло до этой точки (свет тоже теряется по пути через воздух);
//   - сколько из него рассеялось в сторону камеры.
// Рассеяние Рэлея (молекулы воздуха) сильнее для синего цвета -> днём небо голубое,
// а на закате свет проходит через толщу воздуха, синий рассеивается по пути -> небо красное.
// Рассеяние Ми (пыль, дымка) почти не зависит от цвета и светит вперёд -> светлый ореол вокруг солнца.
//
// Радиусы и направление на солнце ставит скрипт PlanetAtmosphere.cs, остальное можно крутить в материале.
shader_type spatial;
// unshaded - свет считаем сами; cull_front - рисуем внутреннюю сторону сферы, чтобы атмосфера была видна
// и из космоса, и с поверхности; depth_test_disabled - не прячемся за планетой, а сами обрезаем луч по глубине сцены.
render_mode unshaded, blend_premul_alpha, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform float planet_radius = 250.0;      // радиус планеты в единицах меша (без масштаба узла)
uniform float atmosphere_radius = 300.0;  // радиус верхней границы атмосферы
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0); // направление от планеты на солнце (мировое)

uniform vec3 rayleigh_color = vec3(0.16, 0.40, 1.0);       // рассеяние по каналам R,G,B: синий сильнее всех
uniform float rayleigh_strength : hint_range(0.0, 10.0) = 1.0;
uniform float mie_strength : hint_range(0.0, 5.0) = 0.25;     // дымка
uniform float mie_direction : hint_range(0.0, 0.99) = 0.8;    // насколько дымка светит "вперёд" (ореол вокруг солнца)
uniform float density : hint_range(0.0, 20.0) = 1.6;          // общая плотность воздуха
uniform float scale_height : hint_range(0.02, 1.0) = 0.25;    // на какой доле толщины атмосферы плотность падает в e раз
uniform float sun_intensity : hint_range(0.0, 100.0) = 22.0;  // яркость солнечного света в атмосфере
// Дымка поверх земли и предметов (0 = нет, 1 = физически честно).
// Атмосфера у маленькой планеты тонкая и плотная, поэтому честный расчёт даёт густой туман у земли
// и "светящийся" диск из космоса. Небо (лучи, уходящие в космос) эта настройка не трогает.
uniform float surface_haze : hint_range(0.0, 1.0) = 0.12;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

const int VIEW_STEPS = 16;  // шагов вдоль луча камеры
const int LIGHT_STEPS = 6;  // шагов вдоль луча к солнцу из каждой точки

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Пересечение луча (начало ro, направление rd) со сферой: расстояния до входа и выхода. Нет пересечения -> x > y.
vec2 ray_sphere(vec3 ro, vec3 rd, vec3 center, float radius) {
	vec3 oc = ro - center;
	float b = dot(oc, rd);
	float c = dot(oc, oc) - radius * radius;
	float h = b * b - c;
	if (h < 0.0) return vec2(1e20, -1e20);
	h = sqrt(h);
	return vec2(-b - h, -b + h);
}

void fragment() {
	// Масштаб узла (далёкие планеты рисуются уменьшенными - атмосфера уменьшается вместе с ними)
	float scale = length(MODEL_MATRIX[0].xyz);
	vec3 center = NODE_POSITION_WORLD;
	float rp = planet_radius * scale;
	float ra = atmosphere_radius * scale;
	float thickness = ra - rp;

	vec3 ro = CAMERA_POSITION_WORLD;
	vec3 rd = normalize(world_pos - ro);

	// Отрезок луча внутри атмосферы
	vec2 ta = ray_sphere(ro, rd, center, ra);
	if (ta.x > ta.y || ta.y < 0.0) discard;
	float t0 = max(ta.x, 0.0); // камера может быть внутри атмосферы
	float t1 = ta.y;

	// Обрезаем луч там, где он упирается в планету...
	vec2 tp = ray_sphere(ro, rd, center, rp);
	if (tp.x > 0.0) t1 = min(t1, tp.x);

	// ...или в любой другой объект сцены (луна перед атмосферой, горы, корабль)
	float depth = texture(depth_texture, SCREEN_UV).x;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	t1 = min(t1, length(view.xyz / view.w));

	if (t1 <= t0) discard;

	// Луч упёрся в землю или предмет (а не ушёл в космос) -> дымку ослабляем до surface_haze.
	float haze = 1.0;
	if (t1 < ta.y - 0.001) {
		haze = surface_haze;
		// Из космоса к краю диска планеты ослабление плавно снимаем: остаётся тонкая яркая кромка атмосферы.
		// Внутри атмосферы не нужно - там граница "земля / небо" и есть горизонт.
		float b = dot(center - ro, rd);
		float closest = length(center - ro - rd * b);              // как близко луч проходит к центру планеты
		float outside = smoothstep(ra, ra + thickness, length(ro - center)); // 0 в атмосфере, 1 в космосе
		haze = mix(haze, mix(surface_haze, 1.0, smoothstep(rp * 0.9, rp, closest)), outside);
	}

	vec3 beta_r = rayleigh_color * rayleigh_strength;
	float beta_m = mie_strength;
	vec3 sun = normalize(sun_direction);

	float ds = (t1 - t0) / float(VIEW_STEPS);
	float view_depth = 0.0;      // сколько воздуха между камерой и текущей точкой
	vec3 sum_r = vec3(0.0);      // накопленный рассеянный свет (Рэлей)
	vec3 sum_m = vec3(0.0);      // накопленный рассеянный свет (Ми)

	for (int i = 0; i < VIEW_STEPS; i++) {
		vec3 p = ro + rd * (t0 + (float(i) + 0.5) * ds);
		vec3 up = p - center;
		float h = (length(up) - rp) / thickness;                 // высота: 0 у земли, 1 на границе атмосферы
		float rho = exp(-max(h, 0.0) / scale_height) * density;  // плотность воздуха здесь
		float step_depth = rho * ds / thickness;

		view_depth += step_depth * 0.5;

		// Тень планеты: если луч к солнцу проходит сквозь планету, сюда свет не попадает.
		// Смягчаем край, чтобы граница дня и ночи была плавной.
		float lit = 1.0;
		if (dot(up, sun) < 0.0) {
			float miss = length(cross(up, sun)); // как близко к центру проходит луч к солнцу
			lit = smoothstep(rp * 0.97, rp * 1.03, miss);
		}

		if (lit > 0.0) {
			// Сколько воздуха между этой точкой и солнцем
			float light_len = ray_sphere(p, sun, center, ra).y;
			float lds = light_len / float(LIGHT_STEPS);
			float light_depth = 0.0;
			for (int j = 0; j < LIGHT_STEPS; j++) {
				vec3 q = p + sun * ((float(j) + 0.5) * lds);
				float hq = (length(q - center) - rp) / thickness;
				light_depth += exp(-max(hq, 0.0) / scale_height) * density * lds / thickness;
			}

			// Ослабление света на пути солнце -> точка -> камера
			vec3 attenuation = exp(-(beta_r + beta_m * 1.1) * (view_depth + light_depth));
			sum_r += attenuation * step_depth * lit;
			sum_m += attenuation * step_depth * lit;
		}

		view_depth += step_depth * 0.5;
	}

	// Фазовые функции: в какую сторону рассеивается свет относительно направления на солнце
	float mu = dot(rd, sun);
	float phase_r = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
	float g = mie_direction;
	float phase_m = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + mu * mu))
	              / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * mu, 1.5));

	vec3 color = sun_intensity * (sum_r * beta_r * phase_r + sum_m * beta_m * phase_m) * haze;

	// Насколько воздух заслоняет то, что за ним (звёзды, поверхность)
	vec3 transmittance = exp(-(beta_r + beta_m * 1.1) * view_depth * haze);

	ALBEDO = color;
	ALPHA = 1.0 - dot(transmittance, vec3(1.0 / 3.0));
}
