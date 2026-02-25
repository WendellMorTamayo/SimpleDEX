use aiken/cbor
use aiken/collection/list
use aiken/crypto
use cardano/address.{Address, VerificationKey}
use cardano/assets.{AssetName, PolicyId}
use cardano/transaction.{InlineDatum, Output, OutputReference, Transaction}

// Identifies a token type (no amount — amount lives in the UTxO value itself)
pub type TokenId {
  policy_id: PolicyId,
  asset_name: AssetName,
}

// The order listing stored as inline datum on the script UTxO
pub type OrderDatum {
  // Seller's pubkey hash — receives payment, can cancel
  owner: ByteArray,
  // What token is being sold (the UTxO value holds the actual amount)
  offer: TokenId,
  // What token the seller wants in return
  ask: TokenId,
  // Price per unit of offer token (in ask token units)
  // e.g. price = 5 means 1 offer token costs 5 ask tokens
  price: Int,
  // Unique order tag: blake2b_256(tx_hash ++ output_index) from the UTxO that created this order
  // Prevents double satisfaction — each order has a unique tag, so the seller payment output
  // must reference this exact tag, making it impossible for one output to satisfy two validators
  order_tag: ByteArray,
}

pub type OrderRedeemer {
  // Buyer specifies how many offer tokens they want to purchase
  Buy
  // Owner cancels and reclaims — no extra data needed
  Cancel
}

// Simple DEX swap validator
// Seller locks offer tokens at this script address with an OrderDatum.
// Buyer submits a Buy redeemer to purchase — must pay the seller the ask token at the specified price.
// Seller submits a Cancel redeemer to reclaim — must sign the transaction.
// Uses blake2b_256 tagging on the UTxO OutputReference to prevent double satisfaction.
validator simple_dex {
  spend(
    datum: Option<OrderDatum>,
    redeemer: OrderRedeemer,
    utxo: OutputReference,
    self: Transaction,
  ) {
    // Unwrap datum — fails if the UTxO has no datum attached
    expect Some(order) = datum

    let owner = order.owner
    let price = order.price
    let ask = order.ask

    // Compute a unique tag by hashing the OutputReference of the UTxO being spent.
    // Every UTxO has a unique OutputReference (tx_hash + output_index),
    // so every order produces a different tag.
    let datum_tag =
      utxo
        |> cbor.serialise()
        |> crypto.blake2b_256()

    when redeemer is {
      // Buy: verify the seller receives payment with the correct tag
      Buy ->
        self.outputs
          |> list.any(
              fn(output) {
                // Extract the pubkey hash — fails if output goes to a script address
                expect VerificationKey(pkh) = output.address.payment_credential
                // Check: payment goes to the seller
                let is_sent_to_owner = pkh == owner

                // Extract the inline datum and cast to ByteArray
                // Fails if output has NoDatum or DatumHash (must be InlineDatum)
                expect InlineDatum(raw_datum) = output.datum
                expect raw_datum: ByteArray = raw_datum

                // Check: output carries this order's unique tag
                // Prevents double satisfaction — one output can only match one tag
                let is_correct_tag = datum_tag == raw_datum

                // Check: output contains enough of the ask token (>= allows overpay)
                let is_price_met =
                  assets.quantity_of(
                    output.value,
                    ask.policy_id,
                    ask.asset_name,
                  ) >= price

                // All three must be true — ? traces which check failed
                and {
                  is_sent_to_owner?,
                  is_price_met?,
                  is_correct_tag?,
                }
              },
            )

      // Cancel: only the original owner can reclaim by signing the transaction
      Cancel -> list.has(self.extra_signatories, owner)
    }
  }

  // Reject all other script purposes (mint, withdraw, etc.)
  else(_) {
    fail
  }
}

// -- Test Helpers --

