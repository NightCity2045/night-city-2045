using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Mirrors the client's selected UI culture for server-side localized messages.
    ///     Robust's loc.culture_name is archived locally, but not replicated to the server.
    /// </summary>
    public static readonly CVarDef<string> NCPreferredCulture =
        CVarDef.Create("nc.localization.preferred_culture", "en-US", CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
