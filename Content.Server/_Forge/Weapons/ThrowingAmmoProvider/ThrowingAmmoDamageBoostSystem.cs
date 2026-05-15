using Content.Shared._Forge.Weapons.ThrowingAmmoProvider;
using Content.Server.Damage.Systems;

namespace Content.Server._Forge.Weapons.ThrowingAmmoProvider;

public sealed class ThrowingAmmoDamageBoostSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<ThrowingAmmoDamageBoostComponent, GetThrowDamageModifierEvent>(OnGetThrowDamageModifier);
    }

    private void OnGetThrowDamageModifier(
        EntityUid uid,
        ThrowingAmmoDamageBoostComponent component,
        ref GetThrowDamageModifierEvent args)
    {
        if (component.DamageMultiplier <= 0f)
            return;

        args.Multiplier *= component.DamageMultiplier;
    }
}
