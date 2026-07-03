using Content.Shared.DoAfter;
using Content.Shared.Storage;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.VendingMachines;

/// <summary>
/// Entry for partial vending machine restock with optional pricing.
/// Extends EntitySpawnEntry functionality with price support.
/// </summary>
[Serializable, DataDefinition]
public partial struct PartialRestockEntry
{
    [DataField("id")]
    public EntProtoId? PrototypeId = null;

    /// <summary>
    ///     The probability that an item will spawn. Takes decimal form so 0.05 is 5%, 0.50 is 50% etc.
    /// </summary>
    [DataField("prob")] public float SpawnProbability = 1;

    /// <summary>
    ///     orGroup signifies to pick between entities designated with an ID.
    /// </summary>
    [DataField("orGroup")] public string? GroupId = null;

    [DataField] public int Amount = 1;

    /// <summary>
    ///     How many of this can be spawned, in total.
    ///     If this is lesser or equal to <see cref="Amount"/>, it will spawn <see cref="Amount"/> exactly.
    ///     Otherwise, it chooses a random value between <see cref="Amount"/> and <see cref="MaxAmount"/> on spawn.
    /// </summary>
    [DataField] public int MaxAmount = 1;

    /// <summary>
    /// Price to set for this item if it's not already in the vending machine's inventory.
    /// If the item already exists, the existing price will be used instead.
    /// </summary>
    [DataField] public uint Price = 0;

    public PartialRestockEntry() { }
}

public static class PartialRestockEntryExtensions
{
    /// <summary>
    /// Gets the amount to spawn for this entry, handling randomization between Amount and MaxAmount.
    /// </summary>
    public static double GetAmount(this PartialRestockEntry entry, IRobustRandom? random = null, bool getAverage = false)
    {
        // Max amount is less or equal than amount, so just return the amount
        if (entry.MaxAmount <= entry.Amount)
            return entry.Amount;

        // If we want the average, just calculate the expected amount
        if (getAverage)
            return (entry.Amount + entry.MaxAmount) / 2.0;

        // Otherwise get a random value in between
        IoCManager.Resolve(ref random);
        return random.Next(entry.Amount, entry.MaxAmount);
    }

    /// <summary>
    /// Gets the amount to spawn for this entry using System.Random.
    /// </summary>
    public static double GetAmount(this PartialRestockEntry entry, System.Random random, bool getAverage = false)
    {
        // Max amount is less or equal than amount, so just return the amount
        if (entry.MaxAmount <= entry.Amount)
            return entry.Amount;

        // If we want the average, just calculate the expected amount
        if (getAverage)
            return (entry.Amount + entry.MaxAmount) / 2.0;

        // Otherwise get a random value in between
        return random.Next(entry.Amount, entry.MaxAmount);
    }
}

[RegisterComponent, NetworkedComponent, Access(typeof(SharedVendingMachineSystem))]
public sealed partial class VendingMachineRestockPartialComponent : Component
{
    /// <summary>
    /// The time (in seconds) that it takes to restock a machine.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("restockDelay")]
    public TimeSpan RestockDelay = TimeSpan.FromSeconds(5.0f);

    /// <summary>
    /// What sort of machine inventory does this restock?
    /// This is checked against the VendingMachineComponent's pack value.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("canRestock", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<VendingMachineInventoryPrototype>))]
    public HashSet<string> CanRestock = new();

    /// <summary>
    /// The contents that this restock box will add to the vending machine.
    /// Similar to StorageFill component but with pricing support.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("contents")]
    public List<PartialRestockEntry> Contents = new();

    /// <summary>
    /// Sound that plays when starting to restock a machine.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("soundRestockStart")]
    public SoundSpecifier SoundRestockStart = new SoundPathSpecifier("/Audio/Machines/vending_restock_start.ogg")
    {
        Params = new AudioParams
        {
            Volume = -2f,
            Variation = 0.2f
        }
    };

    /// <summary>
    /// Sound that plays when finished restocking a machine.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("soundRestockDone")]
    public SoundSpecifier SoundRestockDone = new SoundPathSpecifier("/Audio/Machines/vending_restock_done.ogg");
}
