using Content.Server.Station.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared.Procedural;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Server.Station.Components;

/// <summary>
/// Runs EnsurePlanet against the largest grid on Mapinit.
/// </summary>
[RegisterComponent, Access(typeof(StationBiomeSystem))]
public sealed partial class StationBiomeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Biome = "Grasslands";

    // If null, its random
    [DataField]
    public int? Seed = null;

    [DataField]
    public Color MapLightColor = Color.Black;
}
