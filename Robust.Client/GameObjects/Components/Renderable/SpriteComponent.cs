using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Graphics.Clyde;
using Robust.Client.Sprite.Layers;
using Robust.Shared.Animations;
using Robust.Shared.ComponentTrees;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Sprite;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Client.GameObjects;

[RegisterComponent]
[Access(typeof(SpriteSystem), typeof(Clyde), Other = AccessPermissions.ReadExecute)]
public sealed partial class SpriteComponent : Component, IComponentTreeEntry<SpriteComponent>, IAnimationProperties
{
    internal SpriteSystem Sys = default!;

    /// <summary>
    /// This is the collection of layers that make up this sprite.
    /// </summary>
    public SpriteLayer Sprite
    {
        get
        {
            // TODO SPRITE turn property into field
            // For now, this exception is probably going to be helpful for people upgradding to the new engine version.
            if (SpriteInternal == null)
                throw new Exception($"Sprite component has not yet been initialized.");
            return SpriteInternal;
        }
    }
    internal SpriteLayer? SpriteInternal;

    [DataField(customTypeSerializer: typeof(ConstantSerializer<DrawDepth>))]
    public int DrawDepth = Robust.Shared.GameObjects.DrawDepth.Default;

    [ViewVariables] public bool ContainerOccluded;
    [ViewVariables] public bool InertUpdateQueued;
    [ViewVariables] public bool UpdateQueued;
    [ViewVariables, Access(Other = AccessPermissions.ReadWriteExecute)]
    public uint RenderOrder;
    [ViewVariables] public bool IsInert;

    #region Init Fields
    [DataField] internal string? State;
    [DataField] internal string? Texture;

    [IncludeDataField] public LayerCollectionData InitData = default!;

    /// <summary>
    /// See <see cref="BaseLayer.Strategy"/>
    /// </summary>
    [DataField] internal bool? NoRot;

    /// <summary>
    /// See <see cref="BaseLayer.Strategy"/>
    /// </summary>
    [DataField("snapCardinals")] internal bool? SnapCardinalsInternal;

    #endregion

    #region LayerCollection proxy properties
    [ViewVariables] public Vector2 Scale => Sprite.Scale;
    [ViewVariables] public Angle Rotation => Sprite.Rotation;
    [ViewVariables] public Vector2 Offset => Sprite.Offset;
    [ViewVariables] public Color Color => Sprite.Color;
    [ViewVariables] public bool Visible => Sprite.Visible;
    [ViewVariables] public RsiDirection? DirOverride => Sprite.DirOverride;
    [ViewVariables] public Matrix3x2 LocalMatrix => Sprite.Transform;
    [ViewVariables] public IReadOnlyList<BaseLayer> Layers => Sprite.Layers;
    [ViewVariables] public IReadOnlyDictionary<LayerKey, int> LayerMap => Sprite.LayerMap;
    [ViewVariables] public LayerRenderingStrategy? Strategy => Sprite.Strategy;
    [ViewVariables] public RSI? BaseRSI => Sprite.GetRsi();
    public BaseLayer this[int index] => Sprite[index];
    public BaseLayer this[LayerKey key] => Sprite[key];
    #endregion

    #region Component Tree Properties
    public DynamicTree<ComponentTreeEntry<SpriteComponent>>? Tree { get; set; }
    public EntityUid? TreeUid { get; set; }
    public bool AddToTree => Visible && !ContainerOccluded && Layers.Count > 0;
    public bool TreeUpdateQueued { get; set; }
    #endregion

