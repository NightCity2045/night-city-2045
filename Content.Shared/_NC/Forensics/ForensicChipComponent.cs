using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Map;

namespace Content.Shared._NC.Forensics;

/// <summary>
/// Компонент для "щепки" с данными места преступления.
/// При вставке в КПК отображает метку на карте.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ForensicChipComponent : Component
{
    [DataField, AutoNetworkedField]
    public string VictimName = "Unknown";

    [AutoNetworkedField]
    public NetCoordinates Coordinates;

    [AutoNetworkedField]
    public TimeSpan Timestamp;
}
