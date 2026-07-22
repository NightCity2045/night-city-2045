using System.Text.RegularExpressions;
using Content.Shared.Humanoid;
using Robust.Shared.Enums;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    [GeneratedRegex("[^A-Za-z '\\-]")]
    private static partial Regex NCInvalidNameCharacterRegex();

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex NCEnglishLetterRegex();

    /// <summary>
    /// Validates character-name input on the client before it reaches the profile.
    /// </summary>
    public static bool IsValidNCNameInput(string name)
    {
        return name.Length <= MaxNameLength && !NCInvalidNameCharacterRegex().IsMatch(name);
    }

    /// <summary>
    /// Enforces the same Latin-only rule during authoritative server validation.
    /// </summary>
    public static string SanitizeNCName(string name)
    {
        return NCInvalidNameCharacterRegex().Replace(name, string.Empty).Trim();
    }

    /// <summary>
    /// Prevents separator-only names from passing authoritative validation.
    /// </summary>
    public static bool HasNCEnglishLetter(string name)
    {
        return NCEnglishLetterRegex().IsMatch(name);
    }

    /// <summary>
    /// Generates a profile name exclusively from the Night City English datasets.
    /// </summary>
    public static string GetNCEnglishName(Gender gender)
    {
        var namingSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<NamingSystem>();
        return namingSystem.GetNCEnglishName(gender);
    }
}
