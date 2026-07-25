using System;
using System.Runtime.InteropServices;

namespace Etch.Scene;

public readonly struct ColorFilter
{
    public const int FloatCount = 20;

    public readonly float M11, M12, M13, M14, M15;
    public readonly float M21, M22, M23, M24, M25;
    public readonly float M31, M32, M33, M34, M35;
    public readonly float M41, M42, M43, M44, M45;

    public ColorFilter(
        float m11, float m12, float m13, float m14, float m15,
        float m21, float m22, float m23, float m24, float m25,
        float m31, float m32, float m33, float m34, float m35,
        float m41, float m42, float m43, float m44, float m45)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14; M15 = m15;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24; M25 = m25;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34; M35 = m35;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44; M45 = m45;
    }

    public static ColorFilter Identity => new(
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0);

    public static ColorFilter Grayscale => new(
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0);

    public static ColorFilter Sepia => new(
        0.393f, 0.769f, 0.189f, 0, 0,
        0.349f, 0.686f, 0.168f, 0, 0,
        0.272f, 0.534f, 0.131f, 0, 0,
        0, 0, 0, 1, 0);

    public static ColorFilter Invert => new(
        -1, 0, 0, 0, 1,
        0, -1, 0, 0, 1,
        0, 0, -1, 0, 1,
        0, 0, 0, 1, 0);

    public static ColorFilter Brightness(float amount) => new(
        amount, 0, 0, 0, 0,
        0, amount, 0, 0, 0,
        0, 0, amount, 0, 0,
        0, 0, 0, 1, 0);

    public static ColorFilter Contrast(float amount) => new(
        amount, 0, 0, 0, (1f - amount) * 0.5f,
        0, amount, 0, 0, (1f - amount) * 0.5f,
        0, 0, amount, 0, (1f - amount) * 0.5f,
        0, 0, 0, 1, 0);

    public bool IsIdentity =>
        M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0 && M15 == 0 &&
        M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 && M25 == 0 &&
        M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0 && M35 == 0 &&
        M41 == 0 && M42 == 0 && M43 == 0 && M44 == 1 && M45 == 0;
}
