use aiken/collection/list
use aiken/collection/pairs
use cardano/address.{Credential, Script}
use cardano/certificate.{Certificate}
use cardano/transaction.{InlineDatum, Spend, Transaction}
use types.{MerkelizedCancel, MerkelizedOrderRedeemer, OrderDatum}

validator cancel_logic {
  withdraw(_redeemer: Data, account: Credential, self: Transaction) {
    expect Script(_own_hash) = account

    let relevant_inputs =
      self.inputs
        |> list.filter(
            fn(input) {
              let utxo = input.output_reference
              when self.redeemers |> pairs.get_first(Spend(utxo)) is {
                Some(raw_redeemer) -> {
                  expect redeemer: MerkelizedOrderRedeemer = raw_redeemer
                  when redeemer is {
                    MerkelizedCancel -> True
                    _ -> False
                  }
                }
                None -> False
              }
            },
          )

    list.all(
      relevant_inputs,
      fn(input) {
        expect InlineDatum(raw_datum) = input.output.datum
        expect order: OrderDatum = raw_datum

        list.has(self.extra_signatories, order.owner)
      },
    )
  }

  publish(_redeemer: Data, _certificate: Certificate, _self: Transaction) {
    True
  }

  else(_) {
    fail
  }
}
