using System;
using System.Collections.Generic;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Sprite.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Sprite;
using Robust.Shared.Utility;

namespace Robust.Client.Animations
{
    /// <summary>
    ///     An animation track that plays RSI state animations manually, so they can be precisely controlled etc.
    /// </summary>
    public sealed class AnimationTrackSpriteFlick : AnimationTrack
    {
        /// <summary>
        ///     A list of key frames for when to fire flicks.
        /// </summary>
        public List<KeyFrame> KeyFrames { get; private set; } = new();

        // TODO: Should this layer key be per keyframe maybe?
        /// <summary>
        ///     The layer key of the layer to flick on.
        /// </summary>
        public LayerKey? LayerKey { get; set; }

        public override (int KeyFrameIndex, float FramePlayingTime) InitPlayback()
        {
            if (LayerKey == null)
            {
                throw new InvalidOperationException("Must set LayerKey.");
            }

            return (-1, 0);
        }

        public override (int KeyFrameIndex, float FramePlayingTime)
            AdvancePlayback(object context, int prevKeyFrameIndex, float prevPlayingTime, float frameTime)
        {
            DebugTools.AssertNotNull(LayerKey);

            var entity = (EntityUid) context;
            var ent = IoCManager.Resolve<IEntityManager>();
            var sys = ent.System<SpriteSystem>();

            var playingTime = prevPlayingTime + frameTime;
            var keyFrameIndex = prevKeyFrameIndex;
            // Advance to the correct key frame.
            while (keyFrameIndex != KeyFrames.Count - 1 && KeyFrames[keyFrameIndex + 1].KeyTime < playingTime)
            {
                playingTime -= KeyFrames[keyFrameIndex + 1].KeyTime;
                keyFrameIndex += 1;
            }

            if (keyFrameIndex < 0)
                return (keyFrameIndex, playingTime);

            var keyFrame = KeyFrames[keyFrameIndex];

            if (LayerKey == null
                || !sys.TryGetLayer(entity, LayerKey!.Value, out RsiLayer? layer)
                || layer.State is not { } state)
            {
                return (keyFrameIndex, playingTime);
            }

            var animationTime = Math.Min(state.AnimationLength - 0.01f, playingTime);
            sys.LayerSetAutoAnimated(layer, false);
            sys.LayerSetRsiState(layer, keyFrame.State);
            sys.LayerSetAnimationTime(layer, animationTime);
            return (keyFrameIndex, playingTime);
        }

        public struct KeyFrame
        {
            /// <summary>
            ///     The RSI state to play when this keyframe gets triggered.
            /// </summary>
            public readonly RSI.StateId State;

            /// <summary>
            ///     The time between this keyframe and the last.
            /// </summary>
            public readonly float KeyTime;

            public KeyFrame(RSI.StateId state, float keyTime)
            {
                State = state;
                KeyTime = keyTime;
            }
        }
    }
}
