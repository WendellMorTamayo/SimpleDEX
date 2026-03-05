use aiken/crypto.{ScriptHash}
use cardano/transaction.{OutputReference, Transaction}
use merkelized_validator
use types.{MerkelizedBuy, MerkelizedCancel, MerkelizedOrderRedeemer, OrderDatum}

validator simple_dex_merkelized(
  buy_hash: ScriptHash,
  cancel_hash: ScriptHash,
) {
  spend(
    _datum: Option<OrderDatum>,
    redeemer: MerkelizedOrderRedeemer,
    _utxo: OutputReference,
    self: Transaction,
  ) {
    let logic_hash =
      when redeemer is {
        MerkelizedBuy { .. } -> buy_hash
        MerkelizedCancel -> cancel_hash
      }
    merkelized_validator.generic_delegated_validation(
      staking_validator: logic_hash,
      withdraw_redeemer_validator: fn(_redeemer: Data) { True },
      redeemers: self.redeemers,
    )
  }

  else(_) {
    fail
  }
}
