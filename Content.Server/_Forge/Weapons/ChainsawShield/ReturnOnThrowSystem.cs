using Content.Shared.Actions;
using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Server.Projectiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Server._Forge.Weapons.ChainsawShield;

public sealed class ReturnOnThrowSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly ProjectileSystem _projectiles = default!;
    [Dependency] private readonly SharedPvsOverrideSystem _pvs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ReturnOnThrowComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<ReturnOnThrowComponent, StopThrowEvent>(OnStopThrow);
        SubscribeLocalEvent<ReturnOnThrowComponent, EmbedEvent>(OnEmbedded);
        SubscribeLocalEvent<ReturnOnThrowComponent, ReturnOnThrowActionEvent>(OnReturnAction);
        SubscribeLocalEvent<ReturnOnThrowComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<ReturnOnThrowComponent, EntGotInsertedIntoContainerMessage>(OnGotInsertedIntoContainer);
        SubscribeLocalEvent<ReturnOnThrowComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnThrown(EntityUid uid, ReturnOnThrowComponent component, ref ThrownEvent args)
    {
        if (args.User == null || !_player.TryGetSessionByEntity(args.User.Value, out _))
        {
            ClearReturn(uid, component);
            return;
        }

        GrantReturnAction(uid, component, args.User.Value, enabled: false);
    }

    private void OnStopThrow(EntityUid uid, ReturnOnThrowComponent component, StopThrowEvent args)
    {
        if (component.ReturnOwner == null)
            return;

        SetReturnReady(uid, component);
    }

    private void OnEmbedded(EntityUid uid, ReturnOnThrowComponent component, ref EmbedEvent args)
    {
        if (component.ReturnOwner == null)
            return;

        SetReturnReady(uid, component);
    }

    private void OnGotEquippedHand(EntityUid uid, ReturnOnThrowComponent component, ref GotEquippedHandEvent args)
    {
        ClearReturn(uid, component);
    }

    private void OnGotInsertedIntoContainer(EntityUid uid, ReturnOnThrowComponent component, EntGotInsertedIntoContainerMessage args)
    {
        ClearReturn(uid, component);
    }

    private void OnShutdown(EntityUid uid, ReturnOnThrowComponent component, ref ComponentShutdown args)
    {
        ClearReturn(uid, component);
    }

    private void OnReturnAction(EntityUid uid, ReturnOnThrowComponent component, ref ReturnOnThrowActionEvent args)
    {
        if (args.Handled || component.ReturnOwner != args.Performer || !component.ReturnReady)
            return;

        args.Handled = TryReturn(uid, args.Performer, component);
    }

    private void GrantReturnAction(EntityUid uid, ReturnOnThrowComponent component, EntityUid owner, bool enabled)
    {
        if (component.ReturnOwner != null && component.ReturnOwner != owner)
            ClearReturn(uid, component);

        if (!_actions.AddAction(owner, ref component.ReturnActionEntity, out _, component.ReturnAction, uid))
        {
            ClearReturn(uid, component);
            return;
        }

        component.ReturnOwner = owner;
        component.ReturnReady = enabled;
        _actions.SetEnabled(component.ReturnActionEntity, enabled);
        AddPvsOverride(uid, owner);
        Dirty(uid, component);
    }

    private void SetReturnReady(EntityUid uid, ReturnOnThrowComponent component)
    {
        if (component.ReturnActionEntity == null)
        {
            ClearReturn(uid, component);
            return;
        }

        component.ReturnReady = true;
        _actions.SetEnabled(component.ReturnActionEntity, true);
        Dirty(uid, component);
    }

    private bool TryReturn(EntityUid uid, EntityUid owner, ReturnOnThrowComponent component)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(owner))
            return false;

        if (_containers.TryGetContainingContainer(uid, out _))
        {
            ClearReturn(uid, component);
            return false;
        }

        if (TryComp<EmbeddableProjectileComponent>(uid, out var embeddable) &&
            embeddable.EmbeddedIntoUid != null)
        {
            _projectiles.EmbedDetach(uid, embeddable, owner);
        }

        var pickedUp = component.ForcePickup
            ? _hands.TryForcePickupAnyHand(owner, uid, checkActionBlocker: true)
            : _hands.TryPickupAnyHand(owner, uid, checkActionBlocker: true);

        if (!pickedUp)
            return false;

        if (component.ReturnSound != null)
            _audio.PlayPvs(component.ReturnSound, uid);

        ClearReturn(uid, component);
        return true;
    }

    private void ClearReturn(EntityUid uid, ReturnOnThrowComponent component)
    {
        if (component.ReturnOwner != null)
            RemovePvsOverride(uid, component.ReturnOwner.Value);

        _actions.RemoveAction(component.ReturnActionEntity);
        component.ReturnOwner = null;
        component.ReturnReady = false;
        Dirty(uid, component);
    }

    private void AddPvsOverride(EntityUid uid, EntityUid owner)
    {
        if (_player.TryGetSessionByEntity(owner, out var session))
            _pvs.AddSessionOverride(uid, session);
    }

    private void RemovePvsOverride(EntityUid uid, EntityUid owner)
    {
        if (_player.TryGetSessionByEntity(owner, out var session))
            _pvs.RemoveSessionOverride(uid, session);
    }
}
