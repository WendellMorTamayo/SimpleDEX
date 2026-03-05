use aiken/collection/list
use aiken/collection/pairs
use cardano/address.{Credential, Script}
use cardano/certificate.{Certificate}
use cardano/transaction.{InlineDatum, Spend, Transaction}
use types.{MerkelizedBuy, MerkelizedOrderRedeemer, OrderDatum}
use validation

validator buy_logic {
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
                    MerkelizedBuy { .. } -> True
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
        let utxo = input.output_reference

        expect InlineDatum(raw_datum) = input.output.datum
        expect order: OrderDatum = raw_datum

        expect Some(raw_redeemer) =
          self.redeemers
            |> pairs.get_first(Spend(utxo))
        expect redeemer: MerkelizedOrderRedeemer = raw_redeemer
        expect MerkelizedBuy { output_index } = redeemer

        validation.validate_buy_indexed(
          order,
          input,
          self.outputs,
          output_index,
        )
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