// Mock ask token: policy_id #"cc", asset_name #"dd"
fn mock_ask() -> TokenId {
  TokenId { policy_id: #"cc", asset_name: #"dd" }
}

// Mock order: seller wants 100 ask tokens for their offer tokens
fn mock_order(owner: ByteArray) -> OrderDatum {
  OrderDatum {
    owner,
    offer: TokenId { policy_id: #"aa", asset_name: #"bb" },
    ask: mock_ask(),
    price: 100,
    order_tag: #"",
  }
}

// Mock UTxO reference: tx_hash #"0000", output_index 0
fn mock_utxo() -> OutputReference {
  OutputReference { transaction_id: #"0000", output_index: 0 }
}

// Compute blake2b_256 tag from an OutputReference (same as the validator does)
fn mock_tag(utxo: OutputReference) -> ByteArray {
  utxo
    |> cbor.serialise()
    |> crypto.blake2b_256()
}

// -- Buy Tests --

// Happy path: correct recipient, correct tag, exact payment → should pass
test buy_succeeds() {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)

  // Output pays alice the ask token at exact price, with correct tag
  let output =
    Output {
      address: Address {
        payment_credential: VerificationKey(alice),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [output] } }

  simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Tag is from a different UTxO — validator computes tag from the actual utxo
// being spent, so a mismatched tag fails with is_correct_tag ? False
test buy_fails_wrong_tag() fail {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()
  // Tag computed from a different UTxO — won't match the validator's computed tag
  let wrong_utxo = OutputReference { transaction_id: #"0001", output_index: 0 }
  let wrong_tag = mock_tag(wrong_utxo)

  let output =
    Output {
      address: Address {
        payment_credential: VerificationKey(alice),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price,
      ),
      datum: InlineDatum(wrong_tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [output] } }

  simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Buyer pays 99 instead of 100 — fails with is_price_met ? False
test buy_fails_insufficient_payment() fail {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)

  let output =
    Output {
      address: Address {
        payment_credential: VerificationKey(alice),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price - 1,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [output] } }

  simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Payment goes to #"ff" instead of alice (#"01") — fails with is_sent_to_owner ? False
test buy_fails_wrong_recipient() fail {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)

  let output =
    Output {
      address: Address {
        payment_credential: VerificationKey(#"ff"),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [output] } }

  simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Output has NoDatum instead of InlineDatum — crashes on expect InlineDatum(raw_datum)
test buy_fails_no_datum() fail {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()

  let output =
    Output {
      address: Address {
        payment_credential: VerificationKey(alice),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price,
      ),
      datum: transaction.NoDatum,
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [output] } }

  simple_dex.spend(Some(order), Buy, utxo, tx)
}

// Buyer pays 150 instead of 100 — overpaying is allowed (>= check)
test buy_succeeds_overpay() {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()
  let tag = mock_tag(utxo)

  let output =
    Output {
      address: Address {
        payment_credential: VerificationKey(alice),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price + 50,
      ),
      datum: InlineDatum(tag),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [output] } }

  simple_dex.spend(Some(order), Buy, utxo, tx)
}

// -- Cancel Tests --

// Owner (alice) signs the transaction — cancel should pass
test cancel_succeeds() {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, extra_signatories: [alice] } }

  simple_dex.spend(Some(order), Cancel, utxo, tx)
}

// No one signed the transaction — cancel should fail
test cancel_fails_no_signature() fail {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()

  // Empty extra_signatories — no signature present
  let tx = transaction.placeholder

  simple_dex.spend(Some(order), Cancel, utxo, tx)
}

// Eve (#"ff") signs instead of alice (#"01") — cancel should fail
test cancel_fails_wrong_signer() fail {
  let alice = #"01"
  let order = mock_order(alice)
  let utxo = mock_utxo()
  let eve = #"ff"

  // Eve's signature is present, but she's not the owner
  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, extra_signatories: [eve] } }

  simple_dex.spend(Some(order), Cancel, utxo, tx)
}

// -- Double Satisfaction Tests --

// Eve spends two of Alice's orders in one transaction but only includes
// one payment output tagged with utxo_a's hash.
// UTxO A passes (tag matches), but UTxO B fails (tag_a != tag_b)
// because each UTxO produces a different blake2b hash.
// Proves the tagging prevents the exploit — marked `fail` because
// the combined result is False.
test double_satisfaction_blocked() fail {
  let alice = #"01"
  let order = mock_order(alice)

  // Two different UTxOs — different tx hashes, so different tags
  let utxo_a = OutputReference { transaction_id: #"0000", output_index: 0 }
  let utxo_b = OutputReference { transaction_id: #"0001", output_index: 0 }

  // Eve tags the payment with utxo_a's tag only
  let tag_a = mock_tag(utxo_a)

  // Single payment output — Eve tries to satisfy both orders with this
  let single_payment =
    Output {
      address: Address {
        payment_credential: VerificationKey(alice),
        stake_credential: None,
      },
      value: assets.from_asset(
        order.ask.policy_id,
        order.ask.asset_name,
        order.price,
      ),
      datum: InlineDatum(tag_a),
      reference_script: None,
    }

  let tx =
    transaction.placeholder
      |> fn(t) { Transaction { ..t, outputs: [single_payment] } }

  // UTxO A: validator computes blake2b(utxo_a) → matches tag_a → passes
  let order_a_passes = simple_dex.spend(Some(order), Buy, utxo_a, tx)
  // UTxO B: validator computes blake2b(utxo_b) → doesn't match tag_a → fails
  let order_b_passes = simple_dex.spend(Some(order), Buy, utxo_b, tx)

  // Combined result is False — Eve can't steal the second order
  order_a_passes && order_b_passes
}
