use aiken/collection/list
use aiken/collection/pairs
use cardano/address.{Credential}
use cardano/certificate.{Certificate}
use cardano/transaction.{
  InlineDatum, OutputReference, Spend, Transaction,
}
use types.{IndexedBuy, IndexedCancel, IndexedOrderRedeemer, OrderDatum}
use validation

validator simple_dex_indexed {
  spend(
    _datum: Option<OrderDatum>,
    _redeemer: IndexedOrderRedeemer,
    utxo: OutputReference,
    self: Transaction,
  ) {
    expect Some(input) =
      self.inputs
        |> transaction.find_input(utxo)
    pairs.has_key(self.withdrawals, input.output.address.payment_credential)
  }

  withdraw(_redeemer: Data, account: Credential, self: Transaction) {
    let script_inputs =
      self.inputs
        |> list.filter(
            fn(input) { input.output.address.payment_credential == account },
          )

    list.all(
      script_inputs,
      fn(input) {
        let utxo = input.output_reference

        expect InlineDatum(raw_datum) = input.output.datum
        expect order: OrderDatum = raw_datum

        expect Some(raw_redeemer) =
          self.redeemers
            |> pairs.get_first(Spend(utxo))
        expect redeemer: IndexedOrderRedeemer = raw_redeemer

        when redeemer is {
          IndexedBuy { output_index } ->
            validation.validate_buy_indexed(
              order,
              input,
              self.outputs,
              output_index,
            )
          IndexedCancel -> list.has(self.extra_signatories, order.owner)
        }
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
