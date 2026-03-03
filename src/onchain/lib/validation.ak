use aiken/cbor
use aiken/collection/list
use aiken/crypto
use cardano/assets
use cardano/transaction.{InlineDatum, Input, Output, OutputReference, Transaction}
use types.{OrderDatum}

/// Compute the unique datum tag for an order UTxO
pub fn datum_tag(utxo: OutputReference) -> ByteArray {
  utxo
    |> cbor.serialise()
    |> crypto.blake2b_256()
}

/// Validate a Buy (indexed): use output_index to look up the target output
pub fn validate_buy_indexed(
  order: OrderDatum,
  input: Input,
  outputs: List<Output>,
  output_index: Int,
) -> Bool {
  expect Some(output) = list.at(outputs, output_index)

  let offer_quantity =
    assets.quantity_of(
      input.output.value,
      order.offer.policy_id,
      order.offer.asset_name,
    )

  expect offer_quantity >= order.offer.quantity

  let required_payment =
    ( order.offer.quantity * order.price.num + order.price.den - 1 ) / order.price.den

  let is_sent_to_owner = output.address == order.destination

  let is_price_met =
    assets.quantity_of(
      output.value,
      order.ask.policy_id,
      order.ask.asset_name,
    ) >= required_payment

  and {
    is_sent_to_owner?,
    is_price_met?,
  }
}

/// Validate a Buy: seller receives payment with correct tag
pub fn validate_buy(
  order: OrderDatum,
  utxo: OutputReference,
  input: Input,
  self: Transaction,
) -> Bool {
  let tag = datum_tag(utxo)

  let offer_quantity =
    assets.quantity_of(
      input.output.value,
      order.offer.policy_id,
      order.offer.asset_name,
    )

  expect offer_quantity >= order.offer.quantity

  let required_payment =
    ( order.offer.quantity * order.price.num + order.price.den - 1 ) / order.price.den

  self.outputs
    |> list.any(
        fn(output) {
          let is_sent_to_owner = output.address == order.destination

          expect InlineDatum(raw_datum) = output.datum
          expect raw_datum: ByteArray = raw_datum
          let is_correct_tag = tag == raw_datum

          let is_price_met =
            assets.quantity_of(
              output.value,
              order.ask.policy_id,
              order.ask.asset_name,
            ) >= required_payment

          and {
            is_sent_to_owner?,
            is_price_met?,
            is_correct_tag?,
          }
        },
      )
}
