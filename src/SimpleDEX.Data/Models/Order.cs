using Argus.Sync.Data.Models;

namespace SimpleDEX.Data.Models;

public record Order(
    string OutRef,
    string OwnerPkh,
    string DestinationAddress,
    string OfferSubject,
    string AskSubject,
    ulong OfferAmount,
    ulong PriceNum,
    ulong PriceDen,
    string ScriptHash,
    ulong Slot,
    OrderStatus Status,
    ulong? SpentSlot
) : IReducerModel;
