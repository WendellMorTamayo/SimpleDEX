namespace SimpleDEX.Offchain.Models;

public record BuyOrderRequest(
    string OutRef,
    ulong? Amount = null);

public record BuyRequest(
    string BuyerAddress,
    List<BuyOrderRequest> Orders);

public record BuyResponse(
    string UnsignedTxCborHex);
