using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Graphics.Clyde;
using Robust.Client.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Client.Sprite.Layers;

/// <summary>
/// Base class for all layer types
/// </summary>
[Access(typeof(SpriteComponent), typeof(SpriteSystem), Other = AccessPermissions.ReadExecute)]
public abstract class BaseLayer : IComparable<BaseLayer>
{
    protected SpriteSystem System;
    protected SpriteTreeSystem Tree;
    protected IEntityManager EntMan;
    protected ISawmill Log;

    /// <summary>
    /// The primary collection that this layer belongs to.
    /// </summary>
    /// <remarks>
    /// This determines some inherited properties, like the layer's default RSI.
    /// </remarks>
    public LayerCollection? Owner;

    /// <summary>
    /// An list of layer collections that this layer is a part of, in addition to the layer's current <see cref="Owner"/>.
    /// </summary>
    /// <remarks>
    /// This list does not include <see cref="Owner"/>.
    /// </remarks>
    public List<LayerCollection>? Collections;

    public Vector2 Scale = Vector2.One;
    public Angle Rotation;
    public Vector2 Offset;
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public Color Color = Color.White;
    public bool Visible = true;

    /// <inheritdoc cref="BaseLayerData.DirOverride"/>
    public RsiDirection? DirOverride;

    /// <inheritdoc cref="BaseLayerData.DirOffset"/>
    public DirectionOffset DirOffset;

    /// <inheritdoc cref="BaseLayerData.DirBehaviour"/>
    public DirectionBehaviour DirBehaviour;

    /// <inheritdoc cref="BaseLayerData.UnShaded"/>
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public bool NoLighting;

    /// <inheritdoc cref="BaseLayerData.RenderingStrategy"/>
    public LayerRenderingStrategy? Strategy;

    /// <inheritdoc cref="BaseLayerData.DrawDepth"/>
    public int? DrawDepth;

    /// <inheritdoc cref="BaseLayerData.DirectionalDrawDepths"/>
    public int[]? DirectionalDrawDepths;

    /// <summary>
    /// The entity that is associated with this layer's owner.
    /// </summary>
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public virtual Entity<SpriteComponent>? GetEntity() => Owner?.GetEntity();

    // This should really be in the RsiLayer class. But that causes a lot of breaking changes, so fuck it.
    /// <inheritdoc cref="RsiLayerData.AutoAnimated"/>
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public bool AutoAnimated = true;

    /// <summary>
    /// The RSI that this layer will use to retrieve RSI states.
    /// </summary>
    public RSI? GetRsi() => RsiOverride ?? Owner?.GetRsi();
    internal RSI? RsiOverride;

    /// <summary>
    /// The post-processing shaders that will be used to render this layer. Each of these shaders will require drawing
    /// to a separate render target before drawing back to the previous target using the that shader. Hence these
    /// shaders should be used sparingly and, if possible, combined into a single shader.
    /// </summary>
    public ShaderInstance[]? PostShaders;

    /// <summary>
    /// Is this an animated layer?
    /// </summary>
    public abstract bool Animated { get; }

    /// <summary>
    /// Is the layer actually drawn. I.e., does it contribute to the sprites bounding box?
    /// </summary>
    public virtual bool Drawn => Visible;

    /// <summary>
    /// The direction type of this layer. This will be used by any parent <see cref="Layers.LayerCollection"/> to approximate
    /// the layer's bounding box.
    /// </summary>
    internal abstract RsiDirectionType DirectionType { get; }

    internal int DirectionCount => DirectionType switch
    {
        RsiDirectionType.Dir1 => 1,
        RsiDirectionType.Dir4 => 4,
        _ => 8,
    };

    /// <summary>
    /// The layer's transformation matrix, computed from the <see cref="Scale"/>,  <see cref="Rotation"/>, and
    /// <see cref="Offset"/>.
    /// </summary>
    internal Matrix3x2 Transform = Matrix3x2.Identity;

    /// <summary>
    /// The layer's local bounding box. This does not account for the layer's <see cref="Transform"/>.
    /// </summary>
    internal Box2? Bounds;

    [MemberNotNullWhen(false, nameof(Bounds))]
    internal bool BoundsDirty => Bounds == null;

    internal BaseLayer(SpriteSystem system, SpriteTreeSystem tree, EntityManager entMan, ISawmill log)
    {
        System = system;
        Tree = tree;
        EntMan = entMan;
        Log = log;
    }

    internal BaseLayer(BaseLayer toClone)
    {
        Scale = toClone.Scale;
        Rotation = toClone.Rotation;
        Offset = toClone.Offset;
        Color = toClone.Color;
        Visible = toClone.Visible;
        DirOverride = toClone.DirOverride;
        DirOffset = toClone.DirOffset;
        DirBehaviour = toClone.DirBehaviour;
        NoLighting = toClone.NoLighting;
        Strategy = toClone.Strategy;
        DrawDepth = toClone.DrawDepth;
        DirectionalDrawDepths = toClone.DirectionalDrawDepths?.ToArray();
        RsiOverride = toClone.RsiOverride;
        PostShaders = toClone.PostShaders?.ToArray();

        UpdateTransform();
        System = toClone.System;
        Tree = toClone.Tree;
        EntMan = toClone.EntMan;
        Log = toClone.Log;
    }

    internal abstract BaseLayer Clone();

    internal void UpdateTransform()
    {
        Transform = Matrix3Helpers.CreateTransform(Offset, Rotation, Scale);
    }

