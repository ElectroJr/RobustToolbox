using System.Diagnostics.CodeAnalysis;
using Robust.Client.ComponentTrees;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Light;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Robust.Client.GameObjects
{
    public sealed class PointLightSystem : SharedPointLightSystem
    {
        [Dependency] private readonly LightTreeSystem _lightTree = default!;
        [Dependency] private readonly IPrototypeManager _protoMan = default!;

        public static ProtoId<LightMaskPrototype> DefaultMask = "default";
        private LightMaskPrototype _defaultMask = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<PointLightComponent, ComponentInit>(HandleInit);
            SubscribeLocalEvent<PointLightComponent, ComponentHandleState>(OnLightHandleState);
            _defaultMask = _protoMan.Index(DefaultMask);
        }

        private void OnLightHandleState(EntityUid uid, PointLightComponent component, ref ComponentHandleState args)
        {
            if (args.Current is not PointLightComponentState state)
                return;

            component.Enabled = state.Enabled;
            component.Offset = state.Offset;
            component.Softness = state.Softness;
            component.CastShadows = state.CastShadows;
            component.Energy = state.Energy;
            component.Radius = state.Radius;
            component.Color = state.Color;

            _lightTree.QueueTreeUpdate(uid, component);
        }

        public override SharedPointLightComponent EnsureLight(EntityUid uid)
        {
            return EnsureComp<PointLightComponent>(uid);
        }

        public override bool ResolveLight(EntityUid uid, [NotNullWhen(true)] ref SharedPointLightComponent? component)
        {
            if (component is not null)
                return true;

            TryComp<PointLightComponent>(uid, out var comp);
            component = comp;
            return component != null;
        }

        public override bool TryGetLight(EntityUid uid, [NotNullWhen(true)] out SharedPointLightComponent? component)
        {
            if (TryComp<PointLightComponent>(uid, out var comp))
            {
                component = comp;
                return true;
            }

            component = null;
            return false;
        }

        public override bool RemoveLightDeferred(EntityUid uid)
        {
            return RemCompDeferred<PointLightComponent>(uid);
        }

        protected override void UpdatePriority(EntityUid uid, SharedPointLightComponent comp, MetaDataComponent meta)
        {
        }

        private void HandleInit(Entity<PointLightComponent> light, ref ComponentInit args)
        {
            SetMask(light!, light.Comp.Mask);
        }

        public void SetMask(Entity<PointLightComponent?> light, ProtoId<LightMaskPrototype>? mask)
        {
            if (!Resolve(light.Owner, ref light.Comp))
                return;

            light.Comp.MaskPrototype = mask == null ? _defaultMask : _protoMan.Index(mask.Value);
        }

        #region Setters

        public void SetContainerOccluded(EntityUid uid, bool occluded, SharedPointLightComponent? comp = null)
        {
            if (!ResolveLight(uid, ref comp) || occluded == comp.ContainerOccluded || comp is not PointLightComponent clientComp)
                return;

            comp.ContainerOccluded = occluded;
            Dirty(uid, comp);

            if (comp.Enabled)
                _lightTree.QueueTreeUpdate(uid, clientComp);
        }

        public override void SetEnabled(EntityUid uid, bool enabled, SharedPointLightComponent? comp = null, MetaDataComponent? meta = null)
        {
            if (!ResolveLight(uid, ref comp) || enabled == comp.Enabled || comp is not PointLightComponent clientComp)
                return;

            base.SetEnabled(uid, enabled, comp, meta);
            if (!comp.ContainerOccluded)
                _lightTree.QueueTreeUpdate(uid, clientComp);
        }

        public override void SetRadius(EntityUid uid, float radius, SharedPointLightComponent? comp = null, MetaDataComponent? meta = null)
        {
            if (!ResolveLight(uid, ref comp) || MathHelper.CloseToPercent(radius, comp.Radius) ||
                comp is not PointLightComponent clientComp)
                return;

            base.SetRadius(uid, radius, comp, meta);
            if (clientComp.TreeUid != null)
                _lightTree.QueueTreeUpdate(uid, clientComp);
        }
        #endregion
    }
}
