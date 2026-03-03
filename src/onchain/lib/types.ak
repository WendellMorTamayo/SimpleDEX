use cardano/address.{Address}
use cardano/assets.{AssetName, PolicyId}

// Identifies a token type (no amount — amount lives in the UTxO value itself)
pub type TokenId {
  policy_id: PolicyId,
  asset_name: AssetName,
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
  // What token is being sold (the UTxO value holds the actual amount)
  offer: TokenId,
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
