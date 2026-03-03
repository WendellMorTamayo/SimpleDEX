namespace SimpleDEX.Offchain.Models;

public record OrderItem(
    string OfferSubject,
    ulong OfferAmount,
    string AskSubject,
    ulong PriceNum,
    ulong PriceDen);

public record OrderRequest(
    string ChangeAddress,
    string ScriptHash,
    List<OrderItem> Orders);

public record OrderResponse(
    string UnsignedTxCborHex);
