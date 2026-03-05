use aiken/cbor

test encode_buy_hash() {
  let hash = #"87568b508db8959a979eba197a75af9100b9a1b965405febbbebd011"
  trace @"buy_hash CBOR:"
  trace cbor.diagnostic(cbor.serialise(hash))
  True
}

test encode_cancel_hash() {
  let hash = #"622aa0c964fa8fb64ba4f72808510f34ac73258723b028a22313db52"
  trace @"cancel_hash CBOR:"
  trace cbor.diagnostic(cbor.serialise(hash))
  True
}
