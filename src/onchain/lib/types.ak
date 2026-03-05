use cardano/address.{Address}
use cardano/assets.{AssetName, PolicyId}

// Identifies a token type (no amount — amount lives in the UTxO value itself)
pub type TokenId {
  policy_id: PolicyId,
  asset_name: AssetName,
}

// Token identifier with quantity — used for offer to declare amount in datum
pub type Unit {
  policy_id: PolicyId,
  asset_name: AssetName,
  quantity: Int,
}

// Rational number as num/den pair
pub type RationalC {
  num: Int,
  den: Int,
}

// The order listing stored as inline datum on the script UTxO
pub type OrderDatum {
  // Seller's PKH — used for cancel authorization (extra_signatories check)
  owner: ByteArray,
  // Seller's full address — receives payment on Buy
  destination: Address,
  // What token is being sold and how much (enforced to match UTxO value)
  offer: Unit,
  // What token the seller wants in return
  ask: TokenId,
  // Rational price: buyer pays ceil(offer_qty * num / den) of ask tokens
  price: RationalC,
}

pub type OrderRedeemer {
  // Buyer specifies how many offer tokens they want to purchase
  Buy
  // Owner cancels and reclaims — no extra data needed
  Cancel
}

pub type IndexedOrderRedeemer {
  IndexedBuy { output_index: Int }
  IndexedCancel
}

pub type MerkelizedOrderRedeemer {
  MerkelizedBuy { output_index: Int }
  MerkelizedCancel
}
