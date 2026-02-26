namespace SimpleDEX.Offchain.Models;

public record CancelRequest(
    string OrderTxHash,
    ulong OrderIndex
);

public record CancelResponse(
    string UnsignedTxCborHex
);