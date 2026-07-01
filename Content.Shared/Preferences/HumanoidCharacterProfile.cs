using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._NC.Bank.Components;
using Content.Shared._NC.Stats;
using Content.Shared._NC.Stats.Prototypes;
using Content.Shared._White.Bark;
using Content.Shared._White.Bark.Systems;
using Content.Shared._White.TTS;
using Content.Shared.CCVar;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences;

/// <summary>
/// Character profile. Looks immutable, but uses non-immutable semantics internally for serialization/code sanity purposes.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class HumanoidCharacterProfile : ICharacterProfile
{
    private static readonly Regex RestrictedNameRegex = new(@"[^A-Za-z0-9А-Яа-я '\-]");
    private static readonly Regex ICNameCaseRegex = new(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)");

    public const int MaxNameLength = 64;
    public const int MaxDescLength = 1024;
    public const int MaxCustomContentLength = 524288; // WD EDIT
    public const int StartingStatPoints = 62;
    public const int StartingSkillPoints = 82;

    /// Job preferences for initial spawn
    [DataField]
    private Dictionary<ProtoId<JobPrototype>, JobPriority> _jobPriorities = new()
    {
        {
            SharedGameTicker.FallbackOverflowJob, JobPriority.High
        },
    };

    /// Antags we have opted in to
    [DataField]
    private HashSet<ProtoId<AntagPrototype>> _antagPreferences = new();

    /// Enabled traits
    [DataField]
    private HashSet<ProtoId<TraitPrototype>> _traitPreferences = new();

    /// <see cref="_loadoutPreferences"/>
    public Dictionary<string, Loadout> LoadoutPreferences => _loadoutPreferences; // WWDP EDIT
    public IEnumerable<Loadout> LoadoutPreferencesList => _loadoutPreferences.Values; // WWDP EDIT

    [DataField]
    private Dictionary<string, Loadout> _loadoutPreferences = new(); // WWDP EDIT

    [DataField]
    public string Name { get; set; } = "John Doe";

    /// Detailed text that can appear for the character if <see cref="CCVars.FlavorText"/> is enabled
    [DataField]
    public string FlavorText { get; set; } = string.Empty;

    /// Associated <see cref="SpeciesPrototype"/> for this profile
    [DataField]
    public ProtoId<SpeciesPrototype> Species { get; set; } = SharedHumanoidAppearanceSystem.DefaultSpecies;

    // EE -- Contractors Change Start
    [DataField]
    public string Nationality { get; set; } = SharedHumanoidAppearanceSystem.DefaultNationality;

    [DataField]
    public string Employer { get; set; } = SharedHumanoidAppearanceSystem.DefaultEmployer;

    [DataField]
    public string Lifepath { get; set; } = SharedHumanoidAppearanceSystem.DefaultLifepath;
    // EE -- Contractors Change End

    [DataField]
    public string Customspeciename { get; set; } = "";

    [DataField]
    public float Height { get; private set; }

    [DataField]
    public float Width { get; private set; }

    [DataField]
    public int Age { get; set; } = 18;

    [DataField]
    public Sex Sex { get; private set; } = Sex.Male;

    [DataField]
    public int BankBalance { get; set; } = BankAccountComponent.StartingBalance;

    [DataField]
    public string? EmployedDepartment { get; set; } // NC

    [DataField]
    public Dictionary<string, DateTime> QuittedDepartments { get; set; } = new(); // NC

    // WD EDIT START
    [DataField]
    public string BodyType { get; set; } = SharedHumanoidAppearanceSystem.DefaultBodyType;

    [DataField]
    public string Voice { get; set; } = SharedHumanoidAppearanceSystem.DefaultVoice;

    [DataField]
    public string BarkVoice { get; set; } = SharedHumanoidAppearanceSystem.DefaultBarkVoice;

    [DataField]
    public BarkPercentageApplyData BarkSettings { get; set; } = BarkPercentageApplyData.Default;
    // WD EDIT END

    [DataField]
    public Gender Gender { get; private set; } = Gender.Male;

    [DataField]
    public string? DisplayPronouns { get; set; }

    [DataField]
    public string? StationAiName { get; set; }

    [DataField]
    public string? CyborgName { get; set; }

    // WD EDIT START
    [DataField]
    public string? ClownName { get; set; }

    [DataField]
    public string? MimeName { get; set; }
    // WD EDIT END

    [DataField]
    public List<NCStatEntry> Stats { get; set; } = new();

    [DataField]
    public List<NCSkillEntry> Skills { get; set; } = new();

    [DataField]
    public bool StatsAndSkillsLocked { get; set; }

    /// <see cref="Appearance"/>
    public ICharacterAppearance CharacterAppearance => Appearance;

    /// Stores markings, eye colors, etc for the profile
    [DataField]
    public HumanoidCharacterAppearance Appearance { get; set; } = new();

    /// When spawning into a round what's the preferred spot to spawn
    [DataField]
    public SpawnPriorityPreference SpawnPriority { get; private set; } = SpawnPriorityPreference.None;

    /// <see cref="_jobPriorities"/>
    public IReadOnlyDictionary<ProtoId<JobPrototype>, JobPriority> JobPriorities => _jobPriorities;

    /// <see cref="_antagPreferences"/>
    public IReadOnlySet<ProtoId<AntagPrototype>> AntagPreferences => _antagPreferences;

    /// <see cref="_traitPreferences"/>
    public IReadOnlySet<ProtoId<TraitPrototype>> TraitPreferences => _traitPreferences;

    /// If we're unable to get one of our preferred jobs do we spawn as a fallback job or do we stay in lobby
    [DataField]
    public PreferenceUnavailableMode PreferenceUnavailable { get; private set; } =
        PreferenceUnavailableMode.SpawnAsOverflow;

    public HumanoidCharacterProfile(
        string name,
        string flavortext,
        string species,
        string customspeciename,
        // EE -- Contractors Change Start
        string nationality,
        string employer,
        string lifepath,
        // EE -- Contractors Change End
        float height,
        float width,
        int age,
        Sex sex,
        int bankBalance, // NC
        string? employedDepartment, // NC
        string voice, // WD EDIT
        string barkVoice, // WD EDIT
        BarkPercentageApplyData barkSettings, // WD EDIT
        string bodyType, // WD EDIT
        Gender gender,
        string? displayPronouns,
        string? stationAiName,
        string? cyborgName,
        string? clownName, // WD EDIT
        string? mimeName, // WD EDIT
        List<NCStatEntry> stats, // NC
        List<NCSkillEntry> skills, // NC
        bool statsAndSkillsLocked, // NC
        HumanoidCharacterAppearance appearance,
        SpawnPriorityPreference spawnPriority,
        Dictionary<ProtoId<JobPrototype>, JobPriority> jobPriorities,
        PreferenceUnavailableMode preferenceUnavailable,
        HashSet<ProtoId<AntagPrototype>> antagPreferences,
        HashSet<ProtoId<TraitPrototype>> traitPreferences,
        Dictionary<string, Loadout> loadoutPreferences, // WWDP EDIT
        Dictionary<string, DateTime> quittedDepartments) // NC
    {
        Name = name;
        FlavorText = flavortext;
        Species = species;
        Customspeciename = customspeciename;
        // EE -- Contractors Change Start
        Nationality = nationality;
        Employer = employer;
        Lifepath = lifepath;
        // EE -- Contractors Change End
        Height = height;
        Width = width;
        Age = age;
        Sex = sex;
        BankBalance = bankBalance;
        EmployedDepartment = employedDepartment;
        QuittedDepartments = quittedDepartments; // NC
        Voice = voice; // WD EDIT
        BarkVoice = barkVoice; // WD EDIT
        BodyType = bodyType; // WD EDIT
        BarkSettings = barkSettings.Clone(); // WD EDIT
        Gender = gender;
        DisplayPronouns = displayPronouns;
        StationAiName = stationAiName;
        CyborgName = cyborgName;
        ClownName = clownName; // WD EDIT
        MimeName = mimeName; // WD EDIT
        Stats = CloneStats(stats);
        Skills = CloneSkills(skills);
        StatsAndSkillsLocked = statsAndSkillsLocked;
        Appearance = appearance;
        SpawnPriority = spawnPriority;
        _jobPriorities = jobPriorities;
        PreferenceUnavailable = preferenceUnavailable;
        _antagPreferences = antagPreferences;
        _traitPreferences = traitPreferences;
        _loadoutPreferences = loadoutPreferences;

        var hasHighPrority = false;
        foreach (var (key, value) in _jobPriorities)
        {
            if (value == JobPriority.Never)
                _jobPriorities.Remove(key);
            else if (value != JobPriority.High)
                continue;

            if (hasHighPrority)
                _jobPriorities[key] = JobPriority.Medium;

            hasHighPrority = true;
        }
    }

    /// <summary>Copy constructor</summary>
    public HumanoidCharacterProfile(HumanoidCharacterProfile other)
        : this(
            other.Name,
            other.FlavorText,
            other.Species,
            other.Customspeciename,
            // EE -- Contractors Change Start
            other.Nationality,
            other.Employer,
            other.Lifepath,
            // EE -- Contractors Change End
            other.Height,
            other.Width,
            other.Age,
            other.Sex,
            other.BankBalance, // NC
            other.EmployedDepartment, // NC
            other.Voice, // WD EDIT
            other.BarkVoice, // WD EDIT
            other.BarkSettings.Clone(), // WD EDIT
            other.BodyType, // WD EDIT
            other.Gender,
            other.DisplayPronouns,
            other.StationAiName,
            other.CyborgName,
            other.ClownName, // WD EDIT
            other.MimeName, // WD EDIT
            other.Stats,
            other.Skills,
            other.StatsAndSkillsLocked,
            other.Appearance.Clone(),
            other.SpawnPriority,
            new Dictionary<ProtoId<JobPrototype>, JobPriority>(other.JobPriorities),
            other.PreferenceUnavailable,
            new HashSet<ProtoId<AntagPrototype>>(other.AntagPreferences),
            new HashSet<ProtoId<TraitPrototype>>(other.TraitPreferences),
            new Dictionary<string, Loadout>(other.LoadoutPreferences), // WWDP EDIT
            new Dictionary<string, DateTime>(other.QuittedDepartments)) // NC
    {
    }

    /// <summary>
    ///     Get the default humanoid character profile, using internal constant values.
    ///     Defaults to <see cref="SharedHumanoidAppearanceSystem.DefaultSpecies"/> for the species.
    /// </summary>
    /// <returns></returns>
    public HumanoidCharacterProfile()
    {
    }

    /// <summary>
    ///     Return a default character profile, based on species.
    /// </summary>
    /// <param name="species">The species to use in this default profile. The default species is <see cref="SharedHumanoidAppearanceSystem.DefaultSpecies"/>.</param>
    /// <returns>Humanoid character profile with default settings.</returns>
    public static HumanoidCharacterProfile DefaultWithSpecies(string species = SharedHumanoidAppearanceSystem.DefaultSpecies)
    {
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var skinColor = SkinColor.ValidHumanSkinTone;

        if (prototypeManager.TryIndex<SpeciesPrototype>(species, out var speciesPrototype))
            skinColor = speciesPrototype.DefaultSkinTone;

        return new()
        {
            Species = species,
            Appearance = new()
            {
                SkinColor = skinColor,
            },
            Nationality = SharedHumanoidAppearanceSystem.DefaultNationality,
            Employer = SharedHumanoidAppearanceSystem.DefaultEmployer,
            Lifepath = SharedHumanoidAppearanceSystem.DefaultLifepath,
        };
    }

    // TODO: This should eventually not be a visual change only.
    public static HumanoidCharacterProfile Random(HashSet<string>? ignoredSpecies = null)
    {
        return RandomWithSpecies(SharedHumanoidAppearanceSystem.DefaultSpecies);
    }

    public static HumanoidCharacterProfile RandomWithSpecies(string species = SharedHumanoidAppearanceSystem.DefaultSpecies)
    {
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var random = IoCManager.Resolve<IRobustRandom>();

        var sex = Sex.Unsexed;
        var age = 18;
        var bodyType = SharedHumanoidAppearanceSystem.DefaultBodyType; // WD EDIT
        if (prototypeManager.TryIndex<SpeciesPrototype>(species, out var speciesPrototype))
        {
            sex = random.Pick(speciesPrototype.Sexes);
            age = random.Next(speciesPrototype.MinAge, speciesPrototype.OldAge); // people don't look and keep making 119 year old characters with zero rp, cap it at middle aged
            bodyType = speciesPrototype.BodyTypes.First(); // WD EDIT
        }

        var gender = Gender.Epicene;

        switch (sex)
        {
            case Sex.Male:
                gender = Gender.Male;
                break;
            case Sex.Female:
                gender = Gender.Female;
                break;
        }

        // WD EDIT START
        var voiceId = random.Pick(prototypeManager
            .EnumeratePrototypes<TTSVoicePrototype>()
            .Where(o => CanHaveVoice(o, sex)).ToArray()
        ).ID;
        // WD EDIT END

        var name = GetName(species, gender);

        var profile = new HumanoidCharacterProfile() // WD EDIT
        {
            Name = name,
            Sex = sex,
            Age = age,
            Gender = gender,
            Voice = voiceId, // WD EDIT
            BodyType = bodyType, // WD EDIT
            Species = species,
            Appearance = HumanoidCharacterAppearance.Random(species, sex),
            Nationality = SharedHumanoidAppearanceSystem.DefaultNationality,
            Employer = SharedHumanoidAppearanceSystem.DefaultEmployer,
            Lifepath = SharedHumanoidAppearanceSystem.DefaultLifepath,
        };

        // WD EDIT START
        var barkSystem = IoCManager.Resolve<IEntityManager>().System<SharedBarkSystem>();
        var barkVoiceList = barkSystem.GetVoiceList(profile);

        var barkVoice = SharedHumanoidAppearanceSystem.DefaultBarkVoice;
        if (barkVoiceList.Any())
        {
            barkVoice = random.Pick(barkVoiceList).ID;
        }

        profile.BarkVoice = barkVoice;

        return profile;
        // WD EDIT END
    }

    public HumanoidCharacterProfile WithName(string name) => new(this) { Name = name };
    public HumanoidCharacterProfile WithFlavorText(string flavorText) => new(this) { FlavorText = flavorText };
    public HumanoidCharacterProfile WithVoice(string voice) => new(this) { Voice = voice }; // WD EDIT
    public HumanoidCharacterProfile WithBodyType(string bodyType) => new(this) { BodyType = bodyType }; // WD EDIT
    public HumanoidCharacterProfile WithBarkVoice(string barkVoice, BarkPercentageApplyData setting) =>
        new(this) { BarkVoice = barkVoice, BarkSettings = setting.Clone() }; // WD EDIT

    public HumanoidCharacterProfile WithAge(int age) => new(this) { Age = age };
    public HumanoidCharacterProfile WithBankBalance(int bankBalance) => new(this) { BankBalance = bankBalance };
    public HumanoidCharacterProfile WithEmployedDepartment(string? employedDepartment) => new(this) { EmployedDepartment = employedDepartment }; // NC
    // EE - Contractors Change Start
    public HumanoidCharacterProfile WithNationality(string nationality) => new(this) { Nationality = nationality };
    public HumanoidCharacterProfile WithEmployer(string employer) => new(this) { Employer = employer };
    public HumanoidCharacterProfile WithLifepath(string lifepath) => new(this) { Lifepath = lifepath };
    // EE - Contractors Change End
    public HumanoidCharacterProfile WithSex(Sex sex) => new(this) { Sex = sex };
    public HumanoidCharacterProfile WithGender(Gender gender) => new(this) { Gender = gender };
    public HumanoidCharacterProfile WithDisplayPronouns(string? displayPronouns) => new(this) { DisplayPronouns = displayPronouns };
    public HumanoidCharacterProfile WithStationAiName(string? stationAiName) => new(this) { StationAiName = stationAiName };
    public HumanoidCharacterProfile WithCyborgName(string? cyborgName) => new(this) { CyborgName = cyborgName };
    public HumanoidCharacterProfile WithClownName(string? clownName) => new(this) { ClownName = clownName }; // WD EDIT
    public HumanoidCharacterProfile WithMimeName(string? mimeName) => new(this) { MimeName = mimeName }; // WD EDIT
    public HumanoidCharacterProfile WithStats(List<NCStatEntry> stats) => new(this) { Stats = CloneStats(stats) };
    public HumanoidCharacterProfile WithSkills(List<NCSkillEntry> skills) => new(this) { Skills = CloneSkills(skills) };
    public HumanoidCharacterProfile WithStatsAndSkillsLocked(bool locked) => new(this) { StatsAndSkillsLocked = locked };
    public HumanoidCharacterProfile WithSpecies(string species) => new(this) { Species = species };
    public HumanoidCharacterProfile WithCustomSpeciesName(string customspeciename) => new(this) { Customspeciename = customspeciename };
    public HumanoidCharacterProfile WithHeight(float height) => new(this) { Height = height };
    public HumanoidCharacterProfile WithWidth(float width) => new(this) { Width = width };

    public HumanoidCharacterProfile WithCharacterAppearance(HumanoidCharacterAppearance appearance) =>
        new(this) { Appearance = appearance };

    public HumanoidCharacterProfile WithSpawnPriorityPreference(SpawnPriorityPreference spawnPriority) =>
        new(this) { SpawnPriority = spawnPriority };

    public HumanoidCharacterProfile WithJobPriorities(IEnumerable<KeyValuePair<ProtoId<JobPrototype>, JobPriority>> jobPriorities)
    {
        var dictionary = new Dictionary<ProtoId<JobPrototype>, JobPriority>(jobPriorities);
        var hasHighPrority = false;

        foreach (var (key, value) in dictionary)
        {
            if (value == JobPriority.Never)
                dictionary.Remove(key);
            else if (value != JobPriority.High)
                continue;

            if (hasHighPrority)
                dictionary[key] = JobPriority.Medium;

            hasHighPrority = true;
        }

        return new(this)
        {
            _jobPriorities = dictionary
        };
    }

    public HumanoidCharacterProfile WithJobPriority(ProtoId<JobPrototype> jobId, JobPriority priority)
    {
        var dictionary = new Dictionary<ProtoId<JobPrototype>, JobPriority>(_jobPriorities);
        if (priority == JobPriority.Never)
            dictionary.Remove(jobId);
        else if (priority == JobPriority.High)
        {
            // There can only ever be one high priority job.
            foreach (var (job, value) in dictionary)
            {
                if (value == JobPriority.High)
                    dictionary[job] = JobPriority.Medium;
            }

            dictionary[jobId] = priority;
        }
        else
            dictionary[jobId] = priority;

        return new(this) { _jobPriorities = dictionary };
    }

    public HumanoidCharacterProfile WithPreferenceUnavailable(PreferenceUnavailableMode mode) =>
        new(this) { PreferenceUnavailable = mode };
    public HumanoidCharacterProfile WithAntagPreferences(IEnumerable<ProtoId<AntagPrototype>> antagPreferences) =>
        new(this) { _antagPreferences = new HashSet<ProtoId<AntagPrototype>>(antagPreferences) };

    public HumanoidCharacterProfile WithAntagPreference(ProtoId<AntagPrototype> antagId, bool pref)
    {
        var list = new HashSet<ProtoId<AntagPrototype>>(_antagPreferences);
        if (pref)
            list.Add(antagId);
        else
            list.Remove(antagId);

        return new(this) { _antagPreferences = list };
    }

    public HumanoidCharacterProfile WithTraitPreference(ProtoId<TraitPrototype> traitId, bool pref)
    {
        var list = new HashSet<ProtoId<TraitPrototype>>(_traitPreferences);

        if (pref)
            list.Add(traitId);
        else
            list.Remove(traitId);

        return new(this) { _traitPreferences = list };
    }

    // WWDP EDIT START
    // I'll rip the hands off whoever coded this piece of shit named Loadouts
    public HumanoidCharacterProfile WithLoadoutPreference(List<Loadout> loadouts)
    {
        var dictionary = loadouts.ToDictionary(p => p.LoadoutName);
        return new(this) { _loadoutPreferences = dictionary };
    }
    // WWDP EDIT END

    public string Summary =>
        Loc.GetString(
            "humanoid-character-profile-summary",
            ("name", Name),
            ("gender", Gender.ToString().ToLowerInvariant()),
            ("age", Age)
        );

    public bool MemberwiseEquals(ICharacterProfile maybeOther)
    {
        return maybeOther is HumanoidCharacterProfile other
            && Name == other.Name
            && Age == other.Age
            && Sex == other.Sex
            && BankBalance == other.BankBalance
            && EmployedDepartment == other.EmployedDepartment // NC
            && QuittedDepartments.SequenceEqual(other.QuittedDepartments) // NC
            && Voice == other.Voice // WD EDIT
            && BarkVoice == other.BarkVoice // WD EDIT
            && BodyType == other.BodyType // WD EDIT
            && Gender == other.Gender
            && Species == other.Species
            // EE - Contractors Change Start
            && Nationality == other.Nationality
            && Employer == other.Employer
            && Lifepath == other.Lifepath
            // EE - Contractors Change End
            && PreferenceUnavailable == other.PreferenceUnavailable
            && SpawnPriority == other.SpawnPriority
            && _jobPriorities.SequenceEqual(other._jobPriorities)
            && _antagPreferences.SequenceEqual(other._antagPreferences)
            && _traitPreferences.SequenceEqual(other._traitPreferences)
            && LoadoutPreferences.SequenceEqual(other.LoadoutPreferences)
            && StatsEqual(Stats, other.Stats)
            && SkillsEqual(Skills, other.Skills)
            && StatsAndSkillsLocked == other.StatsAndSkillsLocked
            && Appearance.MemberwiseEquals(other.Appearance)
            && FlavorText == other.FlavorText;
    }

    public void EnsureValid(ICommonSession session, IDependencyCollection collection)
    {
        var configManager = collection.Resolve<IConfigurationManager>();
        var prototypeManager = collection.Resolve<IPrototypeManager>();

        // NC - Force species to human
        Species = SharedHumanoidAppearanceSystem.DefaultSpecies;
        var speciesPrototype = prototypeManager.Index(Species);
        // NC - End

        var sex = Sex switch
        {
            Sex.Male => Sex.Male,
            Sex.Female => Sex.Female,
            Sex.Unsexed => Sex.Unsexed,
            _ => Sex.Male // Invalid enum values.
        };

        // ensure the species can be that sex and their age fits the founds
        if (!speciesPrototype.Sexes.Contains(sex))
        {
            sex = speciesPrototype.Sexes[0];
        }

        var age = Math.Clamp(Age, speciesPrototype.MinAge, speciesPrototype.MaxAge);

        var gender = Gender switch
        {
            Gender.Epicene => Gender.Epicene,
            Gender.Female => Gender.Female,
            Gender.Male => Gender.Male,
            Gender.Neuter => Gender.Neuter,
            _ => Gender.Epicene // Invalid enum values.
        };

        var bodyType = speciesPrototype.BodyTypes.Contains(BodyType) ? BodyType : speciesPrototype.BodyTypes.First(); // WD EDIT

        string name;
        if (string.IsNullOrEmpty(Name))
        {
            name = GetName(Species, gender);
        }
        else if (Name.Length > MaxNameLength)
        {
            name = Name[..MaxNameLength];
        }
        else
        {
            name = Name;
        }

        name = name.Trim();

        if (configManager.GetCVar(CCVars.RestrictedNames))
        {
            name = RestrictedNameRegex.Replace(name, string.Empty);
        }

        if (configManager.GetCVar(CCVars.ICNameCase))
        {
            // This regex replaces the first character of the first and last words of the name with their uppercase version
            name = ICNameCaseRegex.Replace(name, m => m.Groups["word"].Value.ToUpper());
        }

        var customspeciename =
            !speciesPrototype.CustomName
            || string.IsNullOrEmpty(Customspeciename)
                ? ""
                : Customspeciename.Length > MaxNameLength
                    ? FormattedMessage.RemoveMarkupPermissive(Customspeciename)[..MaxNameLength]
                    : FormattedMessage.RemoveMarkupPermissive(Customspeciename);

        if (string.IsNullOrEmpty(name))
        {
            name = GetName(Species, gender);
        }

        string flavortext;
        if (FlavorText.Length > MaxDescLength)
        {
            flavortext = FormattedMessage.RemoveMarkupPermissive(FlavorText)[..MaxDescLength];
        }
        else
        {
            flavortext = FormattedMessage.RemoveMarkupPermissive(FlavorText);
        }

        var appearance = HumanoidCharacterAppearance.EnsureValid(Appearance, Species, Sex);

        var prefsUnavailableMode = PreferenceUnavailable switch
        {
            PreferenceUnavailableMode.StayInLobby => PreferenceUnavailableMode.StayInLobby,
            PreferenceUnavailableMode.SpawnAsOverflow => PreferenceUnavailableMode.SpawnAsOverflow,
            _ => PreferenceUnavailableMode.StayInLobby // Invalid enum values.
        };

        var spawnPriority = SpawnPriority switch
        {
            SpawnPriorityPreference.None => SpawnPriorityPreference.None,
            SpawnPriorityPreference.Arrivals => SpawnPriorityPreference.Arrivals,
            SpawnPriorityPreference.Cryosleep => SpawnPriorityPreference.Cryosleep,
            _ => SpawnPriorityPreference.None // Invalid enum values.
        };

        var civilianDepartments = new[] { "Civilian", "CivilianNC" };
        var departmentPrototypes = prototypeManager.EnumeratePrototypes<DepartmentPrototype>().ToList();

        var priorities = new Dictionary<ProtoId<JobPrototype>, JobPriority>(JobPriorities
            .Where(p =>
            {
                if (!prototypeManager.TryIndex<JobPrototype>(p.Key, out var job) || !job.SetPreference)
                    return false;

                // NC START
                if (EmployedDepartment != null)
                {
                    // Only allow jobs from the employed department
                    if (!departmentPrototypes.Any(d => d.ID == EmployedDepartment && d.Roles.Contains(p.Key)))
                        return false;
                }
                else
                {
                    // If not employed, only allow civilian jobs
                    // We assume civilian jobs are those that belong to a department in CivilianDepartments
                    if (!departmentPrototypes.Any(d => civilianDepartments.Contains(d.ID) && d.Roles.Contains(p.Key)))
                        return false;
                }
                // NC END

                return p.Value switch
                {
                    JobPriority.Never => false,
                    JobPriority.Low => true,
                    JobPriority.Medium => true,
                    JobPriority.High => true,
                    _ => false
                };
            }));

        var hasHighPrio = false;
        foreach (var (key, value) in priorities)
        {
            if (value != JobPriority.High)
                continue;

            if (hasHighPrio)
                priorities[key] = JobPriority.Medium;
            hasHighPrio = true;
        }

        var antags = AntagPreferences
            .Where(id => prototypeManager.TryIndex(id, out var antag) && antag.SetPreference)
            .Distinct()
            .ToList();

        var traits = TraitPreferences
            .Where(prototypeManager.HasIndex)
            .Distinct()
            .ToList();

        var loadouts = LoadoutPreferences
            .Where(l => prototypeManager.HasIndex<LoadoutPrototype>(l.Key))
            .ToList();

        var stats = FitStatsToBudget(EnsureStats(prototypeManager), prototypeManager);
        var skills = FitSkillsToBudget(EnsureSkills(prototypeManager), prototypeManager);

        Name = name;
        Customspeciename = customspeciename;
        FlavorText = flavortext;
        Age = age;
        Sex = sex;
        Gender = gender;
        BodyType = bodyType; // WD EDIT
        Appearance = appearance;
        SpawnPriority = spawnPriority;

        _jobPriorities.Clear();

        foreach (var (job, priority) in priorities)
        {
            _jobPriorities.Add(job, priority);
        }

        PreferenceUnavailable = prefsUnavailableMode;

        _antagPreferences.Clear();
        _antagPreferences.UnionWith(antags);

        _traitPreferences.Clear();
        _traitPreferences.UnionWith(traits);

        _loadoutPreferences.Clear();

        // WD EDIT START
        prototypeManager.TryIndex<TTSVoicePrototype>(Voice, out var voice);
        if (voice is null || !CanHaveVoice(voice, Sex))
            Voice = SharedHumanoidAppearanceSystem.DefaultSexVoice[sex];

        if (!CanHaveBark(prototypeManager, collection))
            BarkVoice = SharedHumanoidAppearanceSystem.DefaultBarkVoice;

        foreach (var (key, loadout) in loadouts)
        {
            if (loadout.CustomContent is not { Length: > MaxCustomContentLength, })
            {
                _loadoutPreferences[key] = loadout;
                continue;
            }
            var truncated = loadout.CustomContent.AsSpan(0, MaxCustomContentLength);
            while (truncated.Length > 0 && char.IsLowSurrogate(truncated[^1]))
                truncated = truncated[..^1];

            var truncatedLoadout = new Loadout(
                    loadout.LoadoutName,
                    loadout.CustomName,
                    loadout.CustomDescription,
                    truncated.ToString(),
                    loadout.CustomColorTint,
                    loadout.CustomHeirloom);

                _loadoutPreferences[key] = truncatedLoadout;
        }
        // WD EDIT END

        Stats = stats;
        Skills = skills;
    }

    // WD EDIT START
    public static bool CanHaveVoice(TTSVoicePrototype voice, Sex sex)
    {
        return voice.RoundStart && sex == Sex.Unsexed || voice.Sex == sex || voice.Sex == Sex.Unsexed;
    }

    public bool CanHaveBark(
        IPrototypeManager prototypeManager, IDependencyCollection collection,
        ProtoId<BarkListPrototype>? id = null
    )
    {
        var voice = BarkVoice;
        if (
            !prototypeManager.TryIndex<BarkListPrototype>(id ?? "default", out var barkList) ||
            !barkList.VoiceList.TryGetValue(voice, out var voiceRequirements) ||
            !prototypeManager.TryIndex<BarkVoicePrototype>(voice, out var voicePrototype))
        {
            return false;
        }

        var isValid = true;
        var reason = "";

        foreach (var requirement in voiceRequirements)
        {
            var passes = requirement.IsValid(
                default!,
                this,
                new Dictionary<string, TimeSpan>(),
                false,
                voicePrototype,
                collection.Resolve<IEntityManager>(),
                prototypeManager,
                collection.Resolve<IConfigurationManager>(),
                out reason);

            if (passes == !requirement.Inverted)
                continue;

            isValid = false;
            break;
        }

        return isValid;
    }
    // WD EDIT END

    public ICharacterProfile Validated(ICommonSession session, IDependencyCollection collection)
    {
        var profile = new HumanoidCharacterProfile(this);
        profile.EnsureValid(session, collection);
        return profile;
    }

    // Sorry this is kind of weird and duplicated,
    // Working inside these non entity systems is a bit wack
    public static string GetName(string species, Gender gender)
    {
        var namingSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<NamingSystem>();
        return namingSystem.GetName(species, gender);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is HumanoidCharacterProfile other && MemberwiseEquals(other);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(_jobPriorities);
        hashCode.Add(_antagPreferences);
        hashCode.Add(_traitPreferences);
        hashCode.Add(_loadoutPreferences);
        hashCode.Add(Name);
        hashCode.Add(FlavorText);
        hashCode.Add(Species);
        hashCode.Add(Employer);
        hashCode.Add(Nationality);
        hashCode.Add(Lifepath);
        hashCode.Add(Age);
        hashCode.Add((int) Sex);
        hashCode.Add(EmployedDepartment); // NC
        hashCode.Add((int) Gender);
        hashCode.Add(Voice); // WD EDIT
        hashCode.Add(BodyType); // WD EDIT
        hashCode.Add(BarkVoice); // WD EDIT
        hashCode.Add(BarkSettings); // WD EDIT
        hashCode.Add(Appearance);
        hashCode.Add((int) SpawnPriority);
        hashCode.Add((int) PreferenceUnavailable);
        hashCode.Add(Customspeciename);
        hashCode.Add(StatsAndSkillsLocked);

        foreach (var stat in Stats)
        {
            hashCode.Add(stat.StatId);
            hashCode.Add(stat.Value.BaseValue);
            hashCode.Add(stat.Value.ProgressionValue);
            hashCode.Add(stat.Value.TemporaryValue);
            hashCode.Add(stat.Value.FinalValue);
        }

        foreach (var skill in Skills)
        {
            hashCode.Add(skill.SkillId);
            hashCode.Add(skill.Value.BaseValue);
            hashCode.Add(skill.Value.ProgressionValue);
            hashCode.Add(skill.Value.TemporaryValue);
            hashCode.Add(skill.Value.FinalValue);
        }

        return hashCode.ToHashCode();
    }

    public HumanoidCharacterProfile Clone()
    {
        return new HumanoidCharacterProfile(this);
    }

    public int GetSpentStatPoints()
    {
        return Stats.Sum(stat => Math.Max(0, stat.Value.BaseValue - 1));
    }

    public int GetSpentSkillPoints(IPrototypeManager prototypeManager)
    {
        var spent = 0;

        foreach (var skill in Skills)
        {
            if (!prototypeManager.TryIndex<NCSkillPrototype>(skill.SkillId, out var proto))
                continue;

            spent += skill.Value.BaseValue * proto.CostMultiplier;
        }

        return spent;
    }

    private static List<NCStatEntry> CloneStats(IEnumerable<NCStatEntry> stats)
    {
        return stats
            .Select(s => new NCStatEntry(s.StatId,
                new NCTrackedValue
                {
                    BaseValue = s.Value.BaseValue,
                    ProgressionValue = s.Value.ProgressionValue,
                    TemporaryValue = s.Value.TemporaryValue,
                    FinalValue = s.Value.FinalValue
                }))
            .ToList();
    }

    private static List<NCSkillEntry> CloneSkills(IEnumerable<NCSkillEntry> skills)
    {
        return skills
            .Select(s => new NCSkillEntry(s.SkillId,
                new NCTrackedValue
                {
                    BaseValue = s.Value.BaseValue,
                    ProgressionValue = s.Value.ProgressionValue,
                    TemporaryValue = s.Value.TemporaryValue,
                    FinalValue = s.Value.FinalValue
                },
                s.Specialization))
            .ToList();
    }

    private static bool StatsEqual(IReadOnlyList<NCStatEntry> left, IReadOnlyList<NCStatEntry> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var l = left[i];
            var r = right[i];
            if (l.StatId != r.StatId)
                return false;

            if (l.Value.BaseValue != r.Value.BaseValue ||
                l.Value.ProgressionValue != r.Value.ProgressionValue ||
                l.Value.TemporaryValue != r.Value.TemporaryValue ||
                l.Value.FinalValue != r.Value.FinalValue)
                return false;
        }

        return true;
    }

    private static bool SkillsEqual(IReadOnlyList<NCSkillEntry> left, IReadOnlyList<NCSkillEntry> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var l = left[i];
            var r = right[i];
            if (l.SkillId != r.SkillId)
                return false;

            if (l.Value.BaseValue != r.Value.BaseValue ||
                l.Value.ProgressionValue != r.Value.ProgressionValue ||
                l.Value.TemporaryValue != r.Value.TemporaryValue ||
                l.Value.FinalValue != r.Value.FinalValue)
                return false;
        }

        return true;
    }

    private List<NCStatEntry> EnsureStats(IPrototypeManager prototypeManager)
    {
        var existing = new List<NCStatEntry>();
        var byId = new Dictionary<string, NCStatEntry>();

        foreach (var stat in CloneStats(Stats))
        {
            if (!prototypeManager.TryIndex<NCStatPrototype>(stat.StatId, out var proto))
                continue;

            if (byId.ContainsKey(stat.StatId))
                continue;

            stat.Value.BaseValue = Math.Clamp(stat.Value.BaseValue, proto.MinValue, proto.MaxValue);
            stat.Value.ProgressionValue = Math.Max(0, stat.Value.ProgressionValue);
            stat.Value.FinalValue = Math.Clamp(
                stat.Value.BaseValue + stat.Value.ProgressionValue + stat.Value.TemporaryValue,
                proto.MinValue,
                proto.MaxValue);

            byId.Add(stat.StatId, stat);
            existing.Add(stat);
        }

        foreach (var proto in prototypeManager.EnumeratePrototypes<NCStatPrototype>().OrderBy(p => p.ID))
        {
            if (!byId.TryGetValue(proto.ID, out var entry))
            {
                entry = new NCStatEntry(proto.ID, new NCTrackedValue(proto.MinValue));
                byId[proto.ID] = entry;
                existing.Add(entry);
            }

            entry.Value.BaseValue = Math.Clamp(entry.Value.BaseValue, proto.MinValue, proto.MaxValue);
            entry.Value.ProgressionValue = Math.Max(0, entry.Value.ProgressionValue);
            entry.Value.FinalValue = Math.Clamp(
                entry.Value.BaseValue + entry.Value.ProgressionValue + entry.Value.TemporaryValue,
                proto.MinValue,
                proto.MaxValue);
        }

        return existing
            .Where(s => prototypeManager.HasIndex<NCStatPrototype>(s.StatId))
            .OrderBy(s => s.StatId)
            .ToList();
    }

    private List<NCSkillEntry> EnsureSkills(IPrototypeManager prototypeManager)
    {
        var existing = new List<NCSkillEntry>();
        var seen = new HashSet<string>();

        foreach (var skill in CloneSkills(Skills))
        {
            if (!prototypeManager.TryIndex<NCSkillPrototype>(skill.SkillId, out var proto))
                continue;

            skill.Specialization = null;

            if (!seen.Add(skill.SkillId))
                continue;

            skill.Value.BaseValue = Math.Clamp(skill.Value.BaseValue, proto.MinValue, proto.MaxValue);
            skill.Value.ProgressionValue = Math.Max(0, skill.Value.ProgressionValue);
            skill.Value.FinalValue = Math.Clamp(
                skill.Value.BaseValue + skill.Value.ProgressionValue + skill.Value.TemporaryValue,
                proto.MinValue,
                proto.MaxValue);
            existing.Add(skill);
        }

        foreach (var proto in prototypeManager.EnumeratePrototypes<NCSkillPrototype>().OrderBy(p => p.ID))
        {
            if (seen.Contains(proto.ID))
                continue;

            existing.Add(new NCSkillEntry(proto.ID, new NCTrackedValue(0)));
        }

        return existing
            .OrderBy(s => s.SkillId)
            .ToList();
    }

    private static List<NCStatEntry> FitStatsToBudget(List<NCStatEntry> stats, IPrototypeManager prototypeManager)
    {
        var spent = stats.Sum(stat => Math.Max(0, stat.Value.BaseValue - 1));
        while (spent > StartingStatPoints)
        {
            var changed = false;
            foreach (var stat in stats.OrderByDescending(s => s.Value.BaseValue).ThenBy(s => s.StatId))
            {
                var proto = prototypeManager.Index<NCStatPrototype>(stat.StatId);
                if (stat.Value.BaseValue <= proto.MinValue)
                    continue;

                // Reduce overflow deterministically so an invalid client payload cannot exceed the creation budget.
                stat.Value.BaseValue--;
                stat.Value.FinalValue = Math.Clamp(
                    stat.Value.BaseValue + stat.Value.ProgressionValue + stat.Value.TemporaryValue,
                    proto.MinValue,
                    proto.MaxValue);
                spent--;
                changed = true;

                if (spent <= StartingStatPoints)
                    break;
            }

            if (!changed)
                break;
        }

        return stats;
    }

    private static List<NCSkillEntry> FitSkillsToBudget(List<NCSkillEntry> skills, IPrototypeManager prototypeManager)
    {
        var spent = skills.Sum(skill =>
            prototypeManager.TryIndex<NCSkillPrototype>(skill.SkillId, out var proto)
                ? skill.Value.BaseValue * proto.CostMultiplier
                : 0);

        while (spent > StartingSkillPoints)
        {
            var changed = false;
            foreach (var skill in skills
                         .OrderByDescending(s => s.Value.BaseValue)
                         .ThenByDescending(s => prototypeManager.Index<NCSkillPrototype>(s.SkillId).CostMultiplier)
                         .ThenBy(s => s.SkillId))
            {
                var proto = prototypeManager.Index<NCSkillPrototype>(skill.SkillId);
                if (skill.Value.BaseValue <= proto.MinValue)
                    continue;

                // Skill costs can vary, so we refund budget using the prototype multiplier of the exact entry.
                skill.Value.BaseValue--;
                skill.Value.FinalValue = Math.Clamp(
                    skill.Value.BaseValue + skill.Value.ProgressionValue + skill.Value.TemporaryValue,
                    proto.MinValue,
                    proto.MaxValue);
                spent -= proto.CostMultiplier;
                changed = true;

                if (spent <= StartingSkillPoints)
                    break;
            }

            if (!changed)
                break;
        }

        return skills;
    }
}
