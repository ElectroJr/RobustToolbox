using System;
using System.Collections.Generic;
using System.Numerics;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.Graphics;
using Robust.Client.Graphics.Clyde;
using Robust.Client.ResourceManagement;
using Robust.Client.Sprite.Layers;
using Robust.Client.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Client.GameObjects;

using SpriteData = Clyde.SpriteData;
using RenderHandle = Clyde.RenderHandle;

// this partial class contains code specific to querying, processing & sorting sprites.
public sealed partial class SpriteSystem
{
    [Dependency] private readonly IClydeInternal _clyde = default!;

    /// <summary>
    /// Stack of render targets used when rendering a complex sprite with multiple nested shaders.
    /// </summary>
    private readonly Stack<ClydeHandle> _shaderTargetStack = new();

    private readonly Stack<Box2i> _vpStack = new();

    /// <summary>
    /// Cached render targets for use when drawing layers with post-proccessing shaders.
    /// </summary>
    /// <remarks>
    /// This is a stack, because when drawing sprites with nested layers, the largest texture will always be needed first.
    /// </remarks>
    private readonly Stack<IRenderTexture> _shaderTargetPool = new();
    // TODO SPRITE dispose?

    /// <summary>
    /// Cached screen texture.
    /// </summary>
    /// <remarks>
    /// Some sprite shaders need to use the current screen texture, but this doesn't really work with nested layers/shaders.
    /// I.e., what do you do when a sprite has two shaders that both want the screen texture? Currently, the texture is
    /// just obtained at most one time, and is cleared whenever a shaded sprite layer has been drawn to the main
    /// viewport texture.
    /// </remarks>
    private Texture? _cachedSreenTexture;

    internal void RenderSprite(
        RenderHandle handle,
        Entity<SpriteComponent> ent,
        Angle eyeRot,
        Angle worldRot,
        Vector2 worldPos,
        RsiDirection? overrideDirection = null)
    {
        // TODO SPRITE
        // Add a variant of this method that supports sprite shaders

        var entry = new SpriteData
        {
            Uid = ent.Owner,
            Sprite = ent.Comp,
            WorldPos = worldPos,
            WorldRot = worldRot
        };

        RenderSprite(handle, entry, eyeRot, overrideDirection);
    }

    internal void RenderSprite(
        RenderHandle handle,
        in SpriteData entry,
        Angle eyeRot,
        RsiDirection? overrideDirection = null)
    {
        QueueFrameUpdate(entry.Uid, entry.Sprite);
        var sprite = entry.Sprite.Sprite;

        // Determine the RSI direction
        var entityAngle = entry.WorldRot + eyeRot;
        entityAngle = entityAngle.Reduced().FlipPositive();
        var dir = overrideDirection ?? (sprite.DirOverride ?? sprite.GetDirection(entityAngle)).OffsetRsiDir(sprite.DirOffset);

        var strat = sprite.Strategy ?? default;
        var drawAngle = strat switch
        {
            LayerRenderingStrategy.SnapToCardinals => entry.WorldRot - entityAngle.RoundToCardinalAngle(),
            LayerRenderingStrategy.NoRotation => -eyeRot,
            _ => entry.WorldRot
        };

        var args = new DrawArgs
        {
            Transform = Matrix3Helpers.CreateTransform(entry.WorldPos, drawAngle),
            Color = Color.White,
            Dir = dir,
            EyeRot = eyeRot,
            Strat = strat,
            Granular = sprite.Granular,
            NoLighting = false,
        };

        if (args.Granular)
        {
            // Im uhh... using the LayerCollection object to pass optional arguments to avoid having to create
            // a bunch of overloads that take in extra arguments, or having to bloat the DrawArgs struct even more.
            // Theres probably a better way to do this, but 99% of the sprites shouldn't have granular rendering.
            sprite.WorldRot = entry.WorldRot;
            sprite.WorldPos = entry.WorldPos;
            sprite.ParentTransform = Matrix3x2.Identity;
        }

        RenderLayer(handle, sprite, in args, null);
    }

