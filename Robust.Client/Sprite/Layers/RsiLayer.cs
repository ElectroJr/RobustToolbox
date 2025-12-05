using System.Numerics;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;
using Direction = Robust.Shared.Maths.Direction;

namespace Robust.Client.Sprite.Layers;

/// <summary>
/// This is a base class for all layers that contain texture information, either directly or via an RSI state.
/// </summary>
[Access(typeof(SpriteComponent), typeof(SpriteSystem), typeof(BaseLayer), Other = AccessPermissions.ReadExecute)]
public abstract class RsiLayer : BaseLayer
{
    /// <summary>
    /// The desired Rsi State.
    /// </summary>
    /// <remarks>
    /// If this is a valid id, and the layer has a Rsi to pull states from, this should match to the
    /// <see cref="State"/>'s <see cref="RSI.State.StateId"/> property. However, if there was an error, the
    /// actual value of <see cref="State"/> may be a fallback/error state.
    /// </remarks>
    [ViewVariables] public RSI.StateId StateId;

    /// <summary>
    /// The Rsi state corresponding to <see cref="StateId"/> fetched from this Layer's RSI
    /// (see <see cref="BaseLayer.GetRsi"/>).
    /// </summary>
    [ViewVariables] public RSI.State? State;

    /// <summary>
    /// A texture to use when rendering this layer. This is only used if <see cref="State"/> is null.
    /// </summary>
    [ViewVariables] public Texture? Texture;

    [ViewVariables] public float AnimationTimeLeft;
    [ViewVariables] public float AnimationTime;
    [ViewVariables] public int AnimationFrame;

    /// <inheritdoc cref="RsiLayerData.AnimationBehaviour"/>
    [ViewVariables] public AnimationBehaviour AnimationBehaviour;

    /// <summary>
    /// Is the animation currently playing in reverse.
    /// </summary>
    [ViewVariables] public bool Reversed;

    public override bool Drawn => Visible && (State != null || Texture != null);
    internal override RsiDirectionType DirectionType => State?.RsiDirections ?? RsiDirectionType.Dir1;
    protected override Box2 CalculateLocalBounds() => _localBounds;
    public override Vector2 GetSize() => _size;
    public override Vector2i GetPixelSize() => _pixelSize;
    private Vector2 _size;
    private Vector2i _pixelSize;
    private Box2 _localBounds;
    public override bool Animated => Visible && AutoAnimated && State is {IsAnimated: true};

    internal RsiLayer(SpriteSystem system, SpriteTreeSystem tree, EntityManager entMan, ISawmill log) :
        base(system, tree, entMan, log)
    {
    }

    internal RsiLayer(RsiLayer toClone) : base(toClone)
    {
        StateId = toClone.StateId;
        State = toClone.State;
        Texture = toClone.Texture;
        _size = toClone._size;
        _pixelSize = toClone._pixelSize;
        AnimationTimeLeft = toClone.AnimationTimeLeft;
        AnimationTime = toClone.AnimationTime;
        AnimationFrame = toClone.AnimationFrame;
        Reversed = toClone.Reversed;
        AnimationBehaviour = toClone.AnimationBehaviour;
        AutoAnimated = toClone.AutoAnimated;
    }

    public RsiDirection EffectiveDirection(Angle worldRotation)
    {
        if (State == null || State.RsiDirections == RsiDirectionType.Dir1)
            return RsiDirection.South;

        return (DirOverride ?? worldRotation.ToRsiDirection(State.RsiDirections)).OffsetRsiDir(DirOffset);
    }

    internal void AdvanceFrameAnimation()
    {
        if (State == null)
            return;

        var delayCount = State.DelayCount;
        while (AnimationTimeLeft < 0)
        {
            if (Reversed)
            {
                AnimationFrame -= 1;

                if (AnimationFrame < 0)
                {
                    if (AnimationBehaviour != AnimationBehaviour.Loop)
                    {
                        // stop at first frame
                        AnimationFrame = 0;
                        AnimationTimeLeft = 0;
                        AutoAnimated = false;
                        InvalidateCache();
                        return;
                    }

                    if (AnimationBehaviour == AnimationBehaviour.Cycle)
                    {
                        AnimationFrame = 1;
                        Reversed = false;
                    }
                    else
                    {
                        AnimationFrame = delayCount - 1;
                    }

                    AnimationTime = -AnimationTimeLeft;
                }
            }
            else
            {
                AnimationFrame += 1;

                if (AnimationFrame >= delayCount)
                {
                    if (AnimationBehaviour != AnimationBehaviour.Loop)
                    {
                        // stop at last frame
                        AnimationFrame = delayCount - 1;
                        AnimationTimeLeft = 0;
                        AutoAnimated = false;
                        InvalidateCache();
                        return;
                    }

                    if (AnimationBehaviour == AnimationBehaviour.Cycle)
                    {
                        AnimationFrame = delayCount - 2;
                        Reversed = true;
                    }
                    else
                    {
                        AnimationFrame = 0;
                    }

                    AnimationTime = -AnimationTimeLeft;
                }
            }

            AnimationTimeLeft += State.GetDelay(AnimationFrame);
        }
    }

    internal override void InvalidateCache()
    {
        base.InvalidateCache();
        _pixelSize = (State?.Size ?? Texture?.Size ?? default);
        _size = (Vector2) _pixelSize / EyeManager.PixelsPerMeter;
        Bounds = _localBounds = Box2.CentredAroundZero(_size);
    }
}
