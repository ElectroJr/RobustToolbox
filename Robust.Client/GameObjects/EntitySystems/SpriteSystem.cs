using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Robust.Client.ComponentTrees;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Sprite.Layers;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Client.GameObjects
{
    /// <summary>
    /// Updates the layer animation for every visible sprite.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class SpriteSystem : EntitySystem
    {
        public const float MinScale = 0.005f;

        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IEyeManager _eye = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IPrototypeManager _proto = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly IComponentFactory _factory = default!;

        // Note that any new system dependencies have to be added to RobustUnitTest.BaseSetup()
        [Dependency] private readonly SharedTransformSystem _xforms = default!;
        [Dependency] private readonly SpriteTreeSystem _tree = default!;
        [Dependency] private readonly AppearanceSystem _appearance = default!;

        public static readonly ProtoId<ShaderPrototype> UnshadedId = "unshaded";
        private readonly Queue<Entity<SpriteComponent>> _animationUpdateQueue = new();

        public static readonly ResPath TextureRoot = SpriteSpecifierSerializer.TextureRoot;

        /// <summary>
        ///     Entities that require a sprite frame update.
        /// </summary>
        private readonly List<EntityUid> _queuedFrameUpdate = new();

        private ISawmill _sawmill = default!;
        private EntityQuery<SpriteComponent> _query;

        /// <summary>
        ///     See <see cref="CVars.RenderSpriteDirectionBias"/>.
        /// </summary>
        public double DirectionBias = -0.05;

        public override void Initialize()
        {
            base.Initialize();

            UpdatesAfter.Add(typeof(SpriteTreeSystem));

            SubscribeLocalEvent<SpriteComponent, ComponentPreInitEvent>(OnPreInit);
            SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

            Subs.CVar(_cfg, CVars.RenderSpriteDirectionBias, OnBiasChanged, true);
            _sawmill = _logManager.GetSawmill("sprite");
            _query = GetEntityQuery<SpriteComponent>();
            InitializePrototypes();
        }

        private void InitializePrototypes()
        {
            var name = _factory.GetComponentName<SpriteComponent>();
            foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.TryGetComponent(name, out SpriteComponent? sprite))
                    InitializeSprite((EntityUid.Invalid, sprite));
            }
        }

        private void OnBiasChanged(double value)
        {
            DirectionBias = value;
        }

        private void DoUpdateIsInert(SpriteComponent component)
        {
            component.InertUpdateQueued = false;
            component.IsInert = component.Sprite.Animated;
        }

        /// <inheritdoc />
        public override void FrameUpdate(float frameTime)
        {
            while (_animationUpdateQueue.TryDequeue(out var sprite))
            {
                DoUpdateIsInert(sprite);
            }

            var realtime = _timing.RealTime.TotalSeconds;
            var syncQuery = GetEntityQuery<SyncSpriteComponent>();
            var metaQuery = GetEntityQuery<MetaDataComponent>();

            foreach (var uid in _queuedFrameUpdate)
            {
                if (!_query.TryGetComponent(uid, out var sprite))
                    continue;

                sprite.UpdateQueued = false;
                if (sprite.IsInert)
                    continue;

                if (metaQuery.GetComponent(uid).EntityPaused)
                    continue;

                var sync = syncQuery.HasComponent(uid);
                foreach (var baseLayer in sprite.Layers)
                {
                    if (baseLayer is not RsiLayer {Animated: true} layer)
                        continue;

                    if (sync)
                    {
                        layer.AnimationTime = (float)(realtime % layer.State!.TotalDelay);
                        layer.AnimationTimeLeft = -layer.AnimationTime;
                        layer.AnimationFrame = 0;
                    }
                    else
                    {
                        layer.AnimationTime += frameTime;
                        layer.AnimationTimeLeft -= frameTime;
                    }

                    layer.AdvanceFrameAnimation();
                }
            }

            _queuedFrameUpdate.Clear();
        }

        /// <summary>
        ///     Force update of the sprite component next frame
        /// </summary>
        public void ForceUpdate(EntityUid uid)
        {
            _queuedFrameUpdate.Add(uid);
        }

        /// <summary>
        /// Gets the specified frame for this sprite at the specified time.
        /// </summary>
        /// <param name="loop">Should we clamp on the last frame and not loop</param>
        public Texture GetFrame(SpriteSpecifier spriteSpec, TimeSpan curTime, bool loop = true)
        {
            Texture? sprite = null;

            switch (spriteSpec)
            {
                case SpriteSpecifier.Rsi rsi:
                    var rsiActual = _resourceCache.GetResource<RSIResource>(rsi.RsiPath).RSI;
                    rsiActual.TryGetState(rsi.RsiState, out var state);
                    var frames = state!.GetFrames(RsiDirection.South);
                    var delays = state.GetDelays();
                    var totalDelay = delays.Sum();

                    // No looping
                    if (!loop && curTime.TotalSeconds >= totalDelay)
                    {
                        sprite = frames[^1];
                    }
                    // Loopable
                    else
                    {
                        var time = curTime.TotalSeconds % totalDelay;
                        var delaySum = 0f;

                        for (var i = 0; i < delays.Length; i++)
                        {
                            var delay = delays[i];
                            delaySum += delay;

                            if (time > delaySum)
                                continue;

                            sprite = frames[i];
                            break;
                        }
                    }

                    sprite ??= Frame0(spriteSpec);
                    break;
                case SpriteSpecifier.Texture texture:
                    sprite = GetTexture(texture);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return sprite;
        }
    }

    /// <summary>
    ///     This event gets raised before a sprite gets drawn using it's post-shader.
    /// </summary>
    [ByRefEvent]
    public readonly struct BeforePostShaderRenderEvent(
        Entity<SpriteComponent> entity,
        BaseLayer layer,
        ShaderInstance shader,
        IClydeViewport viewport)
    {
        public readonly Entity<SpriteComponent> Entity = entity;
        public readonly IClydeViewport Viewport = viewport;
        public SpriteComponent Sprite => Entity.Comp;
        public readonly BaseLayer Layer = layer;
        public readonly ShaderInstance Shader  = shader;
    }
}