    private void RenderLayer(
        RenderHandle handle,
        BaseLayer layer,
        in DrawArgs parentArgs,
        LayerCollection? parent)
    {
        if (!layer.Visible)
            return;

        var args = parentArgs.Update(layer);

        // TODO Sprite
        // can this check be moved elsewhere out of the hot path without causing possible infinite recursion?
        if (layer is LayerCollection {DrawTogether: true, PostShaders: null})
        {
            // draw-together just re-used the code for nested shader rendering
            RenderLayerWithPostShader(handle, layer, ref args, parent, ReadOnlySpan<ShaderInstance>.Empty);
            return;
        }

        if (layer.PostShaders == null || layer.PostShaders.Length <= 0)
            RenderLayerDirect(handle, layer, ref args, parent);
        else
            RenderLayerWithPostShader(handle, layer, ref args, parent, layer.PostShaders);
    }

    /// <summary>
    /// Render a layer directly to the current render target.
    /// </summary>
    private void RenderLayerDirect(
        RenderHandle handle,
        BaseLayer layer,
        ref DrawArgs args,
        LayerCollection? parent)
    {
        // The vast majority of layers are simple layers with RSI states
        // So first branch checks for those.
        var cast = layer as Layer;
        if (cast is {State: { } state})
        {
            // TODO SPRITE RENDERING
            // This matrix transformation can be removed for 4- and 1-directional sprites. Its only really required for
            // 8-directional sprites. But this requires changing the way RSIs are loaded to pre-rotate the textures.
            if (args.Dir != RsiDirection.South && args.Strat != LayerRenderingStrategy.NoRotation)
                args.Transform = Matrix3x2.Multiply(RsiDirectionMatrices[(int) args.Dir], args.Transform);

            args.Dir = state.RsiDirections == RsiDirectionType.Dir1
                ? RsiDirection.South
                : (layer.DirOverride ?? args.Dir).OffsetRsiDir(layer.DirOffset);

            var texture = state.GetFrame(args.Dir, cast.AnimationFrame);
            RenderTextureLayer(handle, texture, in args);
            return;
        }

        if (cast != null)
        {
            RenderTextureLayer(handle, cast.Texture, in args);
            return;
        }

        // Allow layers to re-compute the direction.
        // This is mainly useful for sprites that have the sprites of other entities embedded within them.
        if (layer.DirBehaviour == DirectionBehaviour.Entity)
        {
            var ent = layer.GetEntity();
            var entityAngle = ent == null ? default : _xforms.GetWorldRotation(ent.Value.Owner);
            entityAngle += (entityAngle + args.EyeRot).Reduced().FlipPositive();
            args.Dir = layer.GetDirection(entityAngle);
        }

        args.Dir = (layer.DirOverride ?? args.Dir).OffsetRsiDir(layer.DirOffset);

        if (layer is ShaderLayer shader)
        {
            HandleShaderLayer(shader, args.Dir, parent);
            return;
        }

        if (layer is not LayerCollection collection)
            return;

        if (args.Granular || collection.DirectionType == RsiDirectionType.Dir8)
        {
            RenderLayerCollectionSlow(handle, collection, in args);
            return;
        }

        foreach (var child in collection.GetSortedLayers(args.Dir))
        {
            RenderLayer(handle, child, in args, collection);
        }
    }

    /// <summary>
    /// Render a simple layer texture.
    /// </summary>
    private void RenderTextureLayer(RenderHandle handle, Texture? texture, in DrawArgs args)
    {
        if (texture == null)
            return;

        var color = args.Color;
        if (args.NoLighting)
        {
            DebugTools.Assert(args.Color is {R: >= 0, G: >= 0, B: >= 0, A: >= 0}, "Default shader should not be used with negative color modulation.");

            // Negative color modulation values are by the default shader to disable light shading.
            // Specifically we set colour = - 1 - colour
            // This is good enough to ensure that non-negative values become negative & is trivially invertible.
            color = new Color(new Vector4(-1) - args.Color.RGBA);
        }

        var textureSize = texture.Size / (float) EyeManager.PixelsPerMeter;
        var quad = Box2.CentredAroundZero(textureSize);
        handle.SetModelTransform(args.Transform);
        handle.DrawTextureWorld(
            texture,
            quad.BottomLeft,
            quad.BottomRight,
            quad.TopLeft,
            quad.TopRight,
            color,
            null);
    }

