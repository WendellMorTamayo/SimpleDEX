use cardano/address.{Script}
use cardano/assets
use cardano/transaction.{InlineDatum, Output, OutputReference, Spend, Transaction}
use simple_dex_withdraw
use tests/utils.{mock_order, mock_script_input, mock_utxo, mock_tag}
use types.{Buy, Cancel, OrderDatum}

// Shared script hash for tests
const script_hash = #"dead"

fn to_data(a: Data) -> Data {
  a
}

// -- Spend Tests (just checks withdrawal exists) --

test spend_succeeds_with_withdrawal() {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let offer_qty = 100

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            withdrawals: [Pair(Script(script_hash), 0)],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.spend(Some(order), Buy, utxo, tx)
}

test spend_fails_without_withdrawal() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let offer_qty = 100

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            withdrawals: [],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.spend(Some(order), Buy, utxo, tx)
}

// -- Withdraw Tests (runs full validation) --

test withdraw_buy_succeeds() {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
  let offer_qty = 100

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [Pair(Spend(utxo), to_data(Buy))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

test withdraw_buy_fails_insufficient_payment() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
  let offer_qty = 100

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty - 1,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [Pair(Spend(utxo), to_data(Buy))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

test withdraw_buy_fails_wrong_tag() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let offer_qty = 100
  let wrong_utxo = OutputReference { transaction_id: #"0001", output_index: 0 }
  let wrong_tag = mock_tag(wrong_utxo)

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: InlineDatum(wrong_tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [Pair(Spend(utxo), to_data(Buy))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

test withdraw_cancel_succeeds() {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, 100, script_hash)],
            extra_signatories: [alice_pkh],
            redeemers: [Pair(Spend(utxo), to_data(Cancel))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

test withdraw_cancel_fails_wrong_signer() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, 100, script_hash)],
            extra_signatories: [#"ff"],
            redeemers: [Pair(Spend(utxo), to_data(Cancel))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

// -- Batch Buy via Withdraw --

test withdraw_batch_buy_succeeds() {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let offer_qty = 100

  let utxo_a = OutputReference { transaction_id: #"0000", output_index: 0 }
  let utxo_b = OutputReference { transaction_id: #"0001", output_index: 0 }
  let tag_a = mock_tag(utxo_a)
  let tag_b = mock_tag(utxo_b)

  let output_a =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: InlineDatum(tag_a),
      reference_script: None,
    }

  let output_b =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: InlineDatum(tag_b),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [
              mock_script_input(utxo_a, order, offer_qty, script_hash),
              mock_script_input(utxo_b, order, offer_qty, script_hash),
            ],
            outputs: [output_a, output_b],
            redeemers: [Pair(Spend(utxo_a), to_data(Buy)), Pair(Spend(utxo_b), to_data(Buy))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

// Double satisfaction blocked: two orders, one payment output with only tag_a
test withdraw_double_satisfaction_blocked() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let offer_qty = 100

  let utxo_a = OutputReference { transaction_id: #"0000", output_index: 0 }
  let utxo_b = OutputReference { transaction_id: #"0001", output_index: 0 }
  let tag_a = mock_tag(utxo_a)

  let single_payment =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: InlineDatum(tag_a),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [
              mock_script_input(utxo_a, order, offer_qty, script_hash),
              mock_script_input(utxo_b, order, offer_qty, script_hash),
            ],
            outputs: [single_payment],
            redeemers: [Pair(Spend(utxo_a), to_data(Buy)), Pair(Spend(utxo_b), to_data(Buy))],
          }
        }

  simple_dex_withdraw.simple_dex_withdraw.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}
