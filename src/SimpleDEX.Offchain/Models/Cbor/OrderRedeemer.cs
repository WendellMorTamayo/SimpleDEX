using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Offchain.Models.Cbor;

[CborSerializable]
[CborUnion]
public abstract partial record OrderRedeemer : CborBase;

// Constr(0, []) — Buy redeemer
[CborSerializable]
[CborConstr(0)]
public partial record Buy : OrderRedeemer;

// Constr(1, []) — Cancel redeemer
[CborSerializable]
[CborConstr(1)]
public partial record Cancel : OrderRedeemer;
