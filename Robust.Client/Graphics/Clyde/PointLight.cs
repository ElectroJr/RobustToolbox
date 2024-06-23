using System.Numerics;
using System.Runtime.InteropServices;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde;

struct PointLight(LightProperties properties, float distance, bool cast, Box2 mask)
{
    public LightProperties Properties = properties;
    public readonly Box2 Mask = mask;
    public readonly float DistFromCentreSq = distance;
    public readonly bool CastShadows = cast;
}

/// <summary>
/// Struct containing light rendering vertex data
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct LightVertex(Vector2 tex, Vector2 tex2, LightProperties properties)
{
    public readonly Vector2 Tex = tex; // UV Coordinates in the texture atlas
    public readonly Vector2 Tex2 = tex2; // UV for the sub-texture
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
    float index,
    float angle)
{
    public readonly Color Color = color;
    public readonly Vector2 LightPos = lightPos;
    public readonly float Range = range;
    public readonly float Power = power;
    public readonly float Softness = softness;

    /// <summary>
    /// V coordinates of the light in the shadow/depth texture.
    /// </summary>
    public float Index = index;
    public readonly float Angle = angle;
}
