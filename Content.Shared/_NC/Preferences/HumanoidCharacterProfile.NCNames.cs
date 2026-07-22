using Content.Shared.Humanoid;
using Robust.Shared.Enums;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Validates character-name input on the client before it reaches the profile.
    /// </summary>
    public static bool IsValidNCNameInput(string name)
    {
        if (name.Length > MaxNameLength)
            return false;

        foreach (var character in name)
        {
            if (!IsAllowedNCNameCharacter(character))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Enforces the same Latin-only rule during authoritative server validation.
    /// </summary>
    public static string SanitizeNCName(string name)
    {
        // A fixed-size buffer is sufficient because character profile names are capped at MaxNameLength.
        var buffer = new char[name.Length];
        var count = 0;

        foreach (var character in name)
        {
            if (IsAllowedNCNameCharacter(character))
                buffer[count++] = character;
        }

        return new string(buffer, 0, count).Trim();
    }

    /// <summary>
    /// Prevents separator-only names from passing authoritative validation.
    /// </summary>
    public static bool HasNCEnglishLetter(string name)
    {
        foreach (var character in name)
        {
            if (IsNCEnglishLetter(character))
                return true;
        }

        return false;
    }

    private static bool IsAllowedNCNameCharacter(char character)
    {
        return IsNCEnglishLetter(character) || character is ' ' or '-' or '\'';
    }

    private static bool IsNCEnglishLetter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
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
