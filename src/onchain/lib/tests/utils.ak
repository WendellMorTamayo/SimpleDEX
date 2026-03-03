use aiken/cbor
use aiken/crypto
use cardano/address.{Address, Script, VerificationKey}
use cardano/assets
use cardano/transaction.{InlineDatum, Input, Output, OutputReference}
use types.{OrderDatum, TokenId, RationalC}

pub fn mock_ask() -> TokenId {
  TokenId { policy_id: #"cc", asset_name: #"dd" }
}

pub fn mock_order(owner_pkh: ByteArray, destination: Address) -> OrderDatum {
  OrderDatum {
    owner: owner_pkh,
    destination,
    offer: TokenId { policy_id: #"aa", asset_name: #"bb" },
    ask: mock_ask(),
    price: RationalC { num: 1, den: 1 },
  }
}

pub fn mock_input(
  utxo: OutputReference,
  order: OrderDatum,
  offer_quantity: Int,
) -> Input {
  Input {
    output_reference: utxo,
    output: Output {
      address: mock_address(#"00"),
      value: assets.from_asset(
        order.offer.policy_id,
        order.offer.asset_name,
        offer_quantity,
      ),
      datum: InlineDatum(Void),
      reference_script: None,
    },
  }
}

pub fn mock_address(pkh: ByteArray) -> Address {
  Address { payment_credential: VerificationKey(pkh), stake_credential: None }
}

pub fn mock_utxo() -> OutputReference {
  OutputReference { transaction_id: #"0000", output_index: 0 }
}

pub fn mock_tag(utxo: OutputReference) -> ByteArray {
  utxo
    |> cbor.serialise()
    |> crypto.blake2b_256()
}

pub fn mock_script_address(hash: ByteArray) -> Address {
  Address { payment_credential: Script(hash), stake_credential: None }
}

pub fn mock_script_input(
  utxo: OutputReference,
  order: OrderDatum,
  offer_quantity: Int,
  script_hash: ByteArray,
) -> Input {
  Input {
    output_reference: utxo,
    output: Output {
      address: mock_script_address(script_hash),
      value: assets.from_asset(
        order.offer.policy_id,
        order.offer.asset_name,
        offer_quantity,
      ),
      datum: InlineDatum(order),
      reference_script: None,
    },
  }
}
