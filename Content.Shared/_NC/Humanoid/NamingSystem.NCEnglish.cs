using Content.Shared.Dataset;
using Robust.Shared.Enums;
using Robust.Shared.Random;

namespace Content.Shared.Humanoid;

public sealed partial class NamingSystem
{
    private const string NCMaleFirstNames = "NCNamesFirstMaleEnglish";
    private const string NCFemaleFirstNames = "NCNamesFirstFemaleEnglish";
    private const string NCLastNames = "NCNamesLastEnglish";

    /// <summary>
    /// Builds a Latin-only first and last name for Night City character profiles.
    /// </summary>
    public string GetNCEnglishName(Gender gender)
    {
        var firstNames = gender == Gender.Female ? NCFemaleFirstNames : NCMaleFirstNames;
        var first = _random.Pick(_prototypeManager.Index<DatasetPrototype>(firstNames).Values);
        var last = _random.Pick(_prototypeManager.Index<DatasetPrototype>(NCLastNames).Values);

        return Loc.GetString("namepreset-firstlast", ("first", first), ("last", last));
    }
}
