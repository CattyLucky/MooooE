using System.Numerics;
using Content.Shared.Procedural;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems;
using Content.Shared._Forge.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static readonly (string Prototype, int Weight)[] HuntDungeonExteriorRocks =
    {
        ("WallRock", 46),
        ("WallRockCoal", 10),
        ("WallRockTin", 10),
        ("WallRockQuartz", 8),
        ("WallRockCopper", 6),
        ("WallRockLithium", 5),
        ("WallRockSilver", 4),
        ("WallRockPlasma", 3),
        ("WallRockGold", 3),
        ("WallRockUranium", 2),
        ("WallRockSalt", 2),
        ("WallRockDiamond", 1),
    };

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

        state.HuntDungeonAnchorCoordinates = anchorCoords;

        return TryQueueNextHuntDungeonGeneration(contractId, contract, state);
    }

    private bool TryQueueNextHuntDungeonGeneration(
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (state.HuntDungeonAnchorCoordinates == null)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve dungeon spawn anchor.");
            return false;
        }

        if (!TryPickHuntDungeonPrototype(contractId, contract.Config.HuntDungeons, out var dungeonPrototype))
            return false;

        var dungeonConfig = _prototypes.Index<DungeonConfigPrototype>(dungeonPrototype);
        var generationMap = _map.CreateMap(out var generationMapId, runMapInit: false);
        Entity<MapGridComponent> grid;
        try
        {
            grid = _mapManager.CreateGridEntity(generationMapId);
            _xform.SetMapCoordinates(grid, new MapCoordinates(Vector2.Zero, generationMapId));
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot create dungeon grid: {e}");
            _map.DeleteMap(generationMapId);
            return false;
        }

        try
        {
            state.HuntDebrisEntity = grid.Owner;
            state.HuntDungeonGenerationMap = generationMap;
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
            state.HuntDungeonGenerationMap = null;
            _map.DeleteMap(generationMapId);
            return false;
        }
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
        return IsHuntDebrisAreaClear(coords, new Vector2(diameter, diameter), 0f);
    }

    private bool IsHuntDebrisAreaClear(MapCoordinates coords, Vector2 size, float safetyRadius)
    {
        var bounds = Box2.CenteredAround(
                coords.Position,
                new Vector2(Math.Max(1f, size.X), Math.Max(1f, size.Y)))
            .Enlarged(Math.Max(0f, safetyRadius));

        _huntDebrisPlacementGridScratch.Clear();
        _mapManager.FindGridsIntersecting(
            coords.MapId,
            bounds,
            ref _huntDebrisPlacementGridScratch,
            includeMap: false);

        return _huntDebrisPlacementGridScratch.Count == 0;
    }

    private bool TryGetHuntDungeonPlacementMapCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        EntityCoordinates debrisCoords,
        Box2 generatedBounds,
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
        var placementPadding = config.HuntDebrisSafetyRadius + NcContractTuning.HuntDungeonExteriorPadding + 1f;
        if (!IsHuntDebrisAreaClear(candidate, generatedBounds.Size, placementPadding))
            return false;

        spawnCoords = candidate;
        return true;
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

        if (state.HuntDungeonGenerationMap is not { } generationMap ||
            generationMap == EntityUid.Invalid ||
            TerminatingOrDeleted(generationMap) ||
            !TryConsolidateHuntDungeonGeneration(
                key.ContractId,
                state,
                generationMap,
                out var generatedGrid,
                out var generatedBounds))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated grid is missing.");
            return;
        }

        if (!TryPlaceGeneratedHuntDungeon(
                key.Store,
                contract,
                state,
                generationMap,
                generatedGrid,
                generatedBounds,
                out var placedGrid))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': cannot find a free dungeon placement after {Math.Max(1, contract.Config.HuntDebrisPlacementAttempts)} attempts.");
            return;
        }

        SpawnHuntDungeonExterior(key.ContractId, placedGrid.Owner, placedGrid.Comp);

        var spawnCoordinates = new List<EntityCoordinates>();
        spawnCoordinates.Clear();
        CollectHuntDungeonRoomSpawnCoordinates(dungeons, placedGrid.Owner, placedGrid.Comp, spawnCoordinates);
        if (spawnCoordinates.Count == 0)
            CollectHuntDebrisSpawnCoordinates(placedGrid.Owner, placedGrid.Comp, spawnCoordinates);

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

        if (state.PinpointerEntities.Count > 0 &&
            TryResolveSpawnedHuntPinpointerTarget(key.Store, contract, state, out var pinpointerTarget))
        {
            RetargetObjectivePinpointers(key, state, pinpointerTarget);
        }

        CollectHuntDungeonRoomSpawnCoordinates(dungeons, placedGrid.Owner, placedGrid.Comp, spawnCoordinates);
        if (spawnCoordinates.Count == 0)
            CollectHuntDebrisSpawnCoordinates(placedGrid.Owner, placedGrid.Comp, spawnCoordinates);

        RemoveHuntTargetOccupiedCoordinates(state, spawnCoordinates);
        SpawnHuntDungeonFill(key.ContractId, contract.Config, dungeons, spawnCoordinates);

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

    private bool TryConsolidateHuntDungeonGeneration(
        string contractId,
        ObjectiveRuntimeState state,
        EntityUid generationMap,
        out Entity<MapGridComponent> generatedGrid,
        out Box2 generatedBounds
    )
    {
        generatedGrid = default;
        generatedBounds = default;
        if (!TryComp(generationMap, out TransformComponent? mapXform) || mapXform.ChildCount == 0)
            return false;

        if (state.HuntDebrisEntity is not { } primaryGridUid ||
            primaryGridUid == EntityUid.Invalid ||
            !TryComp(primaryGridUid, out MapGridComponent? primaryGrid) ||
            !TryComp(primaryGridUid, out TransformComponent? primaryXform))
        {
            return false;
        }

        var mergeGrids = new List<Entity<MapGridComponent>>();
        var children = mapXform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp(child, out MapGridComponent? childGrid))
                continue;

            if (child == primaryGridUid)
                continue;

            mergeGrids.Add((child, childGrid));
        }

        for (var i = 0; i < mergeGrids.Count; i++)
        {
            var mergeGrid = mergeGrids[i];
            if (mergeGrid.Owner == EntityUid.Invalid || TerminatingOrDeleted(mergeGrid.Owner))
                continue;

            if (!TryComp(mergeGrid.Owner, out TransformComponent? mergeXform))
                continue;

            var mergeMatrix = Matrix3x2.Multiply(
                _xform.GetWorldMatrix(mergeXform),
                _xform.GetInvWorldMatrix(primaryXform));

            try
            {
                _gridFixture.Merge(
                    primaryGridUid,
                    mergeGrid.Owner,
                    mergeMatrix,
                    primaryGrid,
                    mergeGrid.Comp,
                    primaryXform,
                    mergeXform);
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt dungeon generation for '{contractId}' failed to merge grid fragment '{ToPrettyString(mergeGrid.Owner)}': {e}");
                return false;
            }

            if (!TryComp(primaryGridUid, out primaryGrid) ||
                !TryComp(primaryGridUid, out primaryXform))
            {
                return false;
            }
        }

        primaryGrid.CanSplit = false;
        generatedBounds = _xform.GetWorldMatrix(primaryXform).TransformBox(primaryGrid.LocalAABB);
        generatedGrid = (primaryGridUid, primaryGrid);
        return true;
    }

    private bool TryPlaceGeneratedHuntDungeon(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid generationMap,
        Entity<MapGridComponent> generatedGrid,
        Box2 generatedBounds,
        out Entity<MapGridComponent> placedGrid
    )
    {
        placedGrid = default;

        if (state.HuntDungeonAnchorCoordinates is not { } anchorCoords)
            return false;

        var attempts = Math.Max(1, contract.Config.HuntDebrisPlacementAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!TryGetHuntDungeonPlacementMapCoordinates(
                    store,
                    contract.Config,
                    anchorCoords,
                    generatedBounds,
                    out var placementCenter) ||
                !_map.TryGetMap(placementCenter.MapId, out var targetMap))
            {
                continue;
            }

            var placementOffset = placementCenter.Position - generatedBounds.Center;
            var xform = Transform(generatedGrid.Owner);
            var position = _xform.GetWorldPosition(xform);
            var rotation = _xform.GetWorldRotation(xform);
            _xform.SetParent(generatedGrid.Owner, xform, targetMap.Value);
            _xform.SetWorldPositionRotation(generatedGrid.Owner, position + placementOffset, rotation, xform);

            state.HuntDungeonGridEntities.Clear();
            state.HuntDungeonGridEntities.Add(generatedGrid.Owner);
            state.HuntDebrisEntity = generatedGrid.Owner;
            state.HuntDungeonGenerationMap = null;
            placedGrid = generatedGrid;
            ConfigureHuntDungeonRadarContact(generatedGrid.Owner);

            if (TryComp(generationMap, out TransformComponent? generationMapXform))
                _map.DeleteMap(generationMapXform.MapID);

            return true;
        }

        return false;
    }

    private void ConfigureHuntDungeonRadarContact(EntityUid grid)
    {
        _contractMeta.SetEntityName(grid, "contract hunt site");
        _shuttle.SetIFFColor(grid, Color.FromHex("#d67e27"));
        _shuttle.AddIFFFlag(grid, IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
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

    private void SpawnHuntDungeonExterior(string contractId, EntityUid site, MapGridComponent grid)
    {
        if (!_prototypes.TryIndex<ContentTileDefinition>(NcContractTuning.HuntDungeonExteriorTile, out var tileDef))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt dungeon exterior for '{contractId}' skipped missing tile '{NcContractTuning.HuntDungeonExteriorTile}'.");
            return;
        }

        var generatedTiles = new HashSet<Vector2i>();
        if (!TryCollectHuntDungeonTileBounds(site, grid, generatedTiles, out var bounds))
            return;

        var padding = Math.Max(1, NcContractTuning.HuntDungeonExteriorPadding);
        var center = new Vector2(
            (bounds.Left + bounds.Right + 1) / 2f,
            (bounds.Bottom + bounds.Top + 1) / 2f);
        var radiusX = Math.Max(1f, (bounds.Right - bounds.Left + 1) / 2f + padding);
        var radiusY = Math.Max(1f, (bounds.Top - bounds.Bottom + 1) / 2f + padding);

        var exteriorTiles = new List<(Vector2i Index, Tile Tile)>();
        var rockCandidates = new List<Vector2i>();
        var tileRandom = _random.GetRandom();

        for (var x = bounds.Left - padding; x <= bounds.Right + padding; x++)
        {
            for (var y = bounds.Bottom - padding; y <= bounds.Top + padding; y++)
            {
                var tile = new Vector2i(x, y);
                if (generatedTiles.Contains(tile) ||
                    (_map.TryGetTileRef(site, grid, tile, out var existingTile) && !existingTile.Tile.IsEmpty))
                {
                    continue;
                }

                if (!TryGetHuntDungeonExteriorDistance(tile, center, radiusX, radiusY, out var distance))
                    continue;

                exteriorTiles.Add((tile, _tile.GetVariantTile(tileDef, tileRandom)));

                if (!IsNearGeneratedHuntDungeonTile(
                        tile,
                        generatedTiles,
                        NcContractTuning.HuntDungeonExteriorCoreClearance) &&
                    ShouldSpawnHuntDungeonExteriorRock(distance))
                {
                    rockCandidates.Add(tile);
                }
            }
        }

        if (exteriorTiles.Count == 0)
            return;

        _map.SetTiles(site, grid, exteriorTiles);
        SpawnHuntDungeonExteriorRocks(contractId, site, grid, rockCandidates);
    }

    private bool TryCollectHuntDungeonTileBounds(
        EntityUid site,
        MapGridComponent grid,
        HashSet<Vector2i> output,
        out Box2i bounds
    )
    {
        output.Clear();
        bounds = new Box2i();

        var hasTile = false;
        var left = 0;
        var right = 0;
        var bottom = 0;
        var top = 0;

        var enumerator = _map.GetAllTilesEnumerator(site, grid, true);
        while (enumerator.MoveNext(out var tile))
        {
            var tileRef = tile.Value;
            if (tileRef.Tile.IsEmpty)
                continue;

            var indices = tileRef.GridIndices;
            output.Add(indices);

            if (!hasTile)
            {
                left = right = indices.X;
                bottom = top = indices.Y;
                hasTile = true;
                continue;
            }

            left = Math.Min(left, indices.X);
            right = Math.Max(right, indices.X);
            bottom = Math.Min(bottom, indices.Y);
            top = Math.Max(top, indices.Y);
        }

        if (!hasTile)
            return false;

        bounds = new Box2i(left, bottom, right, top);
        return true;
    }

    private static bool TryGetHuntDungeonExteriorDistance(
        Vector2i tile,
        Vector2 center,
        float radiusX,
        float radiusY,
        out float distance
    )
    {
        var dx = (tile.X + 0.5f - center.X) / radiusX;
        var dy = (tile.Y + 0.5f - center.Y) / radiusY;
        distance = dx * dx + dy * dy;

        var edgeNoise = GetHuntDungeonExteriorEdgeNoise(tile);
        return distance <= 1f + edgeNoise;
    }

    private static float GetHuntDungeonExteriorEdgeNoise(Vector2i tile)
    {
        unchecked
        {
            var hash = tile.X * 73856093 ^ tile.Y * 19349663;
            hash ^= hash >> 13;
            hash *= 1274126177;
            var normalized = (hash & 0x7fffffff) / (float) int.MaxValue;
            return (normalized - 0.5f) * 0.24f;
        }
    }

    private bool ShouldSpawnHuntDungeonExteriorRock(float distance)
    {
        if (distance < 0.36f)
            return false;

        var chance = distance > 0.72f
            ? NcContractTuning.HuntDungeonExteriorEdgeRockChance
            : NcContractTuning.HuntDungeonExteriorInnerRockChance;

        return _random.Prob(chance);
    }

    private static bool IsNearGeneratedHuntDungeonTile(
        Vector2i tile,
        HashSet<Vector2i> generatedTiles,
        int clearance
    )
    {
        var radius = Math.Max(0, clearance);
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (generatedTiles.Contains(new Vector2i(tile.X + x, tile.Y + y)))
                    return true;
            }
        }

        return false;
    }

    private void SpawnHuntDungeonExteriorRocks(
        string contractId,
        EntityUid site,
        MapGridComponent grid,
        List<Vector2i> candidates
    )
    {
        var maxCount = Math.Min(candidates.Count, NcContractTuning.HuntDungeonExteriorMaxRockCount);
        var spawned = 0;
        while (spawned < maxCount && candidates.Count > 0)
        {
            var candidateIndex = _random.Next(candidates.Count);
            var tile = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex);

            if (!TryPickHuntDungeonExteriorRock(contractId, out var prototype) ||
                !IsHuntDungeonExteriorRockCoordinateValid(site, grid, tile))
            {
                continue;
            }

            try
            {
                Spawn(prototype, _map.GridTileToLocal(site, grid, tile));
                spawned++;
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt dungeon exterior for '{contractId}' failed to spawn '{prototype}': {e}");
            }
        }
    }

    private bool TryPickHuntDungeonExteriorRock(string contractId, out string prototypeId)
    {
        prototypeId = string.Empty;

        Span<int> weights = stackalloc int[HuntDungeonExteriorRocks.Length];
        var total = 0;
        for (var i = 0; i < HuntDungeonExteriorRocks.Length; i++)
        {
            var entry = HuntDungeonExteriorRocks[i];
            if (entry.Weight <= 0 || !_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                continue;

            weights[i] = entry.Weight;
            total += entry.Weight;
        }

        if (total > 0)
        {
            var roll = _random.Next(total);
            for (var i = 0; i < HuntDungeonExteriorRocks.Length; i++)
            {
                var weight = weights[i];
                if (weight <= 0)
                    continue;

                roll -= weight;
                if (roll >= 0)
                    continue;

                prototypeId = HuntDungeonExteriorRocks[i].Prototype;
                return true;
            }
        }

        Sawmill.Warning(
            $"[Contracts] Hunt dungeon exterior for '{contractId}' skipped: no configured rock prototypes exist.");
        return false;
    }

    private bool IsHuntDungeonExteriorRockCoordinateValid(
        EntityUid site,
        MapGridComponent grid,
        Vector2i tile
    )
    {
        return _map.TryGetTileRef(site, grid, tile, out var tileRef) &&
               !tileRef.Tile.IsEmpty &&
               !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
    }

    private void SpawnHuntDungeonFill(
        string contractId,
        ContractObjectiveConfigData config,
        IReadOnlyList<Dungeon> dungeons,
        List<EntityCoordinates> spawnCoordinates
    )
    {
        var count = PickHuntDungeonFillCount(config, spawnCoordinates.Count, CountGeneratedHuntDungeonRooms(dungeons));
        if (count <= 0)
            return;

        var spawned = 0;
        while (spawned < count && spawnCoordinates.Count > 0)
        {
            if (!TryPickHuntDungeonFillPrototype(contractId, config.HuntDungeonFill, out var prototype))
                return;

            var coordIndex = _random.Next(spawnCoordinates.Count);
            var coords = spawnCoordinates[coordIndex];
            spawnCoordinates.RemoveAt(coordIndex);

            if (!IsHuntDungeonFillCoordinateValid(coords))
                continue;

            try
            {
                Spawn(prototype, coords);
                spawned++;
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt dungeon fill for '{contractId}' failed to spawn '{prototype}': {e}");
            }
        }
    }

    private int PickHuntDungeonFillCount(
        ContractObjectiveConfigData config,
        int availableCoordinates,
        int roomCount
    )
    {
        if (availableCoordinates <= 0 || config.HuntDungeonFill.Count == 0)
            return 0;

        var min = Math.Max(
            Math.Max(0, config.HuntDungeonFillCount.Min),
            roomCount * NcContractTuning.HuntDungeonFillMinPerRoom);
        var max = Math.Max(
            Math.Max(min, config.HuntDungeonFillCount.Max),
            roomCount * NcContractTuning.HuntDungeonFillMaxPerRoom);
        if (max <= 0)
            return 0;

        var count = min == max
            ? min
            : _random.Next(min, max + 1);

        return Math.Min(count, availableCoordinates);
    }

    private static int CountGeneratedHuntDungeonRooms(IReadOnlyList<Dungeon> dungeons)
    {
        var count = 0;
        for (var i = 0; i < dungeons.Count; i++)
            count += dungeons[i].Rooms.Count;

        return count;
    }

    private bool TryPickHuntDungeonFillPrototype(
        string contractId,
        IReadOnlyList<NcHuntDungeonFillEntry> fill,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (fill.Count == 0)
            return false;

        var weights = fill.Count <= 128
            ? stackalloc int[fill.Count]
            : new int[fill.Count];

        long total = 0;
        for (var i = 0; i < fill.Count; i++)
        {
            var entry = fill[i];
            if (entry == null ||
                entry.Weight <= 0 ||
                !_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
            {
                continue;
            }

            weights[i] = entry.Weight;
            total += entry.Weight;
        }

        if (total > 0)
        {
            var roll = total <= int.MaxValue
                ? _random.Next((int)total)
                : (long)(_random.NextDouble() * total);

            for (var i = 0; i < fill.Count; i++)
            {
                var weight = weights[i];
                if (weight <= 0)
                    continue;

                roll -= weight;
                if (roll >= 0)
                    continue;

                var entry = fill[i];
                if (entry == null)
                    continue;

                prototypeId = entry.Prototype;
                return true;
            }
        }

        Sawmill.Warning(
            $"[Contracts] Hunt dungeon fill for '{contractId}' skipped: no configured fill prototypes exist.");
        return false;
    }

    private void ForceLoadHuntDebris(EntityUid debris)
    {
        if (!HasComp<LocalityLoaderComponent>(debris))
            return;

        RaiseLocalEvent(debris, new LocalStructureLoadedEvent());
        RemCompDeferred<LocalityLoaderComponent>(debris);
    }

    private void CollectHuntDungeonRoomSpawnCoordinates(
        IReadOnlyList<Dungeon> dungeons,
        EntityUid debris,
        MapGridComponent grid,
        List<EntityCoordinates> output
    )
    {
        output.Clear();
        var seen = new HashSet<Vector2i>();
        for (var i = 0; i < dungeons.Count; i++)
        {
            foreach (var tile in dungeons[i].RoomTiles)
            {
                if (!seen.Add(tile) ||
                    !_map.TryGetTileRef(debris, grid, tile, out var tileRef) ||
                    tileRef.Tile.IsEmpty ||
                    _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
                {
                    continue;
                }

                output.Add(_map.GridTileToLocal(debris, grid, tile));
            }
        }
    }

    private void CollectHuntDebrisSpawnCoordinates(
        EntityUid debris,
        MapGridComponent grid,
        List<EntityCoordinates> output
    )
    {
        output.Clear();
        AppendHuntDebrisSpawnCoordinates((debris, grid), output);
    }

    private void CollectHuntDebrisSpawnCoordinates(
        IReadOnlyList<Entity<MapGridComponent>> grids,
        List<EntityCoordinates> output
    )
    {
        output.Clear();
        for (var i = 0; i < grids.Count; i++)
            AppendHuntDebrisSpawnCoordinates(grids[i], output);
    }

    private void AppendHuntDebrisSpawnCoordinates(
        Entity<MapGridComponent> grid,
        List<EntityCoordinates> output
    )
    {
        var enumerator = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp, true);
        while (enumerator.MoveNext(out var tile))
        {
            var tileRef = tile.Value;
            if (tileRef.Tile.IsEmpty ||
                _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
                continue;

            output.Add(_map.GridTileToLocal(grid.Owner, grid.Comp, tileRef.GridIndices));
        }
    }

    private void RemoveHuntTargetOccupiedCoordinates(
        ObjectiveRuntimeState state,
        List<EntityCoordinates> coordinates
    )
    {
        if (coordinates.Count == 0 || state.HuntSpawnedTargets.Count == 0)
            return;

        var occupied = new HashSet<(EntityUid Grid, Vector2i Tile)>();
        for (var i = 0; i < state.HuntSpawnedTargets.Count; i++)
        {
            var target = state.HuntSpawnedTargets[i];
            if (target == EntityUid.Invalid || TerminatingOrDeleted(target))
                continue;

            var targetXform = Transform(target);
            if (targetXform.GridUid is not { } targetGrid ||
                !TryComp(targetGrid, out MapGridComponent? grid))
                continue;

            occupied.Add((targetGrid, _map.TileIndicesFor(targetGrid, grid, targetXform.Coordinates)));
        }

        if (occupied.Count == 0)
            return;

        for (var i = coordinates.Count - 1; i >= 0; i--)
        {
            var coordinatesXform = coordinates[i];
            if (!TryComp(coordinatesXform.EntityId, out MapGridComponent? grid))
                continue;

            var tile = _map.TileIndicesFor(coordinatesXform.EntityId, grid, coordinatesXform);
            if (occupied.Contains((coordinatesXform.EntityId, tile)))
                coordinates.RemoveAt(i);
        }
    }

    private bool IsHuntDungeonFillCoordinateValid(EntityCoordinates coordinates)
    {
        return coordinates != EntityCoordinates.Invalid &&
               _turf.TryGetTileRef(coordinates, out var tileRef) &&
               tileRef is { } tile &&
               !tile.Tile.IsEmpty &&
               !_turf.IsTileBlocked(tile, CollisionGroup.MobMask);
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
