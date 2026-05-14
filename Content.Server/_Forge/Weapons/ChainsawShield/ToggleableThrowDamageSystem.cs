using Content.Server.Administration.Logs;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Forge.Weapons;
using Content.Shared.Camera;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._Forge.Weapons;

public sealed class ToggleableThrowDamageSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DamageExamineSystem _damageExamine = default!;
    [Dependency] private readonly GunSystem _guns = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _cameraRecoil = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ToggleableThrowDamageComponent, ThrowDoHitEvent>(OnThrowDoHit);
        SubscribeLocalEvent<ToggleableThrowDamageComponent, DamageExamineEvent>(OnDamageExamine);
        SubscribeLocalEvent<ToggleableThrowDamageComponent, AttemptPacifiedThrowEvent>(OnAttemptPacifiedThrow);
    }

    private void OnThrowDoHit(EntityUid uid, ToggleableThrowDamageComponent component, ThrowDoHitEvent args)
    {
        if (TerminatingOrDeleted(args.Target))
            return;

        var damage = GetDamage(uid, component) * _damageable.UniversalThrownDamageModifier * GetCannonMultiplier(uid);
        var changed = _damageable.TryChangeDamage(
            args.Target,
            damage,
            component.IgnoreResistances,
            origin: args.Component.Thrower);

        if (changed != null && HasComp<MobStateComponent>(args.Target))
            _adminLogger.Add(LogType.ThrowHit,
                $"{ToPrettyString(args.Target):target} received {changed.GetTotal():damage} damage from collision");

        if (changed is { Empty: false })
            _color.RaiseEffect(Color.Red, new List<EntityUid> { args.Target }, Filter.Pvs(args.Target, entityManager: EntityManager));

        _guns.PlayImpactSound(args.Target, changed, null, false, null, null);
        if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0f)
            _cameraRecoil.KickCamera(args.Target, body.LinearVelocity.Normalized());
    }

    private void OnDamageExamine(EntityUid uid, ToggleableThrowDamageComponent component, ref DamageExamineEvent args)
    {
        var damage = GetDamage(uid, component) * _damageable.UniversalThrownDamageModifier * GetCannonMultiplier(uid);
        _damageExamine.AddDamageExamine(args.Message, _damageable.ApplyUniversalAllModifiers(damage), Loc.GetString("damage-throw"));
    }

    private void OnAttemptPacifiedThrow(Entity<ToggleableThrowDamageComponent> ent, ref AttemptPacifiedThrowEvent args)
    {
        args.Cancel("pacified-cannot-throw");
    }

    private DamageSpecifier GetDamage(EntityUid uid, ToggleableThrowDamageComponent component)
    {
        return TryComp<ItemToggleComponent>(uid, out var toggle) && toggle.Activated
            ? component.ActiveDamage
            : component.InactiveDamage;
    }

    private float GetCannonMultiplier(EntityUid uid)
    {
        return TryComp<ThrowingAmmoDamageBoostComponent>(uid, out var boost)
            ? boost.DamageMultiplier
            : 1f;
    }
}
