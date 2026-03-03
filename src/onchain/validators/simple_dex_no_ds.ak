use aiken/collection/list
use cardano/assets
use cardano/transaction.{OutputReference, Transaction}
use types.{OrderDatum, OrderRedeemer, Buy, Cancel}

// Simple DEX swap validator WITHOUT double-satisfaction prevention.
// Identical to simple_dex except the Buy branch does NOT require a
// blake2b_256 datum tag on the payment output. This allows one payment
// output to satisfy multiple orders — the classic double-satisfaction exploit.
// Included for educational comparison.
validator simple_dex_no_ds {
  spend(
    datum: Option<OrderDatum>,
    redeemer: OrderRedeemer,
    utxo: OutputReference,
    self: Transaction,
  ) {
    expect Some(order) = datum

    let destination = order.destination
    let ask = order.ask

    when redeemer is {
      Buy -> {
        expect Some(input) =
          self.inputs |> transaction.find_input(utxo)
        let offer_quantity =
          assets.quantity_of(
            input.output.value,
            order.offer.policy_id,
            order.offer.asset_name,
          )

        let required_payment =
          ( offer_quantity * order.price.num + order.price.den - 1 ) / order.price.den

        self.outputs
          |> list.any(
              fn(output) {
                let is_sent_to_owner = output.address == destination

                let is_price_met =
                  assets.quantity_of(
                    output.value,
                    ask.policy_id,
                    ask.asset_name,
                  ) >= required_payment

                and {
                  is_sent_to_owner?,
                  is_price_met?,
                }
              },
            )
      }

      Cancel -> list.has(self.extra_signatories, order.owner)
    }
  }

  else(_) {
    fail
  }
}