    /// <summary>
    /// Invalidate any cached information for this layer and any collections that it belongs to.
    /// </summary>
    /// <remarks>
    /// This does not update the <see cref="Transform"/> matrix.
    /// </remarks>
    [MustCallBase]
    internal virtual void InvalidateCache()
    {
        Bounds = null;
        Owner?.InvalidateCache();

        if (Collections == null)
            return;

        DebugTools.Assert(Owner == null || !Collections.Contains(Owner));
        foreach (var layer in Collections)
        {
            layer.InvalidateCache();
        }
    }

    public abstract Vector2 GetSize();
    public virtual Vector2i GetPixelSize() => (Vector2i) GetSize() * EyeManager.PixelsPerMeter;

    /// <summary>
    /// Get a layer's local bounding box relative. This does account for the layer's rotation and offset or scale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Box2 GetLocalBounds()
    {
        DebugTools.Assert(BoundsDirty || this is not LayerCollection coll || coll.Layers.All(x => x is not {BoundsDirty: true} || !x.Drawn));
        DebugTools.Assert(BoundsDirty || Bounds.Value.EqualsApprox(CalculateLocalBounds()));
        Bounds ??= CalculateLocalBounds();
        return Bounds.Value;
    }

    /// <summary>
    /// Compute a layer's local bounding box. This does account for the layer's rotation and offset or scale.\
    /// </summary>
    protected abstract Box2 CalculateLocalBounds();

    /// <summary>
    /// Compute this layer's bounding box relative to some owning <see cref="LayerCollection"/> while taking into
    /// account the offset rotation and scale. This bound is just an approximation and will generally over estimate the
    /// size of the bounds.
    /// </summary>
    internal Box2 CalculateRelativeBounds()
    {
        var size = GetSize();
        DebugTools.Assert(size.EqualsApprox(GetLocalBounds().Size));
        var longestSide = Math.Max(size.X, size.Y);

        // Longest possible side of the bounding box of a rotated layer
        var longestRotatedSide = Math.Max(longestSide, (size.X + size.Y) / MathF.Sqrt(2));

        // If this layer has any form of arbitrary rotation, return a bounding box big enough to cover
        // any possible rotation.
        if (Rotation != 0)
        {
            size = new Vector2(longestRotatedSide, longestRotatedSide);
            return Box2.CenteredAround(Offset, size * Scale);
        }

        var strat = Strategy ?? Owner?.Strategy;
        if (strat == LayerRenderingStrategy.SnapToCardinals)
        {
            // We won't know the actual direction it snaps to, so we have to assume the box is given by the longest side.
            size = new Vector2(longestSide, longestSide);
            return Box2.CenteredAround(Offset, size * Scale);
        }

        // Build the bounding box based on how many directions the sprite has
        size = DirectionType switch
        {
            RsiDirectionType.Dir4 => new Vector2(longestSide, longestSide),
            RsiDirectionType.Dir8 => new Vector2(longestRotatedSide, longestRotatedSide),
            _ => size
        };

        return Box2.CenteredAround(Offset, size * Scale);
    }

    /// <summary>
    ///     Converts an angle (between 0 and 2pi) to an RSI direction. This will slightly bias the angle to avoid flickering for
    ///     4-directional sprites.
    /// </summary>
    public RsiDirection GetDirection(Angle angle)
    {
        var dirType = DirectionType;
        if (dirType == RsiDirectionType.Dir1)
            return RsiDirection.South;

        if (dirType == RsiDirectionType.Dir8)
            return angle.GetDir().Convert(dirType);

        // For 4-directional sprites, as entities are often moving & facing diagonally, we will slightly bias the
        // angle to avoid the sprite flickering.

        // mod is -0.5 for angles between 0-90 and 180-270, and +0.5 for 90-180 and 270-360
        var mod = (Math.Floor(angle.Theta / MathHelper.PiOver2) % 2) - 0.5;

        var modTheta = angle.Theta + mod * System.DirectionBias;

        return ((int) Math.Round(modTheta / MathHelper.PiOver2) % 4) switch
        {
            0 => RsiDirection.South,
            1 => RsiDirection.East,
            2 => RsiDirection.North,
            _ => RsiDirection.West,
        };
    }

    public int CompareTo(BaseLayer? other) => Nullable.Compare(DrawDepth, other?.DrawDepth);


    #region Obsolete

    [Obsolete("Use RsiLayer.StateId")]
    public RSI.StateId RsiState => (this as RsiLayer)?.StateId ?? RSI.StateId.Invalid;
    [Obsolete("Use RsiLayer.State")]
    public RSI.State? ActualState => (this as RsiLayer)?.State;

    [Obsolete("Use GetSize()")]
    public Vector2 Size => GetSize();

    [Obsolete("Use GetPixelSize()")]
    public Vector2i PixelSize => GetPixelSize();

    [Obsolete("Use GetRsi()")]
    public RSI? ActualRsi => GetRsi();

    [Obsolete("Use GetRsi()")]
    public RSI? Rsi => GetRsi();

    [Obsolete("Use GetRsi()")]
    public RSI? RSI => GetRsi();

    [Obsolete("Use Shaders array")]
    public string? ShaderPrototype => PostShaders?.FirstOrDefault()?.Prototype?.Id;

    [Obsolete, Access(Other = AccessPermissions.ReadWriteExecute)]
    public void GetLayerDrawMatrix(RsiDirection dir, out Matrix3x2 mat)
    {
        var noRot = GetEntity()?.Comp.NoRotation ?? false;
        if (dir == RsiDirection.South || noRot)
            mat = Transform;
        else
            mat = Matrix3x2.Multiply(Clyde.RsiDirectionMatrices[(int) dir], Transform);
    }

    #endregion
}
