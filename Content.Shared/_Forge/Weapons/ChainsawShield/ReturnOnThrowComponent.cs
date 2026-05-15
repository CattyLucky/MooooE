using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ReturnOnThrowComponent : Component
{
    [DataField(required: true)]
    public string ReturnAction = default!;

    [DataField]
    public bool ForcePickup;

    [DataField]
    public SoundSpecifier? ReturnSound;

    public EntityUid? ReturnActionEntity;

    [AutoNetworkedField]
    public EntityUid? ReturnOwner;

    [AutoNetworkedField]
    public bool ReturnReady;
}

public sealed partial class ReturnOnThrowActionEvent : InstantActionEvent;
