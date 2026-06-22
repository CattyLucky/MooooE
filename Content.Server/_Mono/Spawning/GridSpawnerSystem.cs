using Content.Server.RandomMetadata; // Forge-Change: apply late-added random metadata to spawned grids.
using Content.Shared.Random.Helpers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Mono.Spawning;

public sealed partial class GridSpawnerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private RandomMetadataSystem _randomMetadata = default!; // Forge-Change
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridSpawnerComponent, MapInitEvent>(OnInit);
    }

    private void OnInit(Entity<GridSpawnerComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent.Owner);

        if (_loader.TryLoadGrid(xform.MapID, ent.Comp.Path, out var grid, offset: _transform.GetWorldPosition(xform)))
        {
            if (ent.Comp.NameGrid)
            {
                if (_proto.TryIndex(ent.Comp.NameDataset, out var dataset))
                {
                    _metadata.SetEntityName(grid.Value, _random.Pick(dataset));
                }
                else
                {
                    var name = ent.Comp.Path.FilenameWithoutExtension;
                    _metadata.SetEntityName(grid.Value, name);
                }
            }

            EntityManager.AddComponents(grid.Value, ent.Comp.AddComponents);

            // Forge-Change-start: RandomMetadata is added after MapInit for spawned grids, so apply it manually.
            if (TryComp<RandomMetadataComponent>(grid.Value, out var randomMetadata))
                _randomMetadata.ApplyRandomMetadata(grid.Value, randomMetadata);
            // Forge-Change-end
        }
    }
}
