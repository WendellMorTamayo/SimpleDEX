use cardano/assets
use cardano/transaction.{Output, OutputReference, Transaction}
use simple_dex_no_ds
use types.{Buy}
use tests/utils.{mock_order, mock_input, mock_address, mock_utxo}

// -- Buy Tests (No Double-Satisfaction Prevention) --

// Happy path: correct payment to seller, no tag needed
test buy_no_ds_succeeds() {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
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
      datum: transaction.NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_input(utxo, order, offer_qty)],
            outputs: [output],
          }
        }

  simple_dex_no_ds.simple_dex_no_ds.spend(Some(order), Buy, utxo, tx)
}

// Buyer pays 99 instead of 100 — rejected
test buy_no_ds_fails_insufficient_payment() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
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
      datum: transaction.NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_input(utxo, order, offer_qty)],
            outputs: [output],
          }
        }

  simple_dex_no_ds.simple_dex_no_ds.spend(Some(order), Buy, utxo, tx)
}

// Payment goes to eve instead of alice — rejected
test buy_no_ds_fails_wrong_recipient() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let utxo = mock_utxo()
  let offer_qty = 100

  let eve = mock_address(#"ff")
  let output =
    Output {
      address: eve,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: transaction.NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_input(utxo, order, offer_qty)],
            outputs: [output],
          }
        }

  simple_dex_no_ds.simple_dex_no_ds.spend(Some(order), Buy, utxo, tx)
}

// Double satisfaction: Eve spends two orders with a single payment output.
// Without the datum tag, BOTH orders pass — proving double satisfaction is NOT blocked.
test double_satisfaction_no_ds_not_blocked() {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let offer_qty = 100

  let utxo_a = OutputReference { transaction_id: #"0000", output_index: 0 }
  let utxo_b = OutputReference { transaction_id: #"0001", output_index: 0 }

  // Single payment output — no tag, just pays enough to the right address
  let single_payment =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty,
      ),
      datum: transaction.NoDatum,
      reference_script: None,
    }

  let inputs = [
    mock_input(utxo_a, order, offer_qty),
    mock_input(utxo_b, order, offer_qty),
  ]

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction { ..t, inputs: inputs, outputs: [single_payment] }
        }

  // Both orders pass with the same single payment — double satisfaction succeeds
  let order_a_passes =
    simple_dex_no_ds.simple_dex_no_ds.spend(Some(order), Buy, utxo_a, tx)
  let order_b_passes =
    simple_dex_no_ds.simple_dex_no_ds.spend(Some(order), Buy, utxo_b, tx)

  order_a_passes && order_b_passes
}
