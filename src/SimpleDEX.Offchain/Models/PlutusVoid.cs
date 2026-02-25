using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Tx.Models;

[CborSerializable]
[CborConstr(0)]
public partial record PlutusVoid : CborBase;