using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public enum ContractExecutionKind : byte
{
    InventoryDelivery = 0,
    TrackedDeliveryObjective,
    RetrievalRouteDelivery,
    HuntObjective,
    GhostRoleObjective
}
