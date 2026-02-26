namespace SimpleDEX.Offchain.Models;

public record OrderDto(
    string OutRef,
    string OwnerAddress,
    string OfferSubject,
    string AskSubject,
    ulong Price,
    ulong Slot,
    string Status,
    ulong? SpentSlot
);
