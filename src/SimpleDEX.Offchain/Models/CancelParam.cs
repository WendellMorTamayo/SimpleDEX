using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace SimpleDEX.Offchain.Models;

public record CancelRequest(
    string OrderTxHash,
    ulong OrderIndex,
    string Owner
);

public record CancelResponse(
    string UnsignedTxCborHex
);