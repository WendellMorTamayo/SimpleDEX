use aiken/collection/pairs
use aiken/crypto.{ScriptHash}
use cardano/address.{Script}
use cardano/transaction.{Redeemer, ScriptPurpose, Withdraw}

pub fn generic_delegated_validation(
  staking_validator: ScriptHash,
  withdraw_redeemer_validator: fn(Data) -> Bool,
  redeemers: Pairs<ScriptPurpose, Redeemer>,
) -> Bool {
  expect Some(rdmr) =
    redeemers
      |> pairs.get_first(Withdraw(Script(staking_validator)))
  withdraw_redeemer_validator(rdmr)
}
