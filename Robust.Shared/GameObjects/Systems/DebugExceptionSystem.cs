using System;

namespace Robust.Shared.GameObjects;

public sealed class DebugExceptionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DebugExceptionOnAddComponent, ComponentPreInitEvent>(OnCompPreInit);
        SubscribeLocalEvent<DebugExceptionInitializeComponent, ComponentInit>((_, _, _) => throw new NotSupportedException());
        SubscribeLocalEvent<DebugExceptionStartupComponent, ComponentStartup>((_, _, _) => throw new NotSupportedException());
    }

    private void OnCompPreInit(EntityUid uid, DebugExceptionOnAddComponent component, ref ComponentPreInitEvent args)
    {
        throw new NotSupportedException();
    }
}
