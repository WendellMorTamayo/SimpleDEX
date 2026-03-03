using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Data.Models.Cbor;

[CborSerializable]
[CborConstr(0)]
public partial record TokenId(
    byte[] PolicyId,
    byte[] AssetName
) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Unit(
    byte[] PolicyId,
    byte[] AssetName,
    ulong Quantity
) : CborBase;
