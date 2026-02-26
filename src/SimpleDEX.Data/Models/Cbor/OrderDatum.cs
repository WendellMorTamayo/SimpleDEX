using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus.Address;

namespace SimpleDEX.Data.Models.Cbor;

[CborSerializable]
[CborConstr(0)]
public partial record OrderDatum(
    byte[] Owner,
    Address Destination,
    TokenId Offer,
    TokenId Ask,
    ulong Price
) : CborBase;
