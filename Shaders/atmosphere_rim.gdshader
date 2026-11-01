// =====================================================================
//  АТМОСФЕРА: СТИЛИЗОВАННАЯ КРОМКА
//  Как на фото Земли из космоса: тонкое голубое свечение по краю планеты,
//  оранжевое у границы дня и ночи, а над центром диска атмосфера почти прозрачная.
//
//  Старый вариант (честный расчёт рассеяния света) лежит рядом: atmosphere.gdshader,
//  материал с ним - atmosphere_scattering.tres.
// =====================================================================
//
//  КОРОТКО ОБ УСТРОЙСТВЕ ШЕЙДЕРА В GODOT
//
//  Шейдер - маленькая программа, которая выполняется на видеокарте:
//    vertex()   - один раз для каждой вершины меша (можно двигать вершины);
//    fragment() - один раз для каждого пикселя, который меш закрывает на экране (здесь решаем цвет).
//  Все пиксели считаются параллельно и независимо, поэтому "соседей" пиксель не видит.
//
//  shader_type spatial - шейдер для 3D-объектов.
//  render_mode         - как рисовать: со светом или без, как смешивать с тем, что уже на экране, и т.д.
//  uniform             - параметр, который задаётся снаружи (в инспекторе материала или из скрипта).
//                        Подсказки после двоеточия: hint_range(мин, макс) - ползунок, source_color - выбор цвета.
//  varying             - значение, которое vertex() передаёт в fragment() (видеокарта плавно смешивает его между вершинами).
//  Большие буквы (VERTEX, ALBEDO, TIME, CAMERA_POSITION_WORLD...) - встроенные переменные Godot.
//
//  ИДЕЯ ЭТОГО ШЕЙДЕРА
//
//  Вокруг планеты - сфера чуть больше неё (её создаёт скрипт PlanetAtmosphere.cs).
//  Для каждого пикселя этой сферы пускаем луч из камеры и смотрим, как он проходит мимо планеты:
//    - луч проходит МИМО планеты: чем ближе он к поверхности, тем ярче свечение -> ореол по краю;
//    - луч ПОПАДАЕТ в планету: светимся только если смотрим на поверхность "по касательной"
//      (у края диска), а в центре диска свечения нет -> никакого тумана на планете.
//  Потом красим: на дневной стороне голубым, у границы дня и ночи оранжевым, на ночной - ничего.
// =====================================================================

shader_type spatial;

// unshaded            - освещение Godot не нужно, цвет считаем сами.
// blend_add           - цвет ПРИБАВЛЯЕТСЯ к тому, что уже нарисовано: атмосфера только светится
//                       и ничего не закрывает (поверхность планеты и звёзды остаются видны).
// cull_front          - рисуем внутреннюю сторону сферы: тогда она видна и снаружи, и изнутри.
// depth_test_disabled - не прячемся за планетой (иначе задняя половина сферы была бы закрыта ею),
//                       а что перед нами стоит, проверяем сами по буферу глубины (см. ниже).
render_mode unshaded, blend_add, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// ---- задаёт скрипт PlanetAtmosphere.cs, руками не трогать ----
uniform float planet_radius = 250.0;              // радиус планеты в единицах меша
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0); // направление от планеты на солнце

// ---- форма (group_uniforms группирует параметры в инспекторе) ----
group_uniforms Shape;
uniform float thickness : hint_range(0.005, 0.2) = 0.035;     // толщина ореола в долях радиуса планеты
uniform float disk_haze : hint_range(0.0, 1.0) = 0.35;        // свечение на самом диске у его края
uniform float disk_haze_power : hint_range(1.0, 16.0) = 5.0;  // больше = свечение на диске уже, ближе к краю

// ---- цвет ----
group_uniforms Color;
uniform vec3 day_color : source_color = vec3(0.35, 0.62, 1.0);    // цвет на дневной стороне
uniform vec3 sunset_color : source_color = vec3(1.0, 0.45, 0.15); // цвет у границы дня и ночи
uniform float intensity : hint_range(0.0, 10.0) = 1.5;            // общая яркость
uniform float terminator_width : hint_range(0.05, 1.0) = 0.35;    // ширина полосы заката
uniform float backlight : hint_range(0.0, 5.0) = 1.5;             // кольцо вокруг планеты, когда она закрывает солнце

// ---- небо, если камера внутри атмосферы (на поверхности) ----
group_uniforms Sky;
uniform float sky_brightness : hint_range(0.0, 3.0) = 0.5;  // яркость неба над головой
uniform float horizon_boost : hint_range(1.0, 5.0) = 2.5;   // во сколько раз ярче у горизонта
group_uniforms;

