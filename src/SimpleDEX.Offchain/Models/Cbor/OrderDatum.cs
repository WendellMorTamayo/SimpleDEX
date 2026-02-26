using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus.Address;

namespace SimpleDEX.Offchain.Models.Cbor;

// Constr(0, [owner, offer, ask, price])
[CborSerializable]
[CborConstr(0)]
public partial record OrderDatum(
    Address Owner,
    TokenId Offer,
    TokenId Ask,
    ulong Price
) : CborBase;
