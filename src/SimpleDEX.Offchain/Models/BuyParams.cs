namespace SimpleDEX.Offchain.Models;

public record BuyRequest(
    string OwnerAddress,
    string OrderOutRef,
    string AskSubject,
    ulong AskPrice);

public record BuyResponse(
    string UnsignedTxCborHex);
