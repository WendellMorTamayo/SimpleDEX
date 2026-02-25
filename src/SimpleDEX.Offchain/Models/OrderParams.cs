namespace SimpleDEX.Offchain.Models;

public record OrderRequest(
    string ChangeAddress,
    string OfferSubject,
    ulong OfferAmount,
    string AskSubject,
    ulong AskPrice);

public record OrderResponse(
    string UnsignedTxCborHex);
