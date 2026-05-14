using Content.Server.Damage.Components;
using Content.Shared.Damage;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._Forge.Weapons;

public sealed class ThrowingAmmoBonusDamageSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ThrowingAmmoDamageBoostComponent, ThrowDoHitEvent>(OnThrowDoHit);
    }

    private void OnThrowDoHit(EntityUid uid, ThrowingAmmoDamageBoostComponent component, ref ThrowDoHitEvent args)
    {
        if (TerminatingOrDeleted(args.Target))
            return;

        if (!TryComp<DamageOtherOnHitComponent>(uid, out var damageOnHit))
            return;

        if (component.DamageMultiplier <= 1f)
            return;

        var bonusFactor = component.DamageMultiplier - 1f;
        var bonus = damageOnHit.Damage * _damageable.UniversalThrownDamageModifier * bonusFactor;
        _damageable.TryChangeDamage(args.Target, bonus, damageOnHit.IgnoreResistances, origin: args.Component.Thrower);
    }
}
