using Content.Shared._Forge.Weapons;
using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Server.Projectiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Server._Forge.Weapons;

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

        SubscribeLocalEvent<ReturnOnThrowActionComponent, ReturnOnThrowActionEvent>(OnReturnActionFromAction);
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

        SetReturnReady(component);
    }

    private void OnEmbedded(EntityUid uid, ReturnOnThrowComponent component, ref EmbedEvent args)
    {
        if (component.ReturnOwner == null)
            return;

        SetReturnReady(component);
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

    private void OnReturnActionFromAction(EntityUid uid, ReturnOnThrowActionComponent component, ref ReturnOnThrowActionEvent args)
    {
        if (args.Handled || component.Target == null)
            return;

        if (!TryComp<ReturnOnThrowComponent>(component.Target, out var returnComponent))
        {
            _actions.RemoveAction(uid);
            return;
        }

        if (returnComponent.ReturnOwner != args.Performer || !returnComponent.ReturnReady)
            return;

        args.Handled = TryReturn(component.Target.Value, args.Performer, returnComponent);
    }

    private void GrantReturnAction(EntityUid uid, ReturnOnThrowComponent component, EntityUid owner, bool enabled)
    {
        if (component.ReturnOwner != null && component.ReturnOwner != owner)
            ClearReturn(uid, component);

        component.ReturnOwner = owner;
        component.ReturnReady = enabled;

        if (!_actions.AddAction(owner, ref component.ReturnActionEntity, out _, component.ReturnAction, uid))
            return;

        var actionComponent = EnsureComp<ReturnOnThrowActionComponent>(component.ReturnActionEntity.Value);
        actionComponent.Target = uid;

        _actions.SetEnabled(component.ReturnActionEntity, enabled);
        AddPvsOverride(uid, owner);
    }

    private void SetReturnReady(ReturnOnThrowComponent component)
    {
        component.ReturnReady = true;
        _actions.SetEnabled(component.ReturnActionEntity, true);
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

        if (component.ReturnActionEntity != null &&
            TryComp<ReturnOnThrowActionComponent>(component.ReturnActionEntity, out var actionComponent))
        {
            actionComponent.Target = null;
        }
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
