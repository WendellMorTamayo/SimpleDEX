using Argus.Sync.Data.Models;

namespace SimpleDEX.Data.Models;

public record Order(
    string OutRef,
    string OwnerAddress,
    string OfferSubject,
    string AskSubject,
    ulong Price,
    ulong Slot,
    OrderStatus Status,
    ulong? SpentSlot
) : IReducerModel;
