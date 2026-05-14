using Robust.Shared.Audio;

namespace Content.Shared._Forge.Weapons;

[RegisterComponent]
public sealed partial class ToggleableEmbedSoundComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier InactiveSound = default!;

    [DataField(required: true)]
    public SoundSpecifier ActiveSound = default!;
}
