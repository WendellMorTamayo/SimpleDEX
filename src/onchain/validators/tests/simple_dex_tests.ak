use cardano/assets
use cardano/transaction.{InlineDatum, Output, OutputReference, Transaction}
use simple_dex
use types.{OrderDatum, Buy, Cancel, TokenId, Unit, RationalC}
use tests/utils.{mock_ask, mock_order, mock_input, mock_address, mock_utxo,
  mock_tag}

// -- Buy Tests --

// Happy path: 100 offer tokens at 1:1 price → pays 100 ask tokens → should pass
test buy_succeeds() {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
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
            inputs: [mock_input(utxo, order, offer_qty)],
            outputs: [output],
          }
        }

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Tag is from a different UTxO — validator computes tag from the actual utxo
// being spent, so a mismatched tag fails with is_correct_tag ? False
test buy_fails_wrong_tag() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
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
            inputs: [mock_input(utxo, order, offer_qty)],
            outputs: [output],
          }
        }

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Buyer pays 99 instead of 100 — fails with is_price_met ? False
test buy_fails_insufficient_payment() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
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
            inputs: [mock_input(utxo, order, offer_qty)],
            outputs: [output],
          }
        }

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Payment goes to #"ff" instead of alice (#"01") — fails with is_sent_to_owner ? False
test buy_fails_wrong_recipient() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
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
      datum: InlineDatum(tag),
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

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Output has NoDatum instead of InlineDatum — crashes on expect InlineDatum(raw_datum)
test buy_fails_no_datum() fail {
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

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Buyer pays 150 instead of 100 — overpaying is allowed (>= check)
test buy_succeeds_overpay() {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
  let offer_qty = 100

  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        offer_qty + 50,
      ),
      datum: InlineDatum(tag),
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

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// -- Cancel Tests --

// Owner (alice) signs the transaction — cancel should pass
test cancel_succeeds() {
  let alice_pkh = #"01"
  let alice = mock_address(alice_pkh)
  let order = mock_order(alice_pkh, alice)
  let utxo = mock_utxo()

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, extra_signatories: [alice_pkh] } }

  simple_dex.simple_dex.spend(Some(order), Cancel, utxo, tx)
}

// No one signed the transaction — cancel should fail
test cancel_fails_no_signature() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let utxo = mock_utxo()

  // Empty extra_signatories — no signature present
  let tx = transaction.placeholder

  simple_dex.simple_dex.spend(Some(order), Cancel, utxo, tx)
}

// Eve (#"ff") signs instead of alice (#"01") — cancel should fail
test cancel_fails_wrong_signer() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let utxo = mock_utxo()
  let eve_pkh = #"ff"

  // Eve's signature is present, but she's not the owner
  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, extra_signatories: [eve_pkh] } }

  simple_dex.simple_dex.spend(Some(order), Cancel, utxo, tx)
}

// -- Double Satisfaction Tests --

// Eve spends two of Alice's orders in one transaction but only includes
// one payment output tagged with utxo_a's hash.
// UTxO A passes (tag matches), but UTxO B fails (tag_a != tag_b)
// because each UTxO produces a different blake2b hash.
// Proves the tagging prevents the exploit — marked `fail` because
// the combined result is False.
test double_satisfaction_blocked() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let offer_qty = 100

  // Two different UTxOs — different tx hashes, so different tags
  let utxo_a = OutputReference { transaction_id: #"0000", output_index: 0 }
  let utxo_b = OutputReference { transaction_id: #"0001", output_index: 0 }

  // Eve tags the payment with utxo_a's tag only
  let tag_a = mock_tag(utxo_a)

  // Single payment output — Eve tries to satisfy both orders with this
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

  // Both inputs in the tx so find_input works for both
  let inputs = [
    mock_input(utxo_a, order, offer_qty),
    mock_input(utxo_b, order, offer_qty),
  ]

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction { ..t, inputs: inputs, outputs: [single_payment] }
        }

  // UTxO A: validator computes blake2b(utxo_a) → matches tag_a → passes
  let order_a_passes = simple_dex.simple_dex.spend(Some(order), Buy, utxo_a, tx)
  // UTxO B: validator computes blake2b(utxo_b) → doesn't match tag_a → fails
  let order_b_passes = simple_dex.simple_dex.spend(Some(order), Buy, utxo_b, tx)

  // Combined result is False — Eve can't steal the second order
  order_a_passes && order_b_passes
}

// -- Rational Price Tests --

// Fractional price: 3/2 (1.5 ask per offer), 10 offer → ceil(10*3/2) = 15
test buy_succeeds_rational_price() {
  let alice = mock_address(#"01")
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
  let order =
    OrderDatum {
      owner: #"01",
      destination: alice,
      offer: Unit { policy_id: #"aa", asset_name: #"bb", quantity: 10 },
      ask: mock_ask(),
      price: RationalC { num: 3, den: 2 },
    }
  let offer_qty = 10

  // required = ceil(10 * 3 / 2) = ceil(15) = 15
  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        15,
      ),
      datum: InlineDatum(tag),
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

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Fractional price with ceiling: 3/2 price, 7 offer → ceil(7*3/2) = ceil(10.5) = 11
test buy_succeeds_rational_price_ceiling() {
  let alice = mock_address(#"01")
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
  let order =
    OrderDatum {
      owner: #"01",
      destination: alice,
      offer: Unit { policy_id: #"aa", asset_name: #"bb", quantity: 7 },
      ask: mock_ask(),
      price: RationalC { num: 3, den: 2 },
    }
  let offer_qty = 7

  // required = ceil(7 * 3 / 2) = ceil(21/2) = ceil(10.5) = 11
  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        11,
      ),
      datum: InlineDatum(tag),
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

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Ceiling rounding: paying 10 instead of 11 should fail
test buy_fails_rational_price_underpay() fail {
  let alice = mock_address(#"01")
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)
  let order =
    OrderDatum {
      owner: #"01",
      destination: alice,
      offer: Unit { policy_id: #"aa", asset_name: #"bb", quantity: 7 },
      ask: mock_ask(),
      price: RationalC { num: 3, den: 2 },
    }
  let offer_qty = 7

  // required = ceil(7 * 3 / 2) = 11, paying only 10
  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        10,
      ),
      datum: InlineDatum(tag),
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

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Datum declares quantity 100 but UTxO only has 50 — should fail
test buy_fails_quantity_mismatch() fail {
  let alice = mock_address(#"01")
  let order = mock_order(#"01", alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)

  // order.offer.quantity is 100 (from mock_order), but UTxO has only 50
  let output =
    Output {
      address: alice,
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        50,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) {
          Transaction {
            ..t,
            inputs: [mock_input(utxo, order, 50)],
            outputs: [output],
          }
        }

  simple_dex.simple_dex.spend(Some(order), Buy, utxo, tx)
}
