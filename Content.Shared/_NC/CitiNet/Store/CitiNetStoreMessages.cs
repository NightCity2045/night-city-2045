using Content.Shared._NC.CitiNet.Delivery;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._NC.CitiNet.Store;

[Serializable, NetSerializable]
public sealed class CitiNetStoreUpdateState : BoundUserInterfaceState
{
    public int Balance { get; }
    public int DataBalance { get; }
    public bool UsesCorporateAccount { get; }
    public DropType DeliveryType { get; }
    public List<CitiNetStoreCategoryData> Categories { get; }
    public List<CitiNetStoreCartEntryData> CartEntries { get; }
    public int CartTotalPrice { get; }
    public int CartTotalDataPrice { get; }
    public bool CanCheckout { get; }

    public CitiNetStoreUpdateState(
        int balance,
        int dataBalance,
        bool usesCorporateAccount,
        DropType deliveryType,
        List<CitiNetStoreCategoryData> categories,
        List<CitiNetStoreCartEntryData> cartEntries,
        int cartTotalPrice,
        int cartTotalDataPrice,
        bool canCheckout)
    {
        Balance = balance;
        DataBalance = dataBalance;
        UsesCorporateAccount = usesCorporateAccount;
        DeliveryType = deliveryType;
        Categories = categories;
        CartEntries = cartEntries;
        CartTotalPrice = cartTotalPrice;
        CartTotalDataPrice = cartTotalDataPrice;
        CanCheckout = canCheckout;
    }
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreCategoryData
{
    public string Name { get; }
    public List<CitiNetStoreEntryData> Entries { get; }

    public CitiNetStoreCategoryData(string name, List<CitiNetStoreEntryData> entries)
    {
        Name = name;
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreEntryData
{
    public string Id { get; }
    public string ProtoId { get; }
    public string Name { get; }
    public string Description { get; }
    public int Price { get; }
    public int DataPrice { get; }
    public int? RemainingCount { get; }

    public CitiNetStoreEntryData(string id, string protoId, string name, string description, int price, int dataPrice, int? remainingCount)
    {
        Id = id;
        ProtoId = protoId;
        Name = name;
        Description = description;
        Price = price;
        DataPrice = dataPrice;
        RemainingCount = remainingCount;
    }
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreCartEntryData
{
    public string CategoryId { get; }
    public string ProtoId { get; }
    public string Name { get; }
    public int Amount { get; }
    public int TotalPrice { get; }
    public int TotalDataPrice { get; }

    public CitiNetStoreCartEntryData(string categoryId, string protoId, string name, int amount, int totalPrice, int totalDataPrice)
    {
        CategoryId = categoryId;
        ProtoId = protoId;
        Name = name;
        Amount = amount;
        TotalPrice = totalPrice;
        TotalDataPrice = totalDataPrice;
    }
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreBuyRequestMessage : BoundUserInterfaceMessage
{
    public string CategoryId { get; }
    public string EntryProtoId { get; }
    public int Amount { get; }

    public CitiNetStoreBuyRequestMessage(string categoryId, string entryProtoId, int amount = 1)
    {
        CategoryId = categoryId;
        EntryProtoId = entryProtoId;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreRequestDataMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreRemoveFromCartMessage : BoundUserInterfaceMessage
{
    public string CategoryId { get; }
    public string EntryProtoId { get; }
    public int Amount { get; }

    public CitiNetStoreRemoveFromCartMessage(string categoryId, string entryProtoId, int amount = 1)
    {
        CategoryId = categoryId;
        EntryProtoId = entryProtoId;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreClearCartMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CitiNetStoreCheckoutCartMessage : BoundUserInterfaceMessage
{
}
