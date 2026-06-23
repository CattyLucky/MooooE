using Content.Shared._Forge.Trade;
using Robust.Shared.Random;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private string ResolveRuntimeGridName(ContractServerData contract, string fallbackPrefix)
    {
        return ResolveRuntimeGridName(contract, fallbackPrefix, null);
    }

    private string ResolveRuntimeGridName(
        ContractServerData contract,
        string fallbackPrefix,
        IReadOnlyList<string>? localNames
    )
    {
        if (localNames is { Count: > 0 })
            return _random.Pick(localNames).Trim();

        if (contract.Config.GridNames.Count > 0)
            return _random.Pick(contract.Config.GridNames).Trim();

        if (!string.IsNullOrWhiteSpace(contract.Config.GridName))
            return contract.Config.GridName.Trim();

        return string.IsNullOrWhiteSpace(contract.Name)
            ? fallbackPrefix
            : $"{fallbackPrefix}: {contract.Name}";
    }
}
