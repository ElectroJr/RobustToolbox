using System.Numerics;
using System.Runtime.InteropServices;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde;

struct PointLight(LightProperties properties, Box2 mask)
{
    public LightProperties Properties = properties;
    public readonly Box2 Mask = mask;
}

/// <summary>
/// Struct containing light rendering vertex data
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct LightVertex(Vector2 maskUV, LightProperties properties)
{
    public readonly Vector2 MaskUV = maskUV;
    public readonly LightProperties Properties = properties;
}

/// <summary>
/// Struct containing light properties. This makes up part of the light rendering vertex data
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightProperties(
    Color color,
    Vector2 lightPos,
    float range,
    float power,
    float softness,
    float angle)
{
    public readonly Color Color = color;
    public readonly Vector2 LightPos = lightPos;
    public readonly float Range = range;
    public readonly float Power = power;
    public readonly float Softness = softness;
    public readonly float Angle = angle;
}
