using Robust.Shared.Serialization;

namespace Content.Shared._NC.Stats;

/// <summary>
/// Current carried-weight penalty band derived from BODY and inventory mass.
/// </summary>
[Serializable, NetSerializable]
public enum NCBodyLoadLevel : byte
{
    None = 0,
    Light = 1,
    Heavy = 2,
    Overloaded = 3,
}