    #region Obsolete

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use Strategy or SpriteSystem")]
    [ViewVariables] public bool NoRotation
    {
        get => Sprite.Strategy == LayerRenderingStrategy.NoRotation;
        set => Sprite.Strategy =  value ? LayerRenderingStrategy.NoRotation : default;
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use Strategy or SpriteSystem")]
    [ViewVariables] public bool SnapCardinals
    {
        get => Sprite.Strategy == LayerRenderingStrategy.SnapToCardinals;
        set => Sprite.Strategy = value ? LayerRenderingStrategy.SnapToCardinals : default;
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use Layers")]
    public IEnumerable<Layer> AllLayers => Layers.Where(l => l is Layer).Cast<Layer>();

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use shader list")]
    public ShaderInstance? PostShader
    {
        get => Sprite.PostShaders?.FirstOrDefault();
        set
        {
            if (value == null)
            {
                Sprite.PostShaders = null;
                return;
            }

            var shader = value.Mutable ? value : value.Duplicate();
            shader.RaiseEvent = RaiseShaderEvent;
            shader.GetScreenTexture = GetScreenTexture;
            Sprite.PostShaders = [shader];
        }
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use DirOverride or SpriteSystem")]
    public bool EnableDirectionOverride
    {
        get => Sprite.DirOverride != null;
        set => Sprite.DirOverride = value ? (Sprite.DirOverride ?? RsiDirection.South) : null;
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use DirOverride or SpriteSystem")]
    public Direction DirectionOverride
    {
        get => (Direction) (Sprite.DirOverride ?? default);
        set => Sprite.DirOverride = (RsiDirection) value;
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use SpriteSystem")]
    public void LayerSetShader(int layer, ShaderInstance? shader, string? prototype = null)
    {
        Sys.LayerSetShader((Owner, this), layer, shader);
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use SpriteSystem")]
    public void LayerSetShader(object layerKey, ShaderInstance shader, string? prototype = null)
    {
        Sys.LayerSetShader((Owner, this), AsKey(layerKey), shader);
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use SpriteSystem")]
    public void LayerSetShader(int layer, string shaderName)
    {
        Sys.LayerSetShader((Owner, this), layer, shaderName);
    }

    [Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete("Use SpriteSystem")]
    public void LayerSetShader(object layerKey, string shaderName)
    {
        Sys.LayerSetShader((Owner, this), AsKey(layerKey), shaderName);
    }

    [Obsolete("Use SpriteSystem.GetIcon() instead")]
    public IRsiStateLike? Icon => Sys.GetIcon(this);


    private bool _getScreenTexture;
    private bool _raiseShaderEvent;

    /// <summary>
    ///     Whether to pass the screen texture to the <see cref="PostShader"/>.
    /// </summary>
    /// <remarks>
    ///     Should be false unless you really need it.
    /// </remarks>
    [DataField, Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete]
    public bool GetScreenTexture
    {
        get => _getScreenTexture;
        set
        {
            _getScreenTexture = value;
            if (SpriteInternal?.PostShaders is not { } shaders)
                return;

            foreach (ref var shader in shaders.AsSpan())
            {
                if (!shader.Mutable)
                    shader = shader.Duplicate();
                shader.GetScreenTexture = value;
            }
        }
    }

    /// <summary>
    ///     If true, this raise a entity system event before rendering this sprite, allowing systems to modify the
    ///     shader parameters. Usually this can just be done via a frame-update, but some shaders require
    ///     information about the viewport / eye.
    /// </summary>
    [DataField, Access(Other = AccessPermissions.ReadWriteExecute)]
    [Obsolete]
    public bool RaiseShaderEvent
    {
        get => _raiseShaderEvent;
        set
        {
            _raiseShaderEvent = value;
            if (SpriteInternal?.PostShaders is not { } shaders)
                return;

            foreach (ref var shader in shaders.AsSpan())
            {
                if (!shader.Mutable)
                    shader = shader.Duplicate();
                shader.RaiseEvent = value;
            }
        }
    }

    private static LayerKey AsKey(object obj) => obj switch
    {
        string s => s,
        Enum e => e,
        LayerKey k => k,
        _ => throw new Exception($"Key must be either string, enum, or LayerKey but got {obj.GetType().Name}")
    };

    #endregion

    [Obsolete] // Suppress warnings
    void IAnimationProperties.SetAnimatableProperty(string name, object value)
    {
        switch (name)
        {
            case nameof(Rotation):
                Sys.SetRotation((Owner, this), (Angle) value);
                return;
            case nameof(Offset):
                Sys.SetOffset((Owner, this), (Vector2) value);
                return;
            case nameof(Scale):
                Sys.SetScale((Owner, this), (Vector2) value);
                return;
            case nameof(Color):
                Sys.SetColor((Owner, this), (Color) value);
                return;
        }

        if (!name.StartsWith("layer/"))
        {
            AnimationHelper.SetAnimatableProperty(this, name, value);
            return;
        }

        var delimiter = name.IndexOf("/", 6, StringComparison.Ordinal);
        var indexString = name.Substring(6, delimiter - 6);
        var index = int.Parse(indexString, CultureInfo.InvariantCulture);
        var layerProp = name.Substring(delimiter + 1);

        switch (layerProp)
        {
            case "texture":
                Sys.LayerSetTexture((Owner, this), index, new ResPath((string)value));
                return;
            case "state":
                Sys.LayerSetRsiState((Owner, this), index, (string)value);
                return;
            case "color":
                Sys.LayerSetColor((Owner, this), index, (Color)value);
                return;
            default:
                throw new ArgumentException($"Unknown layer property '{layerProp}'");
        }
    }
}
