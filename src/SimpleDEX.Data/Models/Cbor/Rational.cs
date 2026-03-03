using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Data.Models.Cbor;

[CborSerializable]
[CborConstr(0)]
public partial record RationalC(
    ulong Num,
    ulong Den
) : CborBase;
