using Content.Shared.Actions;
using Robust.Shared.Audio;

namespace Content.Shared._Forge.Weapons;

[RegisterComponent]
public sealed partial class ReturnOnThrowComponent : Component
{
    [DataField]
    public string ReturnAction = "ActionReturnOnThrow";

    [DataField]
    public bool ForcePickup;

    [DataField]
    public SoundSpecifier? ReturnSound;

    public EntityUid? ReturnActionEntity;

    public EntityUid? ReturnOwner;

    public bool ReturnReady;
}

[RegisterComponent]
public sealed partial class ReturnOnThrowActionComponent : Component
{
    public EntityUid? Target;
}

public sealed partial class ReturnOnThrowActionEvent : InstantActionEvent;
