use aiken/collection/list
use aiken/collection/pairs
use cardano/address.{Credential}
use cardano/certificate.{Certificate}
use cardano/transaction.{InlineDatum, Input, Output, OutputReference, Spend,
  Transaction}
use types.{Buy, Cancel, OrderDatum, OrderRedeemer}
use validation

validator simple_dex_indexed {
  spend(
    _datum: Option<OrderDatum>,
    _redeemer: OrderRedeemer,
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

    validate_inputs(script_inputs, self.outputs, self)
  }

  publish(_redeemer: Data, _certificate: Certificate, _self: Transaction) {
    True
  }

  else(_) {
    fail
  }
}

/// Walk script inputs and outputs positionally.
/// Buy consumes the next output; Cancel skips it.
fn validate_inputs(
  inputs: List<Input>,
  outputs: List<Output>,
  self: Transaction,
) -> Bool {
  when inputs is {
    [] -> True
    [input, ..rest_inputs] -> {
      let utxo = input.output_reference

      expect InlineDatum(raw_datum) = input.output.datum
      expect order: OrderDatum = raw_datum

      expect Some(raw_redeemer) =
        self.redeemers
          |> pairs.get_first(Spend(utxo))
      expect redeemer: OrderRedeemer = raw_redeemer

      when redeemer is {
        Buy -> {
          expect [output, ..rest_outputs] = outputs
          validation.validate_buy_indexed(order, input, output) && validate_inputs(
            rest_inputs,
            rest_outputs,
            self,
          )
        }
        Cancel ->
          list.has(self.extra_signatories, order.owner) && validate_inputs(
            rest_inputs,
            outputs,
            self,
          )
      }
    }
  }
}
