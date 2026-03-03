use aiken/collection/list
use cardano/transaction.{OutputReference, Transaction}
use types.{OrderDatum, OrderRedeemer, Buy, Cancel}
use validation

validator simple_dex {
  spend(
    datum: Option<OrderDatum>,
    redeemer: OrderRedeemer,
    utxo: OutputReference,
    self: Transaction,
  ) {
    expect Some(order) = datum

    when redeemer is {
      Buy -> {
        expect Some(input) =
          self.inputs |> transaction.find_input(utxo)
        validation.validate_buy(order, utxo, input, self)
      }
      Cancel -> list.has(self.extra_signatories, order.owner)
    }
  }

  else(_) {
    fail
  }
}
