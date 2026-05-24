namespace Content.Server._Forge.Xenoarchaeology;

[RegisterComponent]
public sealed partial class ForgeArtifactStudyPresetComponent : Component
{
    [DataField]
    public int NodeCount = 5;

    [DataField]
    public List<string> Triggers = new()
    {
        "TriggerInteraction",
        "TriggerExamine",
        "TriggerItemLanded",
        "TriggerMusic",
    };

    [DataField]
    public List<string> Effects = new()
    {
        "EffectGoodFeeling",
        "EffectBadFeeling",
        "EffectLightFlicker",
        "EffectJunkSpawn",
    };
}
