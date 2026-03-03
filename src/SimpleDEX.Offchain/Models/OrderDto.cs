namespace SimpleDEX.Offchain.Models;

public record OrderDto(
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
    string Status,
    ulong? SpentSlot
);
