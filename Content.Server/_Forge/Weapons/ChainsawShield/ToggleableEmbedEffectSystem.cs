using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Forge.Weapons;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Projectiles;

namespace Content.Server._Forge.Weapons;

public sealed class ToggleableEmbedEffectSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ToggleableEmbedEffectComponent, EmbedEvent>(OnEmbed);
    }

    private void OnEmbed(EntityUid uid, ToggleableEmbedEffectComponent component, ref EmbedEvent args)
    {
        component.Accumulator = 0f;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ToggleableEmbedEffectComponent, EmbeddableProjectileComponent>();
        while (query.MoveNext(out var uid, out var component, out var embeddable))
        {
            if (embeddable.EmbeddedIntoUid is not { } target ||
                TerminatingOrDeleted(target) ||
                !IsActive(uid))
            {
                component.Accumulator = 0f;
                continue;
            }

            component.Accumulator += frameTime;
            var interval = MathF.Max(component.UpdateInterval, 0.1f);
            if (component.Accumulator < interval)
                continue;

            component.Accumulator -= interval;
            ApplyEffect(uid, target, component);
        }
    }

    private void ApplyEffect(EntityUid uid, EntityUid target, ToggleableEmbedEffectComponent component)
    {
        if (!component.ActiveDamage.Empty)
            _damageable.TryChangeDamage(target, component.ActiveDamage, component.IgnoreResistances, origin: uid);

        if (component.ActiveBleedAmount <= 0f ||
            !TryComp<BloodstreamComponent>(target, out var bloodstream))
        {
            return;
        }

        _bloodstream.TryModifyBleedAmount(target, component.ActiveBleedAmount, bloodstream);
    }

    private bool IsActive(EntityUid uid)
    {
        return TryComp<ItemToggleComponent>(uid, out var toggle) && toggle.Activated;
    }
}
