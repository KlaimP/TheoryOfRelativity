using Godot;
using System;

/// <summary>
/// Вектор двойной точности для мировых координат.
/// Godot хранит позиции во float: точности хватает только на ~10 км от начала координат.
/// Поэтому настоящие позиции тел живут в Vector3D, а на сцену попадают уже относительно
/// плавающего начала координат (GravitySystem.Origin), рядом с игроком.
/// </summary>
public readonly struct Vector3D
{
    public readonly double X, Y, Z;

    public static readonly Vector3D Zero = new(0, 0, 0);

    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }

    public double LengthSquared() => X * X + Y * Y + Z * Z;
    public double Length() => Math.Sqrt(LengthSquared());
    public double Dot(Vector3D o) => X * o.X + Y * o.Y + Z * o.Z;

    public Vector3D Normalized()
    {
        double l = Length();
        return l > 0 ? this / l : Zero;
    }

    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3D operator -(Vector3D a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3D operator *(Vector3D a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vector3D operator *(double k, Vector3D a) => a * k;
    public static Vector3D operator /(Vector3D a, double k) => new(a.X / k, a.Y / k, a.Z / k);

    public static explicit operator Vector3(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static implicit operator Vector3D(Vector3 v) => new(v.X, v.Y, v.Z);

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
