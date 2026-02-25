using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Offchain.Models.Cbor;

[CborSerializable]
public partial record DatumTag(byte[] Tag) : CborBase;