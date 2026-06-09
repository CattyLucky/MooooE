using Content.Shared._Forge.Trade;
using Content.Shared.Destructible;
using Robust.Shared.Map;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void OnContractDroneCoreDestroyed(
        EntityUid uid,
        NcContractDroneCoreComponent component,
        DestructionEventArgs args
    )
    {
        var key = component.Store != EntityUid.Invalid && !string.IsNullOrWhiteSpace(component.ContractId)
            ? (component.Store, component.ContractId)
            : _objectiveRuntime.ByDroneCore.TryGetValue(uid, out var indexedKey)
                ? indexedKey
                : default;

        if (key.Store == EntityUid.Invalid || string.IsNullOrWhiteSpace(key.ContractId))
            return;

        HandleContractDroneCoreDestroyed(key, uid);
    }

    private void HandleContractDroneCoreDestroyed(
        (EntityUid Store, string ContractId) key,
        EntityUid core
    )
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            _objectiveRuntime.ByDroneCore.Remove(core);
            return;
        }

        RemoveDroneHuntCoreTarget(state, core);

        if (TryComp(core, out TransformComponent? coreXform))
            state.LastKnownTargetCoordinates = coreXform.Coordinates;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken || contract.Runtime.Failed || contract.Completed)
            return;

        var previousRequired = contract.Required;
        var previousProgress = contract.Progress;
        var previousStatus = contract.FlowStatus;

        SetObjectiveStage(contract, 1);

        var completionCoords = ResolveDroneHuntCompletionCoordinates(key.Store, state);
        if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, completionCoords))
            return;

        FinalizeObjectiveCompletion(key, contract);
        RaiseContractsChangedIfSnapshotChanged(key, contract, previousRequired, previousProgress, previousStatus);
    }

    private void OnContractDroneCoreLost(
        (EntityUid Store, string ContractId) key,
        EntityUid core
    )
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        RemoveDroneHuntCoreTarget(state, core);

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken ||
            contract.Runtime.Failed ||
            contract.Completed ||
            contract.ExecutionKind != ContractExecutionKind.DroneHuntObjective)
            return;

        if (HasLiveDroneHuntCoreTarget(state))
            return;

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-drone-hunt-target-lost"),
            deleteGuards: false);
    }

    private void SyncDroneHuntObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            SyncObjectiveProgressFromRuntime(contract);
            ResetContractTargetProgress(contract);
            SyncContractFlowStatus(contract);
            return;
        }

        if (contract.Completed)
        {
            SyncObjectiveProgressFromRuntime(contract);
            ResetContractTargetProgress(contract);
            SyncContractFlowStatus(contract);
            return;
        }

        PruneLostDroneHuntCoreTargets(state);
        if (contract.Taken &&
            !contract.Runtime.Failed &&
            state.DroneHuntActive &&
            !HasLiveDroneHuntCoreTarget(state))
        {
            if (TryGetObjectiveContract(key, out var comp, out var liveContract))
            {
                FinalizeObjectiveTerminalOutcome(
                    key,
                    comp,
                    liveContract,
                    Loc.GetString("nc-store-contract-drone-hunt-target-lost"),
                    deleteGuards: false);
                return;
            }
        }

        SyncObjectiveProgressFromRuntime(contract);
        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);
    }

    private void PruneLostDroneHuntCoreTargets(ObjectiveRuntimeState state)
    {
        for (var i = state.DroneHuntCoreTargets.Count - 1; i >= 0; i--)
        {
            var core = state.DroneHuntCoreTargets[i];
            if (core != EntityUid.Invalid && !TerminatingOrDeleted(core))
                continue;

            RemoveDroneHuntCoreTargetAt(state, i);
        }
    }

    private bool HasLiveDroneHuntCoreTarget(ObjectiveRuntimeState state)
    {
        for (var i = 0; i < state.DroneHuntCoreTargets.Count; i++)
        {
            var core = state.DroneHuntCoreTargets[i];
            if (core != EntityUid.Invalid && !TerminatingOrDeleted(core))
                return true;
        }

        return false;
    }

    private EntityCoordinates ResolveDroneHuntCompletionCoordinates(EntityUid store, ObjectiveRuntimeState state)
    {
        if (state.LastKnownTargetCoordinates is { } targetCoords && targetCoords != EntityCoordinates.Invalid)
            return targetCoords;

        if (TryComp(store, out TransformComponent? storeXform))
            return storeXform.Coordinates;

        return EntityCoordinates.Invalid;
    }

    private bool TryResolveDroneHuntPinpointerTarget(
        EntityUid store,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;

        EntityUid best = EntityUid.Invalid;
        var bestDistance = float.MaxValue;
        var hasStorePosition = false;
        var storeCoords = MapCoordinates.Nullspace;
        if (TryComp(store, out TransformComponent? storeXform))
        {
            hasStorePosition = true;
            storeCoords = _xform.ToMapCoordinates(storeXform.Coordinates);
        }

        for (var i = state.DroneHuntCoreTargets.Count - 1; i >= 0; i--)
        {
            var core = state.DroneHuntCoreTargets[i];
            if (core == EntityUid.Invalid || TerminatingOrDeleted(core))
            {
                RemoveDroneHuntCoreTargetAt(state, i);
                continue;
            }

            if (!hasStorePosition)
            {
                best = core;
                break;
            }

            if (!TryComp(core, out TransformComponent? coreXform))
            {
                best = core;
                break;
            }

            var coreCoords = _xform.ToMapCoordinates(coreXform.Coordinates);
            if (coreCoords.MapId != storeCoords.MapId)
            {
                best = core;
                break;
            }

            var distance = (coreCoords.Position - storeCoords.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = core;
        }

        if (best == EntityUid.Invalid)
            return false;

        target = best;
        return true;
    }

    private void RemoveDroneHuntCoreTarget(ObjectiveRuntimeState state, EntityUid core)
    {
        state.DroneHuntCoreTargets.Remove(core);
        _objectiveRuntime.ByDroneCore.Remove(core);
    }

    private void RemoveDroneHuntCoreTargetAt(ObjectiveRuntimeState state, int index)
    {
        var core = state.DroneHuntCoreTargets[index];
        state.DroneHuntCoreTargets.RemoveAt(index);
        _objectiveRuntime.ByDroneCore.Remove(core);
    }
}
