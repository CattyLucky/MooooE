using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Forge.Weapons;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Weapons;

public sealed class ToggleableEmbedEffectSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ToggleableEmbedEffectComponent, EmbedEvent>(OnEmbed);
    }

    private void OnEmbed(EntityUid uid, ToggleableEmbedEffectComponent component, ref EmbedEvent args)
    {
        component.NextUpdate = _timing.CurTime + GetUpdateInterval(component);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ToggleableEmbedEffectComponent, EmbeddableProjectileComponent>();
        while (query.MoveNext(out var uid, out var component, out var embeddable))
        {
            if (curTime < component.NextUpdate)
                continue;

            var interval = GetUpdateInterval(component);
            component.NextUpdate = curTime + interval;

            if (embeddable.EmbeddedIntoUid is not { } target ||
                TerminatingOrDeleted(target) ||
                !IsActive(uid))
            {
                continue;
            }

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

    private static TimeSpan GetUpdateInterval(ToggleableEmbedEffectComponent component)
    {
        return TimeSpan.FromSeconds(MathF.Max(component.UpdateInterval, 0.1f));
    }
}
