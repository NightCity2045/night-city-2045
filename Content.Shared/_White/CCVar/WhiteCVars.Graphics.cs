using Robust.Shared.Configuration;

namespace Content.Shared._White.CCVar;

public sealed partial class WhiteCVars
{
    /// <summary>
    /// What intensity will the grain shader be at
    /// </summary>
    public static readonly CVarDef<float> FilmGrainStrength =
        CVarDef.Create("graphics.film_grain_strength", 60f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Grain shader on/off
    /// </summary>
    public static readonly CVarDef<bool> FilmGrain =
        CVarDef.Create("graphics.film_grain", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Bloom shader on/off
    /// </summary>
    public static readonly CVarDef<bool> Bloom =
        CVarDef.Create("graphics.bloom", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Bloom intensity
    /// </summary>
    public static readonly CVarDef<int> BloomIntensity =
        CVarDef.Create("graphics.bloom_intensity", 150, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Bloom threshold
    /// </summary>
    public static readonly CVarDef<float> BloomThreshold =
        CVarDef.Create("graphics.bloom_threshold", 0.5f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Vignette shader on/off
    /// </summary>
    public static readonly CVarDef<bool> Vignette =
        CVarDef.Create("graphics.vignette", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Vignette radius
    /// </summary>
    public static readonly CVarDef<float> VignetteRadius =
        CVarDef.Create("graphics.vignette_radius", 0.8f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Vignette softness
    /// </summary>
    public static readonly CVarDef<float> VignetteSoftness =
        CVarDef.Create("graphics.vignette_softness", 0.5f, CVar.CLIENTONLY | CVar.ARCHIVE);

}
