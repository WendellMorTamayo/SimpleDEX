use cardano/address.{Script}
use cardano/assets
use cardano/transaction.{
  NoDatum, Output, OutputReference, Spend, Transaction, Withdraw,
}
use buy_logic
use cancel_logic
use simple_dex_merkelized
use tests/utils.{mock_order, mock_script_input, mock_utxo}
use types.{MerkelizedBuy, MerkelizedCancel}

const main_hash = #"dead"

const buy_hash = #"b001"

const cancel_hash = #"c001"

fn to_data(a: Data) -> Data {
  a
}

// -- Spend Tests (delegation: checks logic validator withdrawal exists) --

test spend_buy_succeeds() {
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
            inputs: [mock_script_input(utxo, order, 100, main_hash)],
            redeemers: [
              Pair(Withdraw(Script(buy_hash)), to_data(Void)),
            ],
          }
        }

  simple_dex_merkelized.simple_dex_merkelized.spend(
    buy_hash,
    cancel_hash,
    Some(order),
    MerkelizedBuy { output_index: 0 },
    utxo,
    tx,
  )
}

test spend_cancel_succeeds() {
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
            inputs: [mock_script_input(utxo, order, 100, main_hash)],
            redeemers: [
              Pair(Withdraw(Script(cancel_hash)), to_data(Void)),
            ],
          }
        }

  simple_dex_merkelized.simple_dex_merkelized.spend(
    buy_hash,
    cancel_hash,
    Some(order),
    MerkelizedCancel,
    utxo,
    tx,
  )
}

test spend_fails_without_logic_withdrawal() fail {
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
            inputs: [mock_script_input(utxo, order, 100, main_hash)],
            redeemers: [],
          }
        }

  simple_dex_merkelized.simple_dex_merkelized.spend(
    buy_hash,
    cancel_hash,
    Some(order),
    MerkelizedBuy { output_index: 0 },
    utxo,
    tx,
  )
}

test spend_fails_wrong_logic_withdrawal() fail {
  let alice_pkh = #"01"
  let alice =
    address.Address {
      payment_credential: address.VerificationKey(alice_pkh),
      stake_credential: None,
    }
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()

  // Buy redeemer but cancel_hash withdrawal — should fail
  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_script_input(utxo, order, 100, main_hash)],
            redeemers: [
              Pair(Withdraw(Script(cancel_hash)), to_data(Void)),
            ],
          }
        }

  simple_dex_merkelized.simple_dex_merkelized.spend(
    buy_hash,
    cancel_hash,
    Some(order),
    MerkelizedBuy { output_index: 0 },
    utxo,
    tx,
  )
}

// -- Buy Logic Tests --

test buy_logic_succeeds() {
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
            inputs: [mock_script_input(utxo, order, offer_qty, main_hash)],
            outputs: [output],
            redeemers: [
              Pair(
                Spend(utxo),
                to_data(MerkelizedBuy { output_index: 0 }),
              ),
            ],
          }
        }

  buy_logic.buy_logic.withdraw(Void, Script(buy_hash), tx)
}

test buy_logic_fails_insufficient_payment() fail {
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
            inputs: [mock_script_input(utxo, order, offer_qty, main_hash)],
            outputs: [output],
            redeemers: [
              Pair(
                Spend(utxo),
                to_data(MerkelizedBuy { output_index: 0 }),
              ),
            ],
          }
        }

  buy_logic.buy_logic.withdraw(Void, Script(buy_hash), tx)
}

test buy_logic_fails_wrong_recipient() fail {
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
            inputs: [mock_script_input(utxo, order, offer_qty, main_hash)],
            outputs: [output],
            redeemers: [
              Pair(
                Spend(utxo),
                to_data(MerkelizedBuy { output_index: 0 }),
              ),
            ],
          }
        }

  buy_logic.buy_logic.withdraw(Void, Script(buy_hash), tx)
}

// -- Cancel Logic Tests --

test cancel_logic_succeeds() {
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
            inputs: [mock_script_input(utxo, order, 100, main_hash)],
            extra_signatories: [alice_pkh],
            redeemers: [
              Pair(Spend(utxo), to_data(MerkelizedCancel)),
            ],
          }
        }

  cancel_logic.cancel_logic.withdraw(Void, Script(cancel_hash), tx)
}

test cancel_logic_fails_wrong_signer() fail {
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
            inputs: [mock_script_input(utxo, order, 100, main_hash)],
            extra_signatories: [#"ff"],
            redeemers: [
              Pair(Spend(utxo), to_data(MerkelizedCancel)),
            ],
          }
        }

  cancel_logic.cancel_logic.withdraw(Void, Script(cancel_hash), tx)
}

// -- Batch Buy through Buy Logic --

test batch_buy_succeeds() {
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
              mock_script_input(utxo_a, order, offer_qty, main_hash),
              mock_script_input(utxo_b, order, offer_qty, main_hash),
            ],
            outputs: [output_a, output_b],
            redeemers: [
              Pair(
                Spend(utxo_a),
                to_data(MerkelizedBuy { output_index: 0 }),
              ),
              Pair(
                Spend(utxo_b),
                to_data(MerkelizedBuy { output_index: 1 }),
              ),
            ],
          }
        }

  buy_logic.buy_logic.withdraw(Void, Script(buy_hash), tx)
}
