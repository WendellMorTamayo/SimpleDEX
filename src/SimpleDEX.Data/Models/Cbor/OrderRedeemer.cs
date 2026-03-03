using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Data.Models.Cbor;

[CborSerializable]
[CborUnion]
public abstract partial record OrderRedeemer : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Buy : OrderRedeemer;

[CborSerializable]
[CborConstr(1)]
public partial record Cancel : OrderRedeemer;

[CborSerializable]
[CborUnion]
public abstract partial record IndexedOrderRedeemer : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record IndexedBuy(ulong OutputIndex) : IndexedOrderRedeemer;

[CborSerializable]
[CborConstr(1)]
public partial record IndexedCancel : IndexedOrderRedeemer;
