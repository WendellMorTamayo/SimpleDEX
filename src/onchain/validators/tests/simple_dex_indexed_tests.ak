use cardano/address.{Script}
use cardano/assets
use cardano/transaction.{NoDatum, Output, OutputReference, Spend, Transaction}
use simple_dex_indexed
use tests/utils.{mock_order, mock_script_input, mock_utxo}
use types.{IndexedBuy, IndexedCancel, OrderDatum}

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

  simple_dex_indexed.simple_dex_indexed.spend(
    Some(order),
    IndexedBuy { output_index: 0 },
    utxo,
    tx,
  )
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

  simple_dex_indexed.simple_dex_indexed.spend(
    Some(order),
    IndexedBuy { output_index: 0 },
    utxo,
    tx,
  )
}

// -- Withdraw Tests (runs full validation with output_index matching) --

test withdraw_buy_succeeds() {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let offer_qty = 100

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [
              Pair(Spend(utxo), to_data(IndexedBuy { output_index: 0 })),
            ],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
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
  let offer_qty = 100

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty - 1,
      ),
      datum: NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [
              Pair(Spend(utxo), to_data(IndexedBuy { output_index: 0 })),
            ],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

test withdraw_buy_fails_wrong_recipient() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let offer_qty = 100

  let bob =
    address.Address {
      payment_credential: address.VerificationKey(#"02"),
      stake_credential: None,
    }

  let output =
    Output {
      address: bob,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [
              Pair(Spend(utxo), to_data(IndexedBuy { output_index: 0 })),
            ],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
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
            outputs: [],
            extra_signatories: [alice_pkh],
            redeemers: [Pair(Spend(utxo), to_data(IndexedCancel))],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
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
            outputs: [],
            extra_signatories: [#"ff"],
            redeemers: [Pair(Spend(utxo), to_data(IndexedCancel))],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

// -- Batch Buy via Withdraw (output_index matching) --

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

  let output_a =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: NoDatum,
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
      datum: NoDatum,
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
            redeemers: [
              Pair(
                Spend(utxo_a),
                to_data(IndexedBuy { output_index: 0 }),
              ),
              Pair(
                Spend(utxo_b),
                to_data(IndexedBuy { output_index: 1 }),
              ),
            ],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

// Double satisfaction not blocked: 2 inputs both point at same output_index (known tradeoff)
test withdraw_double_satisfaction_not_blocked() {
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

  let single_payment =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: NoDatum,
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
            redeemers: [
              Pair(
                Spend(utxo_a),
                to_data(IndexedBuy { output_index: 0 }),
              ),
              Pair(
                Spend(utxo_b),
                to_data(IndexedBuy { output_index: 0 }),
              ),
            ],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}

// Invalid output_index beyond list length → list.at fails
test withdraw_buy_fails_invalid_output_index() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()
  let offer_qty = 100

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, offer_qty, script_hash)],
            outputs: [output],
            redeemers: [
              Pair(Spend(utxo), to_data(IndexedBuy { output_index: 99 })),
            ],
          }
        }

  simple_dex_indexed.simple_dex_indexed.withdraw(
    Void,
    Script(script_hash),
    tx,
  )
}
