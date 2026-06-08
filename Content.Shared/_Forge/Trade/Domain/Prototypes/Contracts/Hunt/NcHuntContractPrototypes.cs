using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public enum NcHuntCompletionMode : byte
{
    ConfirmedKill = 0,
    TrophyTurnIn = 1,
    BodyTurnIn = 2
}

[DataDefinition]
public sealed partial class NcHuntTargetData
{
    [DataField("group")]
    public string Group { get; set; } = string.Empty;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public int Count { get; set; }

    /// <summary>
    ///     For BodyTurnIn hunts, marks the spawned target whose corpse must be brought back.
    /// </summary>
    [DataField("body")]
    public bool Body { get; set; }
}

[DataDefinition]
public sealed partial class NcHuntCompletionData
{
    [DataField("mode", required: true)]
    public NcHuntCompletionMode Mode { get; set; } = NcHuntCompletionMode.ConfirmedKill;

    [DataField("trophy")]
    public string Trophy { get; set; } = string.Empty;
}

[DataDefinition]
public sealed partial class NcHuntDebrisEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcHuntDungeonEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcHuntDungeonFillEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcHuntSpawnData
{
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; set; } = new();

    [DataField("debris")]
    public List<NcHuntDebrisEntry> Debris { get; set; } = new();

    [DataField("dungeons")]
    public List<NcHuntDungeonEntry> Dungeons { get; set; } = new();

    [DataField("dungeonFill")]
    public List<NcHuntDungeonFillEntry> DungeonFill { get; set; } = new()
    {
        new() { Prototype = "SpawnDungeonLootArmoryGuns", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootCrateEngi", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootCrateGeneral", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootCrateService", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootMaterialsBasicFull", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootMaterialsValuableFull", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootPartsEngi", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootVaultGuns", Weight = 9 },
        new() { Prototype = "SpawnDungeonLootArmoryMelee", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootArmoryRare", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootClutterEngi", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootCrateVehicle", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootFood", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootKitchenTabletop", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootLatheEngi", Weight = 8 },
        new() { Prototype = "SpawnDungeonLootBureaucracy", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootCircuitBoard", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootClutterKitchen", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootCrateMed", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootCrateRestockGeneral", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootKitsFirstAid", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootLockersEngi", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootLockersProtectiveGear", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootSeed", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootToolsBasicEngineering", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootToolsHydroponics", Weight = 7 },
        new() { Prototype = "SpawnDungeonLootArmoryClutter", Weight = 6 },
        new() { Prototype = "SpawnDungeonLootLockersMed", Weight = 6 },
        new() { Prototype = "SpawnDungeonLootMugs", Weight = 6 },
        new() { Prototype = "SpawnDungeonLootPowerCell", Weight = 6 },
        new() { Prototype = "SpawnDungeonLootSpesos", Weight = 6 },
        new() { Prototype = "SpawnDungeonLootCrateArmoryWeapon", Weight = 5 },
        new() { Prototype = "SpawnDungeonLootCrateMaterials", Weight = 5 },
        new() { Prototype = "SpawnDungeonLootMaterialsBasicSingle", Weight = 5 },
        new() { Prototype = "SpawnDungeonLootOresFull", Weight = 5 },
        new() { Prototype = "SalvageSpawnerTreasure", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootArmoryExplosives", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootChemsHydroponics", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootClutterSalvage", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootCrateHydro", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootLathe", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootLockersGeneral", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootMeleeT1", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootOresSingle", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootToolbox", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootToolsAdvancedEngineering", Weight = 4 },
        new() { Prototype = "SpawnDungeonLootArmorMercenary", Weight = 3 },
        new() { Prototype = "SpawnDungeonLootCrateScience", Weight = 3 },
        new() { Prototype = "SpawnDungeonLootHardsuitsSalvage", Weight = 3 },
        new() { Prototype = "SpawnDungeonLootLatheSalvage", Weight = 3 },
        new() { Prototype = "SpawnDungeonLootMaterialsValuableSingle", Weight = 3 },
        new() { Prototype = "SpawnDungeonLootToolsSalvage", Weight = 3 },
        new() { Prototype = "SpawnDungeonLootToolsSurgeryCrude", Weight = 3 },
        new() { Prototype = "SalvageSpawnerEquipmentValuable", Weight = 2 },
        new() { Prototype = "SalvageSpawnerTreasureValuable", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootBriefcase", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootChems", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootClothesSalvage", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootClutterHydroponics", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootClutterScience", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootCrateRestockService", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootHardsuitsMercenary", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootKitSurgery", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootLockersArmory", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootLockersSalvage", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootRnDDisk", Weight = 2 },
        new() { Prototype = "SpawnDungeonLootToolsSurgeryAdvanced", Weight = 2 },
        new() { Prototype = "SalvageSpawnerEquipment", Weight = 1 },
        new() { Prototype = "SalvageSpawnerScrapCommon", Weight = 1 },
        new() { Prototype = "SalvageSpawnerScrapCommon75", Weight = 1 },
        new() { Prototype = "SalvageSpawnerScrapValuable", Weight = 1 },
        new() { Prototype = "SalvageSpawnerScrapValuable75", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootArmoryClutterSec", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootClothesHydroponics", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootClothesMercenary", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootClothesScience", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootCrateArmoryArmor", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootCrateRestockMed", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootCutlery", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootLatheArmory", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootSuitStorageUnitsSalvage", Weight = 1 },
        new() { Prototype = "SpawnDungeonLootToolsSurgery", Weight = 1 },
    };

    [DataField("dungeonFillCount")]
    public IntRange DungeonFillCount { get; set; } = IntRange.Create(16, 24);

    [DataField("debrisMinDistance")]
    public float DebrisMinDistance { get; set; }

    [DataField("debrisMaxDistance")]
    public float DebrisMaxDistance { get; set; }

    [DataField("debrisSafetyRadius")]
    public float DebrisSafetyRadius { get; set; }

    [DataField("debrisPlacementAttempts")]
    public int DebrisPlacementAttempts { get; set; }
}

[Prototype("ncHuntGroup")]
public sealed partial class NcHuntGroupPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("prototypes", required: true)]
    public List<string> Prototypes { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}

[Prototype("ncHuntContract")]
public sealed partial class NcHuntContractPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("targets", required: true)]
    public List<NcHuntTargetData> Targets { get; private set; } = new();

    [DataField("completion", required: true)]
    public NcHuntCompletionData Completion { get; private set; } = new();

    [DataField("spawn", required: true)]
    public NcHuntSpawnData Spawn { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>Optional extension conditions evaluated by registered server-side handlers.</summary>
    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}
