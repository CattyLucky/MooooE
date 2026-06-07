using Content.Shared._Forge.Trade;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Stacks;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem
{
    private void CommitClaimTakeJournal(
        ClaimTakeJournal journal,
        EntityUid receiver,
        float returnFraction = 0f
    )
    {
        ReturnClaimItemsBestEffort(journal, receiver, returnFraction);

        for (var i = 0; i < journal.PendingDeletes.Count; i++)
        {
            var ent = journal.PendingDeletes[i];
            DeleteFinalEntityBestEffort(ent, "ClaimTake");
        }

        journal.Clear();
    }

    private void ReturnClaimItemsBestEffort(
        ClaimTakeJournal journal,
        EntityUid receiver,
        float returnFraction
    )
    {
        if (returnFraction <= 0f || journal.ReturnCandidates.Count == 0)
            return;

        if (!Exists(receiver) || !TryComp(receiver, out TransformComponent? receiverXform))
            return;

        var returnCount = (int) MathF.Floor(journal.ReturnCandidates.Count * Math.Clamp(returnFraction, 0f, 1f));
        if (returnCount <= 0)
            return;

        for (var i = 0; i < journal.ReturnCandidates.Count && returnCount > 0; i++)
        {
            var ent = journal.ReturnCandidates[i];
            if (!TryGetPlanningEntityPrototypeId(ent, out var prototypeId))
                continue;

            try
            {
                var returned = Spawn(prototypeId, receiverXform.Coordinates);
                CopySolutionsBestEffort(ent, returned);
                EnsureComp<NcContractTurnInBlockedComponent>(returned);
                _logic.QueuePickupToHandsOrCrateNextTick(receiver, returned);
                returnCount--;
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Claim] Failed to return consumed contract item prototype '{prototypeId}' to {ToPrettyString(receiver)}: {e}");
            }
        }
    }

    private void CopySolutionsBestEffort(EntityUid source, EntityUid target)
    {
        if (!TryComp(source, out SolutionContainerManagerComponent? sourceManager))
            return;

        foreach (var (name, sourceSolutionEnt) in _solutions.EnumerateSolutions((source, sourceManager), false))
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var sourceSolution = sourceSolutionEnt.Comp.Solution;
            if (!_solutions.EnsureSolutionEntity((target, null), name, out var targetSolutionEnt, sourceSolution.MaxVolume) ||
                targetSolutionEnt == null)
                continue;

            _solutions.RemoveAllSolution(targetSolutionEnt.Value);
            _solutions.SetCapacity(targetSolutionEnt.Value, sourceSolution.MaxVolume);
            _solutions.TryAddSolution(targetSolutionEnt.Value, new(sourceSolution));
        }
    }

    private void RollbackClaimTakeJournal(ClaimTakeJournal journal)
    {
        RestoreClaimTurnInState(journal);
        RestoreClaimRetrievalCargo(journal);
        RestoreClaimStacks(journal);
        journal.Clear();
    }

    private static void RestoreClaimTurnInState(ClaimTakeJournal journal)
    {
        if (journal.TurnInState == null)
            return;

        for (var i = journal.TurnInRestores.Count - 1; i >= 0; i--)
        {
            RestoreClaimTurnInEntry(journal.TurnInState, journal.TurnInRestores[i]);
        }
    }

    private static void RestoreClaimTurnInEntry(ObjectiveRuntimeState state, TurnInRestore restore)
    {
        if (restore.HadValue)
            state.TurnedInByTarget[restore.Key] = restore.PreviousValue;
        else
            state.TurnedInByTarget.Remove(restore.Key);
    }

    private void RestoreClaimRetrievalCargo(ClaimTakeJournal journal)
    {
        for (var i = journal.RetrievalCargoRestores.Count - 1; i >= 0; i--)
        {
            RestoreClaimRetrievalCargoEntry(journal.RetrievalCargoRestores[i]);
        }
    }

    private void RestoreClaimRetrievalCargoEntry((EntityUid Cargo, (EntityUid Store, string ContractId) Key) restore)
    {
        if (Exists(restore.Cargo))
            _objectiveRuntime.ByRetrievalCargo[restore.Cargo] = restore.Key;
    }

    private void RestoreClaimStacks(ClaimTakeJournal journal)
    {
        for (var i = journal.StackRestores.Count - 1; i >= 0; i--)
        {
            RestoreClaimStackEntry(journal.StackRestores[i]);
        }
    }

    private void RestoreClaimStackEntry((EntityUid Ent, int PreviousCount) restore)
    {
        if (TryComp(restore.Ent, out StackComponent? stack))
            _stacks.SetCount(restore.Ent, restore.PreviousCount, stack);
    }

    private sealed class ClaimTakeJournal
    {
        public readonly List<EntityUid> PendingDeletes = new();
        public readonly List<EntityUid> ReturnCandidates = new();

        public readonly List<(EntityUid Cargo, (EntityUid Store, string ContractId) Key)>
            RetrievalCargoRestores = new();

        public readonly List<(EntityUid Ent, int PreviousCount)> StackRestores = new();
        public readonly List<TurnInRestore> TurnInRestores = new();
        public ObjectiveRuntimeState? TurnInState;

        public void TrackStack(EntityUid ent, int previousCount)
        {
            for (var i = 0; i < StackRestores.Count; i++)
            {
                if (StackRestores[i].Ent == ent)
                    return;
            }

            StackRestores.Add((ent, previousCount));
        }

        public void TrackRetrievalCargo(EntityUid cargo, (EntityUid Store, string ContractId) key)
        {
            for (var i = 0; i < RetrievalCargoRestores.Count; i++)
            {
                if (RetrievalCargoRestores[i].Cargo == cargo)
                    return;
            }

            RetrievalCargoRestores.Add((cargo, key));
        }

        public void TrackTurnIn(
            ObjectiveRuntimeState state,
            (string TargetItem, PrototypeMatchMode MatchMode) key
        )
        {
            TurnInState ??= state;

            for (var i = 0; i < TurnInRestores.Count; i++)
            {
                if (TurnInRestores[i].Key == key)
                    return;
            }

            var hadValue = state.TurnedInByTarget.TryGetValue(key, out var previousValue);
            TurnInRestores.Add(new TurnInRestore(key, hadValue, previousValue));
        }

        public void Clear()
        {
            PendingDeletes.Clear();
            ReturnCandidates.Clear();
            RetrievalCargoRestores.Clear();
            StackRestores.Clear();
            TurnInRestores.Clear();
            TurnInState = null;
        }
    }

    private readonly record struct TurnInRestore(
        (string TargetItem, PrototypeMatchMode MatchMode) Key,
        bool HadValue,
        int PreviousValue);
}
