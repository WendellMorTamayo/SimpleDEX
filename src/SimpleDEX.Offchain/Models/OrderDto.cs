namespace SimpleDEX.Offchain.Models;

public record OrderDto(
    string OutRef,
    string OwnerPkh,
    string DestinationAddress,
    string OfferSubject,
    string AskSubject,
    ulong Price,
    ulong Slot,
    string Status,
    ulong? SpentSlot
);
