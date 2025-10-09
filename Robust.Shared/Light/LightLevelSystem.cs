using System;
using System.Numerics;
using Robust.Shared.Collections;
using Robust.Shared.ComponentTrees;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Robust.Shared.Light;
public sealed class LightLevelSystem : EntitySystem
{
    // TODO LIGHT LEVEL make into a CVAR
    /// <summary>
    /// This is the range that is used to look for any nearby light trees when computing the light level at a point.
    /// </summary>
    public const float TreeSearchRange = 15f;

    private const float LightHeight = 1.0f;

    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly OccluderSystem _occluder = default!;
    [Dependency] private readonly SharedLightTreeSystem _tree = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public float CalculateLightLevel(EntityUid uid)
        => CalculateLightLevel(_transform.GetMapCoordinates(uid));

    public float CalculateLightLevel(EntityCoordinates point)
        => CalculateLightLevel(_transform.ToMapCoordinates(point));

    public float CalculateLightLevel(MapCoordinates point)
    {
        var pos = point.Position;
        var treeSearchAabb = new Box2(pos, pos).Enlarged(TreeSearchRange);
        var lights = new ValueList<Entity<SharedPointLightComponent, TransformComponent>>();

        // We manually query individual trees instead of using LightTreeSystem.QueryAabb
        // This is because the actual area we want to query is a point, but we want to include trees from further away.

        foreach (var (tree, treeComp) in _tree.GetIntersectingTrees(point.MapId, treeSearchAabb))
        {
            var localPos = Vector2.Transform(pos, _transform.GetInvWorldMatrix(tree));
            var localAabb = new Box2(localPos, localPos);

            treeComp.Tree.QueryAabb(ref lights,
                static (ref ValueList<Entity<SharedPointLightComponent, TransformComponent>> lights, in ComponentTreeEntry<SharedPointLightComponent> value) =>
                {
                    if (value.Component.CastShadows)
                        lights.Add(value);
                    return true;
                },
                localAabb,
                true);
        }

        var illumination = 0f;
        foreach (var entry in lights)
        {
            illumination += CalculateLightLevel(entry, point);
        }

        return illumination;
    }

    private float CalculateLightLevel(Entity<SharedPointLightComponent, TransformComponent> ent, MapCoordinates point)
    {
        var (_, light, xform) = ent;

        var (lightPos, lightRot) = _transform.GetWorldPositionRotation(xform);
        lightPos += lightRot.RotateVec(light.Offset);

        var lightPosition = new MapCoordinates(lightPos, xform.MapID);

        if (!_occluder.InRangeUnoccluded(lightPosition, point, light.Radius, ignoreTouching: false))
            return 0;

        var dist = point.Position - lightPosition.Position;

        // Calculate the light level the same way as in light_shared.swsl. The problem with this implementation is that
        // values used for rendering are very different from the sort of percentage based values we aim to use in game.
        // // this implementation of light attenuation primarily adapted from
        // // https://lisyarus.github.io/blog/posts/point-light-attenuation.html
        var sqr_dist = Vector2.Dot(dist, dist) + LightHeight;
        var s = Math.Clamp(MathF.Sqrt(sqr_dist) / light.Radius, 0.0f, 1.0f);
        var s2 = s * s;
        var curveFactor = MathHelper.Lerp(s, s2, Math.Clamp(light.CurveFactor, 0.0f, 1.0f));
        var lightVal = Math.Clamp(((1.0f - s2) * (1.0f - s2)) / (1.0f + light.Falloff * curveFactor), 0.0f, 1.0f);
        var colorBrightness = MathF.Max(light.Color.R, MathF.Max(light.Color.G, light.Color.B));
        var energyLightVal = light.Energy * lightVal;
        var finalLightVal = Math.Clamp(energyLightVal * colorBrightness, 0.0f, 1.0f);

        if (!_proto.TryIndex(light.LightMask, out var mask))
            return finalLightVal;

        // TODO LIGHTLEVEL re add GetAngle
        var angleToTarget = Angle.Zero;

        // TODO: read the mask image into a buffer of pixels and sample the returned color to multiply against the light level before final calculation
        // var stream = _resource.ContentFileRead(mask.MaskPath);
        // var image = Image.Load<Rgba32>(stream);
        // Rgba32[] pixelArray = new Rgba32[image.Width * image.Height];
        // image.CopyPixelDataTo(pixelArray);

        var calculatedLight = 0f;
        foreach (var cone in mask.LightCones)
        {
            var angleAttenuation = Math.Min((float) Math.Max(cone.OuterWidth - angleToTarget, 0f), cone.InnerWidth) / cone.OuterWidth;
            var absAngle = Math.Abs(angleToTarget.Degrees);

            // Target is outside the cone's outer width angle, so ignore
            if (absAngle - Math.Abs(cone.Direction) > cone.OuterWidth)
                continue;

            if (absAngle - cone.Direction > cone.InnerWidth && absAngle - cone.Direction < cone.OuterWidth)
                calculatedLight += finalLightVal * angleAttenuation;
            else
                calculatedLight += finalLightVal;
        }

        return calculatedLight;
    }
}