    /// <summary>
    /// Handle a "fake layer" that just exists to modify the parameters of a shader being used by some other
    /// layer.
    /// </summary>
    private void HandleShaderLayer(
        ShaderLayer layer,
        RsiDirection dir,
        LayerCollection? collection)
    {
        // Check people aren't doing silly things with shader layers.
        DebugTools.Assert(layer.Transform.EqualsApprox(Matrix3x2.Identity), "Transforms have no effect on shader layers");
        DebugTools.AssertNull(layer.PostShaders, "oddly enough, shader layers don't support shaders");
        DebugTools.AssertNotNull(collection, "Attempting to draw a shader layer without a collection?");
        if (collection == null || !collection.TryGetLayer(layer.Key, out Layer? target))
        {
            DebugTools.Assert("Shader layer has no target.");
            return;
        }

        if (target.Shader == null)
            return;

        var texture = layer.State?.GetFrame(dir, layer.AnimationFrame) ?? layer.Texture;
        if (texture == null)
            return;

        if (!target.Shader.Mutable)
            target.Shader = target.Shader.Duplicate();

        var clydeTexture = RenderHandle.ExtractTexture(texture, null, out var csr);

        if (layer.ParameterTexture is { } paramTexture)
            target.Shader.SetParameter(paramTexture, clydeTexture);

        if (layer.ParameterUV is not { } paramUV)
            return;

        var sr = RenderHandle.WorldTextureBoundsToUV(clydeTexture, csr);
        var uv = new Vector4(sr.Left, sr.Bottom, sr.Right, sr.Top);
        target.Shader.SetParameter(paramUV, uv);
    }

    /// <summary>
    /// Render a layer collection with finer control over the rendering strategy. This requires re-computing
    /// the sprite transformation matrix for each possible <see cref="LayerRenderingStrategy"/>.
    /// </summary>
    private void RenderLayerCollectionSlow(
        RenderHandle handle,
        LayerCollection collection,
        in DrawArgs args)
    {
        DebugTools.Assert(args.Granular || collection.DirectionType == RsiDirectionType.Dir8);
        var entityAngle = (collection.WorldRot + args.EyeRot).Reduced().FlipPositive();
        var cardinal = entityAngle.RoundToCardinalAngle();
        var transform = Matrix3x2.Multiply(collection.Transform, collection.ParentTransform);

        var defaultArgs = args with
        {
            Transform = Matrix3x2.Multiply(transform, Matrix3Helpers.CreateTransform(collection.WorldPos, collection.WorldRot)),
            Strat = LayerRenderingStrategy.Default
        };

        var snapArgs = args with
        {
            Transform = Matrix3x2.Multiply(transform, Matrix3Helpers.CreateTransform(collection.WorldPos, collection.WorldRot - cardinal)),
            Strat = LayerRenderingStrategy.SnapToCardinals
        };

        var noRotArgs = args with
        {
            Transform = Matrix3x2.Multiply(transform, Matrix3Helpers.CreateTransform(collection.WorldPos, -args.EyeRot)),
            Strat = LayerRenderingStrategy.NoRotation
        };

        var dirType = collection.DirectionType;

        foreach (var layer in collection.GetSortedLayers(args.Dir))
        {
            var strat = layer.Strategy ?? args.Strat;
            if (layer is LayerCollection {Granular: true} coll)
            {
                coll.WorldRot = collection.WorldRot;
                coll.WorldPos = collection.WorldPos;
                coll.ParentTransform = transform;
            }

            // I fucking hate 8-dir sprite. They complicate everything.
            // So fuck it, 4-dir children of 8-dir sprites are just going to ignore the parents direction overrides & offsets
            // TODO SPRITE maybe fix this one day.
            var dir = layer.DirectionType == RsiDirectionType.Dir4 && dirType == RsiDirectionType.Dir8
                ? layer.GetDirection(entityAngle)
                : args.Dir;

            noRotArgs.Dir = dir;
            snapArgs.Dir = dir;
            defaultArgs.Dir = dir;

            switch (strat)
            {
                case LayerRenderingStrategy.NoRotation:
                    RenderLayer(handle, layer, in noRotArgs, collection);
                    break;
                case LayerRenderingStrategy.SnapToCardinals:
                    RenderLayer(handle, layer, in snapArgs, collection);
                    break;
                default:
                    RenderLayer(handle, layer, in defaultArgs, collection);
                    break;
            }
        }
    }

