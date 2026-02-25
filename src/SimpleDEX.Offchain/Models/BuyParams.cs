namespace SimpleDEX.Offchain.Models;

public record BuyRequest(
    string BuyerAddress,
    string SellerAddress,
    string OrderOutRef,
    string AskSubject,
    ulong AskPrice);

public record BuyResponse(
    string UnsignedTxCborHex);
