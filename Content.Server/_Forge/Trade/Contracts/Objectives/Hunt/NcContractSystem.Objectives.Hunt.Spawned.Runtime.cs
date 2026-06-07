using System.Numerics;
using Content.Shared.Procedural;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems;
using Content.Shared._Forge.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private List<Entity<MapGridComponent>> _huntDebrisPlacementGridScratch = new();

    private bool TrySpawnHuntTargets(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        List<EntityCoordinates>? siteSpawnCoordinates = null;

        if (contract.Config.HuntDungeons.Count > 0)
            return TryStartHuntDungeonGeneration(store, contractId, contract, state);

        if (contract.Config.HuntDebris.Count > 0)
        {
            if (!TrySpawnHuntDebris(store, contractId, contract, state, out siteSpawnCoordinates))
                return false;
        }

        return TrySpawnHuntTargetEntities(store, contractId, contract, state, siteSpawnCoordinates);
    }

    private bool TrySpawnHuntTargetEntities(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        List<EntityCoordinates>? siteSpawnCoordinates
    )
    {
        var targets = GetEffectiveTargets(contract);
        var required = Math.Max(1, CalculateTotalRequired(targets));

        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var targetDef = targets[targetIndex];
            var targetRequired = Math.Max(0, targetDef.Required);
            if (targetRequired <= 0)
                continue;

            for (var i = 0; i < targetRequired; i++)
            {
                if (!TryResolveSpawnedHuntPrototype(contractId, targetDef, out var targetProtoId))
                    return false;

                if (!TryResolveHuntTargetSpawnCoordinates(
                        store,
                        contractId,
                        contract,
                        siteSpawnCoordinates,
                        out var spawnCoords))
                {
                    return false;
                }

                if (!TrySpawnObjectiveTarget(contractId, targetProtoId, spawnCoords, out var target))
                    return false;

                state.HuntSpawnedTargets.Add(target);
                if (targetDef.BodyRequired)
                    state.HuntBodyEntity = target;

                if (state.LastKnownTargetCoordinates == null && TryComp(target, out TransformComponent? targetXform))
                    state.LastKnownTargetCoordinates = targetXform.Coordinates;
            }
        }

        return state.HuntSpawnedTargets.Count == required;
    }

    private bool TryStartHuntDungeonGeneration(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var anchorCoords, true) &&
            !TryGetHuntStoreFallbackCoordinates(store, out anchorCoords))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve dungeon spawn point.");
            return false;
        }

        if (!TryPickHuntDungeonPrototype(contractId, contract.Config.HuntDungeons, out var dungeonPrototype))
            return false;

        var dungeonConfig = _prototypes.Index<DungeonConfigPrototype>(dungeonPrototype);
        var attempts = Math.Max(1, contract.Config.HuntDebrisPlacementAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!TryGetHuntDebrisSpawnMapCoordinates(store, contract.Config, anchorCoords, out var spawnMapCoords))
                continue;

            Entity<MapGridComponent> grid;
            try
            {
                grid = _mapManager.CreateGridEntity(spawnMapCoords.MapId);
                _xform.SetMapCoordinates(grid, spawnMapCoords);
            }
            catch (Exception e)
            {
                Sawmill.Error(
                    $"[Contracts] Hunt runtime init failed for '{contractId}': cannot create dungeon grid: {e}");
                return false;
            }

            try
            {
                state.HuntDebrisEntity = grid.Owner;
                state.HuntDungeonGenerationTask = _dungeon.GenerateDungeonAsync(
                    dungeonConfig,
                    dungeonConfig.ID,
                    grid.Owner,
                    grid.Comp,
                    Vector2i.Zero,
                    _random.Next());
                return true;
            }
            catch (Exception e)
            {
                Sawmill.Error(
                    $"[Contracts] Hunt runtime init failed for '{contractId}': dungeon generation '{dungeonPrototype}' threw: {e}");

                state.HuntDebrisEntity = null;
                if (!TerminatingOrDeleted(grid.Owner))
                    Del(grid.Owner);
                return false;
            }
        }

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': cannot find a free dungeon placement after {attempts} attempts.");
        return false;
    }

    private bool TryResolveHuntTargetSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        List<EntityCoordinates>? debrisSpawnCoordinates,
        out EntityCoordinates spawnCoords
    )
    {
        spawnCoords = EntityCoordinates.Invalid;

        if (debrisSpawnCoordinates is { Count: > 0 })
        {
            var index = _random.Next(debrisSpawnCoordinates.Count);
            spawnCoords = debrisSpawnCoordinates[index];

            if (debrisSpawnCoordinates.Count > 1)
                debrisSpawnCoordinates.RemoveAt(index);

            return true;
        }

        if (TryResolveObjectiveSpawnCoordinates(store, contract.Config, out spawnCoords, false))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve hunt spawn point.");
        return false;
    }

    private bool TrySpawnHuntDebris(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out List<EntityCoordinates> spawnCoordinates
    )
    {
        spawnCoordinates = new List<EntityCoordinates>();

        if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var debrisCoords, true) &&
            !TryGetHuntStoreFallbackCoordinates(store, out debrisCoords))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve debris spawn point.");
            return false;
        }

        if (!TryPickHuntDebrisPrototype(contractId, contract.Config.HuntDebris, out var debrisPrototype))
            return false;

        var attempts = Math.Max(1, contract.Config.HuntDebrisPlacementAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!TryGetHuntDebrisSpawnMapCoordinates(store, contract.Config, debrisCoords, out var spawnMapCoords))
                continue;

            EntityUid debris;
            try
            {
                debris = Spawn(debrisPrototype, spawnMapCoords);
            }
            catch (Exception e)
            {
                Sawmill.Error(
                    $"[Contracts] Hunt runtime init failed for '{contractId}': debris spawn '{debrisPrototype}' threw: {e}");
                return false;
            }

            state.HuntDebrisEntity = debris;
            ForceLoadHuntDebris(debris);

            if (!TryComp(debris, out MapGridComponent? grid))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt runtime init failed for '{contractId}': debris '{debrisPrototype}' is not a map grid.");
                Del(debris);
                state.HuntDebrisEntity = null;
                return false;
            }

            if (!IsHuntDebrisGridPlacementClear(debris, grid, contract.Config.HuntDebrisSafetyRadius))
            {
                Del(debris);
                state.HuntDebrisEntity = null;
                continue;
            }

            CollectHuntDebrisSpawnCoordinates(debris, grid, spawnCoordinates);
            if (spawnCoordinates.Count > 0)
                return true;

            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': debris '{debrisPrototype}' has no valid spawn tiles.");
            Del(debris);
            state.HuntDebrisEntity = null;
        }

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': cannot find a free debris placement after {attempts} attempts.");
        return false;
    }

    private bool TryGetHuntStoreFallbackCoordinates(EntityUid store, out EntityCoordinates coordinates)
    {
        if (TryComp(store, out TransformComponent? storeXform))
        {
            coordinates = storeXform.Coordinates;
            return coordinates != EntityCoordinates.Invalid;
        }

        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryGetHuntDebrisSpawnMapCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        EntityCoordinates debrisCoords,
        out MapCoordinates spawnCoords
    )
    {
        spawnCoords = MapCoordinates.Nullspace;
        var anchorCoords = _xform.ToMapCoordinates(debrisCoords);
        if (anchorCoords.MapId == MapId.Nullspace)
            return false;

        var angle = _random.NextAngle();
        var direction = angle.ToVec();
        var minDistance = Math.Max(0f, config.HuntDebrisMinDistance);
        var maxDistance = Math.Max(minDistance, config.HuntDebrisMaxDistance);
        var distance = MathHelper.CloseTo(minDistance, maxDistance)
            ? minDistance
            : _random.NextFloat(minDistance, maxDistance);
        var lateral = (angle + Math.PI / 2).ToVec() *
                      _random.NextFloat(-config.HuntDebrisSafetyRadius, config.HuntDebrisSafetyRadius);

        var origin = GetHuntDebrisSpawnOrigin(store, anchorCoords, direction);
        var candidate = new MapCoordinates(origin + direction * distance + lateral, anchorCoords.MapId);
        if (!IsHuntDebrisAreaClear(candidate, config.HuntDebrisSafetyRadius))
            return false;

        spawnCoords = candidate;
        return true;
    }

    private Vector2 GetHuntDebrisSpawnOrigin(EntityUid store, MapCoordinates anchorCoords, Vector2 direction)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            storeXform.GridUid is not { } gridUid ||
            !TryComp(gridUid, out MapGridComponent? grid))
        {
            return anchorCoords.Position;
        }

        var gridXform = Transform(gridUid);
        if (gridXform.MapID != anchorCoords.MapId)
            return anchorCoords.Position;

        var bounds = _xform.GetWorldMatrix(gridXform).TransformBox(grid.LocalAABB);
        var probe = anchorCoords.Position + direction * MathF.Max(bounds.Width, bounds.Height);
        return bounds.ClosestPoint(probe);
    }

    private bool IsHuntDebrisAreaClear(MapCoordinates coords, float safetyRadius)
    {
        var diameter = Math.Max(1f, safetyRadius * 2f);
        var bounds = Box2.CenteredAround(coords.Position, new Vector2(diameter, diameter));

        _huntDebrisPlacementGridScratch.Clear();
        _mapManager.FindGridsIntersecting(
            coords.MapId,
            bounds,
            ref _huntDebrisPlacementGridScratch,
            includeMap: false);

        return _huntDebrisPlacementGridScratch.Count == 0;
    }

    private bool IsHuntDebrisGridPlacementClear(EntityUid debris, MapGridComponent grid, float safetyRadius)
    {
        var xform = Transform(debris);
        if (xform.MapUid == null)
            return false;

        var bounds = _xform.GetWorldMatrix(xform)
            .TransformBox(grid.LocalAABB)
            .Enlarged(Math.Max(0f, safetyRadius));

        _huntDebrisPlacementGridScratch.Clear();
        _mapManager.FindGridsIntersecting(
            xform.MapID,
            bounds,
            ref _huntDebrisPlacementGridScratch,
            includeMap: false);

        for (var i = 0; i < _huntDebrisPlacementGridScratch.Count; i++)
        {
            if (_huntDebrisPlacementGridScratch[i].Owner != debris)
                return false;
        }

        return true;
    }

    private bool TryPickHuntDebrisPrototype(
        string contractId,
        IReadOnlyList<NcHuntDebrisEntry> debris,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (debris.Count == 0)
            return false;

        var picked = PickWeighted(_random, debris, static entry => entry.Weight);
        prototypeId = picked.Prototype;

        if (_prototypes.HasIndex<EntityPrototype>(prototypeId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': debris prototype '{prototypeId}' is missing.");
        return false;
    }

    private bool TryPickHuntDungeonPrototype(
        string contractId,
        IReadOnlyList<NcHuntDungeonEntry> dungeons,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (dungeons.Count == 0)
            return false;

        var picked = PickWeighted(_random, dungeons, static entry => entry.Weight);
        prototypeId = picked.Prototype;

        if (_prototypes.HasIndex<DungeonConfigPrototype>(prototypeId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': dungeonConfig '{prototypeId}' is missing.");
        return false;
    }

    private void UpdatePendingHuntDungeons()
    {
        if (_objectiveRuntime.ActiveHuntObjectives.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveHuntObjectives)
        {
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
                state.HuntDungeonGenerationTask is not { } task ||
                !task.IsCompleted)
                continue;

            _objectiveRuntime.KeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
            FinishPendingHuntDungeon(_objectiveRuntime.KeysScratch[i]);

        _objectiveRuntime.KeysScratch.Clear();
    }

    private void FinishPendingHuntDungeon((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.HuntDungeonGenerationTask is not { } task ||
            !task.IsCompleted)
            return;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }

        state.HuntDungeonGenerationTask = null;

        if (task.IsCanceled || task.IsFaulted)
        {
            var reason = task.Exception?.GetBaseException().Message ?? "generation task failed";
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': {reason}");
            return;
        }

        var dungeons = task.GetAwaiter().GetResult();
        if (dungeons.Count == 0 || !HasGeneratedHuntDungeonRooms(dungeons))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated no rooms.");
            return;
        }

        if (state.HuntDebrisEntity is not { } site ||
            site == EntityUid.Invalid ||
            TerminatingOrDeleted(site) ||
            !TryComp(site, out MapGridComponent? grid))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated grid is missing.");
            return;
        }

        if (!IsHuntDebrisGridPlacementClear(site, grid, contract.Config.HuntDebrisSafetyRadius))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated grid intersects another grid.");
            return;
        }

        var spawnCoordinates = new List<EntityCoordinates>();
        CollectHuntDebrisSpawnCoordinates(site, grid, spawnCoordinates);
        if (spawnCoordinates.Count == 0)
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated grid has no valid spawn tiles.");
            return;
        }

        if (!TrySpawnHuntTargetEntities(key.Store, key.ContractId, contract, state, spawnCoordinates))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': cannot spawn hunt targets.");
            return;
        }

        if (contract.Config.GivePinpointer &&
            state.HuntPendingPinpointerUser is { } user &&
            user != EntityUid.Invalid &&
            !TerminatingOrDeleted(user) &&
            !TryIssueSpawnedHuntPinpointer(key.Store, user, key.ContractId, contract, state))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init for '{key.ContractId}' generated targets but failed to issue initial pinpointer.");
        }

        state.HuntPendingPinpointerUser = null;
        UpdateObjectiveContractProgress(key.Store, key.ContractId, contract);
    }

    private static bool HasGeneratedHuntDungeonRooms(IReadOnlyList<Dungeon> dungeons)
    {
        for (var i = 0; i < dungeons.Count; i++)
        {
            if (dungeons[i].Rooms.Count > 0)
                return true;
        }

        return false;
    }

    private void ForceLoadHuntDebris(EntityUid debris)
    {
        if (!HasComp<LocalityLoaderComponent>(debris))
            return;

        RaiseLocalEvent(debris, new LocalStructureLoadedEvent());
        RemCompDeferred<LocalityLoaderComponent>(debris);
    }

    private void CollectHuntDebrisSpawnCoordinates(
        EntityUid debris,
        MapGridComponent grid,
        List<EntityCoordinates> output
    )
    {
        output.Clear();

        var enumerator = _map.GetAllTilesEnumerator(debris, grid, true);
        while (enumerator.MoveNext(out var tile))
        {
            var tileRef = tile.Value;
            if (tileRef.Tile.IsEmpty ||
                _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
                continue;

            output.Add(_map.GridTileToLocal(debris, grid, tileRef.GridIndices));
        }
    }

    private bool TryAdvanceSpawnedHuntTargetProgress(
        EntityUid killedTarget,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (!TryGetPlanningEntityPrototypeId(killedTarget, out var prototypeId))
            return false;

        var targets = GetEffectiveTargets(contract);
        if (state.HuntBodyEntity == killedTarget)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!target.BodyRequired || target.Progress >= target.Required)
                    continue;

                if (!MatchesSpawnedHuntTargetEntry(prototypeId, target))
                    continue;

                target.Progress = Math.Min(target.Required, target.Progress + 1);
                targets[i] = target;
                return true;
            }
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.Progress >= target.Required)
                continue;

            if (!MatchesSpawnedHuntTargetEntry(prototypeId, target))
                continue;

            target.Progress = Math.Min(target.Required, target.Progress + 1);
            targets[i] = target;
            return true;
        }

        return false;
    }

    private static int CalculateSpawnedHuntTotalProgress(ContractServerData contract)
    {
        var progress = 0;
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            progress = SaturatingAdd(progress, Math.Max(0, targets[i].Progress));
        }

        return progress;
    }

    private bool TryGetHuntBodyEntity(ObjectiveRuntimeState state, out EntityUid body)
    {
        body = EntityUid.Invalid;
        if (state.HuntBodyEntity is not { } candidate ||
            candidate == EntityUid.Invalid ||
            TerminatingOrDeleted(candidate))
            return false;

        if (!TryComp(candidate, out MobStateComponent? mobState) ||
            mobState.CurrentState != MobState.Dead)
            return false;

        body = candidate;
        return true;
    }

    private bool TryConsumeSpawnedHuntBodyTurnIn(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveConsumeJournal journal,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!RequiresSpawnedHuntBodyTurnIn(contract))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetHuntBodyEntity(state, out var body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' requires the marked corpse to be brought back to the store.");
            return false;
        }

        if (!IsSpawnedHuntBodyInTurnInScope(store, user, body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' body is not being dragged by the claimant and is not near the store.");
            return false;
        }

        journal.TrackHuntBody(state, body);
        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);
        journal.PendingDeletes.Add(body);

        return true;
    }

    private bool IsSpawnedHuntBodyInTurnInScope(EntityUid store, EntityUid user, EntityUid body)
    {
        if (IsSpawnedHuntBodyCarriedByUser(body, user))
            return true;

        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(body, out TransformComponent? bodyXform) ||
            IsTargetInEntityContainer(bodyXform))
            return false;

        var storeMap = _xform.ToMapCoordinates(storeXform.Coordinates);
        var bodyMap = _xform.ToMapCoordinates(bodyXform.Coordinates);
        if (storeMap.MapId != bodyMap.MapId)
            return false;

        var delta = _xform.GetWorldPosition(storeXform) - _xform.GetWorldPosition(bodyXform);
        return delta.LengthSquared() <=
               NcContractTuning.TrackedDeliveryStoreRange * NcContractTuning.TrackedDeliveryStoreRange;
    }

    private bool IsSpawnedHuntBodyCarriedByUser(EntityUid body, EntityUid user)
    {
        if (TryComp(body, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(body, out var root) && root == user;
    }

    private bool TryResolveSpawnedHuntPrototype(
        string contractId,
        ContractTargetServerData target,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (target.MatchMode == PrototypeMatchMode.Exact)
        {
            prototypeId = target.TargetItem;
            return _prototypes.HasIndex<EntityPrototype>(prototypeId);
        }

        if (string.IsNullOrWhiteSpace(target.TargetItem) ||
            !_prototypes.TryIndex<NcHuntGroupPrototype>(target.TargetItem, out var group) ||
            group.Prototypes.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': target group has no spawnable prototypes.");
            return false;
        }

        var candidates = new List<string>(group.Prototypes.Count);
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var candidate = group.Prototypes[i];
            if (!string.IsNullOrWhiteSpace(candidate) && _prototypes.HasIndex<EntityPrototype>(candidate))
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': target group '{group.ID}' has no valid entity prototypes.");
            return false;
        }

        prototypeId = _random.Pick(candidates);
        return true;
    }
}
