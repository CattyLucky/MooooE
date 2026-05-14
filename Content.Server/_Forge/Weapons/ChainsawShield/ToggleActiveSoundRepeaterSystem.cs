using Content.Shared._Forge.Weapons;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Weapons;

public sealed class ToggleActiveSoundRepeaterSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ToggleActiveSoundRepeaterComponent, ItemToggledEvent>(OnToggled);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ToggleActiveSoundRepeaterComponent, ItemToggleComponent>();
        while (query.MoveNext(out var uid, out var repeater, out var toggle))
        {
            if (!toggle.Activated || _timing.CurTime < repeater.NextSound)
                continue;

            _audio.PlayPvs(repeater.Sound, uid);
            ScheduleNext(repeater, repeater.Interval);
        }
    }

    private void OnToggled(EntityUid uid, ToggleActiveSoundRepeaterComponent component, ref ItemToggledEvent args)
    {
        if (!args.Activated)
            return;

        ScheduleNext(component, component.InitialDelay);
    }

    private void ScheduleNext(ToggleActiveSoundRepeaterComponent component, float delay)
    {
        component.NextSound = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }
}
