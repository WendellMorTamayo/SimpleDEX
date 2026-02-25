
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Offchain.Models.Cbor;

[CborSerializable]
[CborIndefinite]
[CborConstr(0)]
public partial record CborOutRef(
    [CborOrder(0)] byte[] Id,
    [CborOrder(1)] ulong Index
): CborBase;