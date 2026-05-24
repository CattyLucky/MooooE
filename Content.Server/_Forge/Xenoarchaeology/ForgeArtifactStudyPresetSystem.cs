using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared.Xenoarchaeology.XenoArtifacts;
using System.Linq;
using System.Reflection;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Xenoarchaeology;

public sealed class ForgeArtifactStudyPresetSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;

    private const int FirstNodeId = 100;
    private static readonly FieldInfo NodeTreeField =
        typeof(ArtifactComponent).GetField("NodeTree", BindingFlags.Instance | BindingFlags.Public)!;
    private static readonly FieldInfo CurrentNodeIdField =
        typeof(ArtifactComponent).GetField("CurrentNodeId", BindingFlags.Instance | BindingFlags.Public)!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ForgeArtifactStudyPresetComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, ForgeArtifactStudyPresetComponent component, MapInitEvent args)
    {
        Timer.Spawn(0, () =>
        {
            if (!Deleted(uid) && TryComp(uid, out ForgeArtifactStudyPresetComponent? preset))
                ApplyPreset(uid, preset);
        });
    }

    public bool ApplyPreset(EntityUid uid, ForgeArtifactStudyPresetComponent component)
    {
        if (!TryComp(uid, out ArtifactComponent? artifact))
            return false;

        var triggers = GetValidTriggers(component);
        var effects = GetValidEffects(component);
        if (triggers.Count == 0 || effects.Count == 0)
            return false;

        RemoveArtifactNodeComponents(uid);

        var nodes = BuildConnectedNodeTree(Math.Max(1, component.NodeCount), triggers, effects);
        if (!TryReplaceArtifactNodeTree(artifact, nodes))
            return false;

        nodes[0].Discovered = true;

        ApplyNodeComponents(uid, nodes[0]);
        Dirty(uid, artifact);

        return true;
    }

    private static bool TryReplaceArtifactNodeTree(ArtifactComponent artifact, List<ArtifactNode> nodes)
    {
        if (NodeTreeField.GetValue(artifact) is not List<ArtifactNode> nodeTree)
            return false;

        nodeTree.Clear();
        nodeTree.AddRange(nodes);
        CurrentNodeIdField.SetValue(artifact, nodes[0].Id);
        return true;
    }

    private List<string> GetValidTriggers(ForgeArtifactStudyPresetComponent component)
    {
        var triggers = new List<string>();
        foreach (var trigger in component.Triggers)
        {
            if (_prototype.HasIndex<ArtifactTriggerPrototype>(trigger))
                triggers.Add(trigger);
            else
                Log.Warning($"Forge artifact study preset references missing trigger prototype '{trigger}'.");
        }

        return triggers;
    }

    private List<string> GetValidEffects(ForgeArtifactStudyPresetComponent component)
    {
        var effects = new List<string>();
        foreach (var effect in component.Effects)
        {
            if (_prototype.HasIndex<ArtifactEffectPrototype>(effect))
                effects.Add(effect);
            else
                Log.Warning($"Forge artifact study preset references missing effect prototype '{effect}'.");
        }

        return effects;
    }

    private static List<ArtifactNode> BuildConnectedNodeTree(
        int nodeCount,
        IReadOnlyList<string> triggers,
        IReadOnlyList<string> effects
    )
    {
        var nodes = new List<ArtifactNode>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var node = new ArtifactNode
            {
                Id = FirstNodeId + i,
                Depth = i,
                Trigger = triggers[i % triggers.Count],
                Effect = effects[i % effects.Count],
            };

            if (i > 0)
                node.Edges.Add(FirstNodeId + i - 1);

            if (i + 1 < nodeCount)
                node.Edges.Add(FirstNodeId + i + 1);

            nodes.Add(node);
        }

        return nodes;
    }

    private void RemoveArtifactNodeComponents(EntityUid uid)
    {
        var componentNames = new HashSet<string>();
        foreach (var trigger in _prototype.EnumeratePrototypes<ArtifactTriggerPrototype>())
            AddComponentNames(componentNames, trigger.Components);

        foreach (var effect in _prototype.EnumeratePrototypes<ArtifactEffectPrototype>())
        {
            AddComponentNames(componentNames, effect.Components);
            AddComponentNames(componentNames, effect.PermanentComponents);
        }

        var entityPrototype = MetaData(uid).EntityPrototype;
        foreach (var name in componentNames)
        {
            var registration = Factory.GetRegistration(name);

            if (entityPrototype?.Components.TryGetComponent(name, out var prototypeComponent) ?? false)
            {
                var component = (Component) Factory.GetComponent(name);
                var restored = (object?) component;
                _serialization.CopyTo(prototypeComponent, ref restored);

                if (EntityManager.HasComponent(uid, registration.Type))
                    EntityManager.RemoveComponent(uid, registration.Type);

                if (restored is Component restoredComponent)
                    EntityManager.AddComponent(uid, restoredComponent);

                continue;
            }

            if (EntityManager.HasComponent(uid, registration.Type))
                EntityManager.RemoveComponent(uid, registration.Type);
        }
    }

    private static void AddComponentNames(HashSet<string> target, ComponentRegistry components)
    {
        foreach (var (name, _) in components)
            target.Add(name);
    }

    private void ApplyNodeComponents(EntityUid uid, ArtifactNode node)
    {
        if (!_prototype.TryIndex<ArtifactTriggerPrototype>(node.Trigger, out var trigger) ||
            !_prototype.TryIndex<ArtifactEffectPrototype>(node.Effect, out var effect))
        {
            return;
        }

        foreach (var (name, entry) in effect.Components.Concat(effect.PermanentComponents).Concat(trigger.Components))
        {
            var registration = Factory.GetRegistration(name);
            var component = (Component) Factory.GetComponent(registration);
            var copied = (object?) component;
            _serialization.CopyTo(entry.Component, ref copied);

            if (EntityManager.HasComponent(uid, registration.Type))
                EntityManager.RemoveComponent(uid, registration.Type);

            if (copied is Component copiedComponent)
                EntityManager.AddComponent(uid, copiedComponent);
        }
    }
}
