using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Offchain.Models.Cbor;

// Constr(0, [policy_id, asset_name])
[CborSerializable]
[CborConstr(0)]
public partial record TokenId(
    byte[] PolicyId,
    byte[] AssetName
) : CborBase;
