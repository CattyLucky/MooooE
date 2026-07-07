using Content.Server.Afk;
using Content.Server._NF.Bank;
using Content.Server._NF.CryoSleep;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Server.Popups;
using Content.Shared.SSDIndicator;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Server.Player;
using Content.Shared.Roles;
using Robust.Shared.Timing;

namespace Content.Server._Forge.AutoSalarySystem;

public sealed class AutoSalarySystem : EntitySystem
{
    private static readonly TimeSpan FailedPaymentRetryDelay = TimeSpan.FromMinutes(1);

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IAfkManager _afkManager = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    public override void Update(float frameTime)
    {
        CleanupSalariesWithoutBankAccounts();

        var query = EntityQueryEnumerator<BankAccountComponent>();
        while (query.MoveNext(out var uid, out var bank))
        {
            if (!TryGetCurrentJob(uid, out var job)
                || job.Salary <= 0)
            {
                if (HasComp<AutoSalaryComponent>(uid))
                    RemCompDeferred<AutoSalaryComponent>(uid);

                continue;
            }

            if (!TryComp<AutoSalaryComponent>(uid, out var comp))
            {
                comp = EnsureComp<AutoSalaryComponent>(uid);
                comp.LastSalaryAt = _timing.CurTime;
                comp.NextRetryAt = TimeSpan.Zero;
                comp.JobPrototype = job.ID;
                Dirty(uid, comp);
                continue;
            }

            if (comp.JobPrototype != job.ID)
            {
                comp.LastSalaryAt = _timing.CurTime;
                comp.NextRetryAt = TimeSpan.Zero;
                comp.JobPrototype = job.ID;
                Dirty(uid, comp);
                continue;
            }

            if (comp.LastSalaryAt + job.SalaryInterval > _timing.CurTime)
                continue;

            if (comp.NextRetryAt > _timing.CurTime)
                continue;

            if (ShouldSkipEntity(uid))
            {
                MarkSalaryHandled(uid, comp);
                continue;
            }

            if (!TryPaySalary(uid, bank, job.Salary))
            {
                comp.NextRetryAt = _timing.CurTime + FailedPaymentRetryDelay;
                Dirty(uid, comp);
                continue;
            }

            MarkSalaryHandled(uid, comp);
        }
    }

    private void CleanupSalariesWithoutBankAccounts()
    {
        var query = EntityQueryEnumerator<AutoSalaryComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!HasComp<BankAccountComponent>(uid))
                RemCompDeferred<AutoSalaryComponent>(uid);
        }
    }

    private bool HasActivePlayer(EntityUid body)
    {
        if (!_mindSystem.TryGetMind(body, out _, out var mind))
            return false;
        if (!_playerManager.TryGetSessionByEntity(body, out var session))
            return false;
        if (session.Status != SessionStatus.InGame)
            return false;
        if (_afkManager.IsAfk(session))
            return false;
        if (mind.IsVisitingEntity)
            return false;
        if (TryComp<SSDIndicatorComponent>(body, out var ssd) && ssd.IsSSD)
            return false;
        return true;
    }

    private bool ShouldSkipEntity(EntityUid body)
    {
        if (IsEntityDead(body))
            return true;
        if (!HasActivePlayer(body))
            return true;
        return false;
    }

    private bool IsEntityDead(EntityUid body)
    {
        return !TryComp<MobStateComponent>(body, out var mobState) || _mobState.IsDead(body, mobState);
    }

    private bool TryPaySalary(EntityUid body, BankAccountComponent bank, int salary)
    {
        if (!_bank.TryBankDeposit(body, salary))
            return false;

        var message = Loc.GetString("auto-salary-popup",
            ("salary", salary),
            ("balance", bank.Balance));

        _popup.PopupEntity(message, body, body);

        if (_playerManager.TryGetSessionByEntity(body, out var session))
            _chat.DispatchServerMessage(session, message, suppressLog: true);

        return true;
    }

    private void MarkSalaryHandled(EntityUid body, AutoSalaryComponent comp)
    {
        comp.LastSalaryAt = _timing.CurTime;
        comp.NextRetryAt = TimeSpan.Zero;
        Dirty(body, comp);
    }

    private bool TryGetCurrentJob(EntityUid body, out JobPrototype job)
    {
        job = default!;
        ProtoId<JobPrototype>? jobId = null;

        if (TryComp<PlayerJobComponent>(body, out var playerJob)
            && !string.IsNullOrWhiteSpace(playerJob.JobPrototype))
        {
            jobId = playerJob.JobPrototype;
        }
        else if (_mindSystem.TryGetMind(body, out var mindId, out _)
            && _jobs.MindTryGetJobId(mindId, out var currentMindJobId)
            && !string.IsNullOrWhiteSpace(currentMindJobId))
        {
            jobId = currentMindJobId;
        }

        if (jobId is not { } resolvedJobId
            || !_proto.TryIndex(resolvedJobId, out JobPrototype? resolvedJob))
        {
            return false;
        }

        job = resolvedJob;
        return true;
    }
}