    /// <summary>
    /// Render a layer to a shader render target before drawing it back to the current target (usually the viewport).
    /// </summary>
    private void RenderLayerWithPostShader(
        RenderHandle handle,
        BaseLayer layer,
        ref DrawArgs args,
        LayerCollection? parent,
        ReadOnlySpan<ShaderInstance> shaders)
    {
        // This method is also used by "draw together" layer collections. So there may not actually be a shader to use.
        ShaderInstance? shader = null;
        if (shaders.Length > 0)
        {
            shader = shaders[0];
            shaders = shaders[1..];
            if (shader.GetScreenTexture)
                shader.SetParameter("SCREEN_TEXTURE", GetScreenTexture(handle));

            if (shader.RaiseEvent && layer.GetEntity() is { } ent)
            {
                // TODO SPRITE
                // If this is entity is being drawn as a layer within some other entity, its possible that this event goes to the wrong target?
                // I.e., maybe we want to target the root entity?
                var ev = new BeforePostShaderRenderEvent(ent, layer, shader, handle.CurrentViewport!);
                RaiseLocalEvent(ent, ref ev);
            }
        }

        // get the size of the layer on screen, scaled slightly to allow for shaders that increase the final layer size.
        var bb = GetScreenBB(layer);
        var requiredRtSize = (Vector2i)(bb.Size * Clyde.PostShadeScale).Rounded();

        // I'm not 100% sure why it works, but without it post-shader
        // can be lower or upper by 1px than original sprite depending on sprite rotation or scale
        // probably some rotation rounding error
        // TODO SPRITE Why is this? is it even true?
        // Was this from before PostShadeScale was introduced and 32x32 sprites were accidentally rounded down?
        if (requiredRtSize.X % 2 != 0)
            requiredRtSize.X++;
        if (requiredRtSize.Y % 2 != 0)
            requiredRtSize.Y++;

        DebugTools.Assert(requiredRtSize is {X: > 0, Y: > 0});

        // Switch to a new render target to render this shader.
        _shaderTargetStack.Push(handle.CurrentTarget);
        var target = GetPostShaderTarget(requiredRtSize);
        handle.UseRenderTarget(target);
        handle.Clear(default, 0, ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);

        // The sprite rendering uses the normal transformation matrices to ensure that the light-map sampling works as
        // if we were just drawing to the normal viewport. So to ensure we actually draw correctly to our post-shader
        // target we need to do this gl viewport fuckery that I'm only 70% sure I understand.
        var vpSize = handle.CurrentViewport?.Size ?? default;
        var roundedPos = (Vector2i) bb.Center;
        var flippedPos = new Vector2i(roundedPos.X, vpSize.Y - roundedPos.Y);
        flippedPos -= target.Size / 2;
        var vp = Box2i.FromDimensions(-flippedPos, vpSize);
        _vpStack.Push(vp);
        handle.Viewport(vp);

        var color = args.Color;
        args.Color = Color.White;

        if (shaders.Length <= 0)
            RenderLayerDirect(handle, layer, ref args, parent);
        else
            RenderLayerWithPostShader(handle, layer, ref args, parent, shaders);

        handle.GetProjView(out var oldProj, out var oldView);
        handle.UseRenderTarget(_shaderTargetStack.Pop());
        if (_shaderTargetStack.Count == 0)
            _cachedSreenTexture = null;

        handle.UseShader(shader);
        handle.SetModelTransform(Matrix3x2.Identity);
        handle.CalcScreenMatrices(out var proj, out var view);
        handle.SetProjView(proj, view);

        // TODO SPRITE
        // this is applicable to the base viewport, not nested viewports
        // need to have the local screen posoition, not roundedPos
        var rounded = roundedPos - target.Size / 2;
        var box = Box2i.FromDimensions(rounded, target.Size);

        handle.DrawTextureScreen(
            target.Texture,
            box.BottomLeft,
            box.BottomRight,
            box.TopLeft,
            box.TopRight,
            color,
            null);

        handle.SetProjView(oldProj, oldView);
        handle.UseShader(null);

        _shaderTargetPool.Push(target);
        _vpStack.Pop();

        // Revert back to previous VP. If the stack is empty, we are back to drawing to the normal IClydeViewport
        // In that case, the render-target switch will have reset the OpenGL viewport back to the default.
        if (_vpStack.TryPeek(out var prev))
            handle.Viewport(prev);

    }

