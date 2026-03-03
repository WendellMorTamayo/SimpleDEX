namespace SimpleDEX.Offchain.Models;

public record CancelOrderRef(
    string TxHash,
    ulong Index);

public record CancelRequest(
    List<CancelOrderRef> Orders);

public record CancelResponse(
    string UnsignedTxCborHex);