// Буфер глубины: насколько далеко от камеры уже нарисованные объекты (чтобы луна перед атмосферой её закрывала)
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

// Позиция пикселя сферы в мире: считаем в vertex(), используем в fragment()
varying vec3 world_pos;

void vertex() {
	// MODEL_MATRIX переводит вершину из координат меша в мировые
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// ---------- 1. Исходные данные ----------

	// Масштаб узла: далёкие планеты рисуются уменьшенными, атмосфера уменьшается вместе с ними
	float scale = length(MODEL_MATRIX[0].xyz);
	vec3 center = NODE_POSITION_WORLD;        // центр планеты в мире
	float rp = planet_radius * scale;         // радиус планеты в мире
	float h_scale = thickness * rp;           // "толщина" ореола в метрах

	vec3 ro = CAMERA_POSITION_WORLD;          // начало луча - камера
	vec3 rd = normalize(world_pos - ro);      // направление луча - через этот пиксель
	vec3 sun = normalize(sun_direction);

	// Камера в космосе или у поверхности? 0 = у земли, 1 = высоко над атмосферой.
	// smoothstep(a, b, x) плавно идёт от 0 (x <= a) до 1 (x >= b) - самая полезная функция в шейдерах.
	float cam_altitude = length(ro - center) - rp;
	float in_space = smoothstep(h_scale * 0.5, h_scale * 4.0, cam_altitude);

	// ---------- 2. Как луч проходит мимо планеты ----------

	// Ближайшая к центру планеты точка луча. dot(вектор, направление) = длина проекции на направление.
	// max(..., 0) - если центр позади камеры, ближайшая точка - сама камера.
	float t_closest = max(dot(center - ro, rd), 0.0);
	vec3 closest_point = ro + rd * t_closest;
	float closest = length(closest_point - center);

	vec3 point;    // точка, в которой считаем освещение (на неё "смотрит" этот пиксель)
	float glow;    // сила свечения 0..1

	if (closest < rp) {
		// Луч попадает в планету. Точка входа: из ближайшей точки отступаем назад по теореме Пифагора.
		float t_hit = t_closest - sqrt(rp * rp - closest * closest);
		point = ro + rd * t_hit;

		// facing: 1 = смотрим на поверхность прямо сверху, 0 = по касательной (у края диска)
		vec3 normal = normalize(point - center);
		float facing = clamp(dot(normal, -rd), 0.0, 1.0);

		// Свечение только у края диска (эффект Френеля). У земли его нет вовсе (in_space = 0).
		glow = disk_haze * pow(1.0 - facing, disk_haze_power) * in_space;
	} else {
		// Луч проходит мимо. Чем выше над поверхностью он проходит, тем слабее свечение (экспонента).
		point = closest_point;
		float height = closest - rp;
		float halo = exp(-height / h_scale);

		// Изнутри атмосферы (стоим на земле) - простое небо: ярче у горизонта, темнее в зените.
		vec3 up = normalize(ro - center);
		float elevation = dot(rd, up);                       // 1 = смотрим в зенит, 0 = на горизонт
		float sky = sky_brightness * mix(horizon_boost, 1.0, smoothstep(0.0, 0.5, elevation));

		glow = mix(sky, halo, in_space);
	}

	// ---------- 3. Что-то стоит перед атмосферой? ----------

	// Восстанавливаем расстояние до уже нарисованного объекта из буфера глубины
	float depth = texture(depth_texture, SCREEN_UV).x;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_distance = length(view.xyz / view.w);
	float point_distance = dot(point - ro, rd);
	if (scene_distance < point_distance - h_scale) discard; // discard = не рисовать этот пиксель

	// ---------- 4. Освещение и цвет ----------

	// ndl: 1 = солнце в зените над этой точкой, 0 = на горизонте (закат), < 0 = ночь
	vec3 normal = normalize(point - center);
	float ndl = dot(normal, sun);

	// День: свет немного заходит за границу на ночную сторону (атмосфера рассеивает его)
	float day = smoothstep(-terminator_width * 0.5, terminator_width, ndl);

	// Цвет: у границы дня и ночи - закатный, дальше в день - голубой
	vec3 color = mix(sunset_color, day_color, smoothstep(0.0, terminator_width, ndl));

	// Контровой свет: смотрим на планету против солнца -> кромка светится даже на ночной стороне
	float toward_sun = pow(max(dot(rd, sun), 0.0), 8.0);
	float light = day + backlight * toward_sun;

	ALBEDO = color * glow * light * intensity;
}
