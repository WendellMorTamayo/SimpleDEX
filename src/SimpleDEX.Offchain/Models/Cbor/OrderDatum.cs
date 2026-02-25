using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Offchain.Models.Cbor;

// Constr(0, [owner, offer, ask, price, order_tag])
[CborSerializable]
[CborConstr(0)]
public partial record OrderDatum(
    byte[] Owner,
    TokenId Offer,
    TokenId Ask,
    ulong Price,
    byte[] OrderTag
) : CborBase;
