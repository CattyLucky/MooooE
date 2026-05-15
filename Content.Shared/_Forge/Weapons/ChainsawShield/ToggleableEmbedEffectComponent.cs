using Content.Shared.Damage;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent]
public sealed partial class ToggleableEmbedEffectComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier ActiveDamage = default!;

    [DataField]
    public float ActiveBleedAmount;

    [DataField]
    public float UpdateInterval = 1f;

    [DataField]
    public bool IgnoreResistances;

    public TimeSpan NextUpdate;
}