    Texture GetScreenTexture(RenderHandle handle)
    {
        if (_cachedSreenTexture != null)
            return _cachedSreenTexture;

        if (handle.CurrentViewport != null)
        {
            _clyde.FlushRenderQueue();
            _cachedSreenTexture = _clyde.CopyScreenTexture(handle.CurrentViewport.RenderTarget);
        }

        _cachedSreenTexture ??= _resourceCache.GetFallback<TextureResource>().Texture;
        return _cachedSreenTexture;
    }

    private Box2 GetScreenBB(BaseLayer layer)
    {
        // TODO SPRITE
        throw new NotImplementedException();
    }

    private IRenderTexture GetPostShaderTarget(Vector2i size)
    {
        if (_shaderTargetPool.TryPop(out var next)
            && next.Size.X >= size.X
            && next.Size.Y >= size.Y)
        {
            return next;
        }

        // This is quite inefficient target re-use
        // If the next available target is not large enough, we just discard it and makes a bigger one.
        // In principle, we could keep the smaller targets around... but whatever.
        // In the end, the pool should be a Matryoshka doll of ever smaller render targets.
        next?.Dispose();
        return _clyde.CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb, true), name: "Layer Collection Target");
    }


    private struct DrawArgs
    {
        public Matrix3x2 Transform;
        public Color Color;
        public Angle EyeRot;
        public RsiDirection Dir;
        public LayerRenderingStrategy Strat;
        public bool Granular;
        public bool NoLighting;

        /// <summary>
        /// Apply a layer's rendering options and return an updated set of arguments.
        /// </summary>
        /// <remarks>
        /// Direction overrides & offsets are applied separately.
        /// </remarks>
        public readonly DrawArgs Update(BaseLayer layer)
        {
            return this with
            {
                Transform = Matrix3x2.Multiply(layer.Transform, Transform),
                Color = Color * layer.Color,
                NoLighting = NoLighting | layer.NoLighting,
                Strat = Granular ? Strat : layer.Strategy ?? Strat,
            };
        }
    }

    internal static readonly Matrix3x2[] RsiDirectionMatrices =
    [
        // array order chosen such that this array can be indexed by casing an RSI direction to an int
        Matrix3x2.Identity,
        Matrix3Helpers.CreateRotation(-Direction.North.ToAngle()),
        Matrix3Helpers.CreateRotation(-Direction.East.ToAngle()),
        Matrix3Helpers.CreateRotation(-Direction.West.ToAngle()),
        Matrix3Helpers.CreateRotation(-Direction.SouthEast.ToAngle()),
        Matrix3Helpers.CreateRotation(-Direction.SouthWest.ToAngle()),
        Matrix3Helpers.CreateRotation(-Direction.NorthEast.ToAngle()),
        Matrix3Helpers.CreateRotation(-Direction.NorthWest.ToAngle())
    ];
}
