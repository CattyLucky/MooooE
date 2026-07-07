namespace Content.Server._Forge.AutoSalarySystem;

[RegisterComponent]
public sealed partial class AutoSalaryComponent : Component
{
    [DataField]
    public TimeSpan LastSalaryAt = TimeSpan.Zero;

    [DataField]
    public TimeSpan NextRetryAt = TimeSpan.Zero;

    [DataField]
    public string? JobPrototype;
}
