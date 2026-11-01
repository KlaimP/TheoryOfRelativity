// Общие функции шума для шейдеров. Подключается так: #include "res://Shaders/noise.gdshaderinc"

// Псевдослучайное число 0..1 для точки в пространстве
float hash3(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Плавный 3D-шум 0..1 (value noise): случайные значения в узлах сетки, плавно смешанные между собой
float noise3(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f); // сглаживание, чтобы не было видно сетки

	return mix(mix(mix(hash3(i + vec3(0.0, 0.0, 0.0)), hash3(i + vec3(1.0, 0.0, 0.0)), f.x),
	               mix(hash3(i + vec3(0.0, 1.0, 0.0)), hash3(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
	           mix(mix(hash3(i + vec3(0.0, 0.0, 1.0)), hash3(i + vec3(1.0, 0.0, 1.0)), f.x),
	               mix(hash3(i + vec3(0.0, 1.0, 1.0)), hash3(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}

// Фрактальный шум: несколько слоёв шума, каждый мельче и слабее предыдущего (крупные пятна + мелкие детали)
float fbm3(vec3 p, int octaves) {
	float value = 0.0;
	float amplitude = 0.5;
	for (int i = 0; i < octaves; i++) {
		value += amplitude * noise3(p);
		p = p * 2.03 + vec3(1.7, 9.2, 3.1); // сдвиг, чтобы слои не совпадали
		amplitude *= 0.5;
	}
	return value;
}
