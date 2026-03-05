# Deploying the Merkelized Validator

## Prerequisites

- `aiken` CLI installed (v1.1.21+)
- Off-chain API running (`dotnet run` in `src/SimpleDEX.Offchain/`)
- Funded wallet on Preview testnet
- `bun` installed (for stake registration)

## Step 1: Build

```sh
cd src/onchain
aiken build
```

This compiles all validators into `plutus.json`.

## Step 2: Get logic validator hashes

```sh
aiken blueprint hash -m buy_logic -v buy_logic
# → b108c2482f1d2aa401c7393cd0d860db9b143cf12bad7228bf32f20d

aiken blueprint hash -m cancel_logic -v cancel_logic
# → 92578e242eea1c90294f5636e9c3e70760be222556bb384bfce71a4b
```

These have no parameters — they're ready to deploy as-is.

## Step 3: Apply parameters to main validator

The main validator needs both logic hashes baked in. Apply one at a time (order matches the function signature: `buy_hash` first, `cancel_hash` second).

The `581c` prefix is CBOR encoding for a 28-byte bytestring (`58` = bytestring, `1c` = 28 in hex).

```sh
# Apply buy_hash (first parameter)
aiken blueprint apply \
  -m simple_dex_merkelized \
  -v simple_dex_merkelized \
  -o plutus-step1.json \
  "581cb108c2482f1d2aa401c7393cd0d860db9b143cf12bad7228bf32f20d"

# Apply cancel_hash (second parameter)
aiken blueprint apply \
  -i plutus-step1.json \
  -m simple_dex_merkelized \
  -v simple_dex_merkelized \
  -o plutusFinal.json \
  "581c92578e242eea1c90294f5636e9c3e70760be222556bb384bfce71a4b"

# Clean up intermediate file
rm plutus-step1.json
```

## Step 4: Verify the parameterized hash

```sh
aiken blueprint hash -i plutusFinal.json -m simple_dex_merkelized -v simple_dex_merkelized
# → 5a0674144486a10c79614d58903a51c789709186ef430728c39f09fa
```

No remaining parameters — the validator is fully applied and ready to deploy.

## Step 5: Update PlutusJsonPath config

Point the off-chain API to `plutusFinal.json` so all deploys use the parameterized blueprint. It contains every validator — buy_logic and cancel_logic are identical to `plutus.json`, and simple_dex_merkelized now has its parameters applied.

In `appsettings.json`:

```json
"PlutusJsonPath": "../../src/onchain/plutusFinal.json"
```

## Step 6: Deploy validators as reference scripts

Deploy order: buy_logic and cancel_logic first (no dependencies), then the parameterized main.

Replace `<YOUR_ADDRESS>` with your funded Preview testnet address.

### 6a. Deploy buy_logic

```sh
curl -X POST http://localhost:5000/api/v1/transactions/deploy \
  -H "Content-Type: application/json" \
  -d '{
    "ChangeAddress": "<YOUR_ADDRESS>",
    "ValidatorName": "buy_logic.buy_logic.withdraw"
  }'

# Sign and submit (use the UnsignedTxCborHex from above)
curl -X POST http://localhost:5000/api/v1/transactions/submit \
  -H "Content-Type: application/json" \
  -d '{ "UnsignedTxCborHex": "<UNSIGNED_TX_CBOR>" }'
```

Save the returned `TxHash` as `BUY_REF_TX`.

### 6b. Deploy cancel_logic

```sh
curl -X POST http://localhost:5000/api/v1/transactions/deploy \
  -H "Content-Type: application/json" \
  -d '{
    "ChangeAddress": "<YOUR_ADDRESS>",
    "ValidatorName": "cancel_logic.cancel_logic.withdraw"
  }'

curl -X POST http://localhost:5000/api/v1/transactions/submit \
  -H "Content-Type: application/json" \
  -d '{ "UnsignedTxCborHex": "<UNSIGNED_TX_CBOR>" }'
```

Save the returned `TxHash` as `CANCEL_REF_TX`.

### 6c. Deploy parameterized main

```sh
curl -X POST http://localhost:5000/api/v1/transactions/deploy \
  -H "Content-Type: application/json" \
  -d '{
    "ChangeAddress": "<YOUR_ADDRESS>",
    "ValidatorName": "simple_dex_merkelized.simple_dex_merkelized.spend"
  }'

curl -X POST http://localhost:5000/api/v1/transactions/submit \
  -H "Content-Type: application/json" \
  -d '{ "UnsignedTxCborHex": "<UNSIGNED_TX_CBOR>" }'
```

Save the returned `TxHash` as `MAIN_REF_TX`.

## Step 7: Register staking credentials

The withdraw-zero pattern requires staking credentials to be registered on-chain. Without this, `Withdraw(Script(hash))` entries in transactions would be rejected.

Using reference inputs from the deployed scripts in Step 6:

```sh
cd scripts

# Register buy_logic staking credential
bun register-stake.ts buy_logic.buy_logic.withdraw "<BUY_REF_TX>#0"

# Register cancel_logic staking credential
bun register-stake.ts cancel_logic.cancel_logic.withdraw "<CANCEL_REF_TX>#0"
```

## Step 8: Update appsettings.json

Add entries for all 3 validators in the `Validators` section:

```json
{
  "Validators": {
    "87568b508db8959a979eba197a75af9100b9a1b965405febbbebd011": {
      "Type": "merkelized-buy-logic",
      "ScriptRef": {
        "TxHash": "<BUY_REF_TX>",
        "TxIndex": 0
      }
    },
    "622aa0c964fa8fb64ba4f72808510f34ac73258723b028a22313db52": {
      "Type": "merkelized-cancel-logic",
      "ScriptRef": {
        "TxHash": "<CANCEL_REF_TX>",
        "TxIndex": 0
      }
    },
    "07d3f5a3889578718a30137f4473dbe171dbb1efd167aad2f4594244": {
      "Type": "merkelized-main",
      "Address": "<CONTRACT_ADDRESS from Step 5c response>",
      "ScriptRef": {
        "TxHash": "<MAIN_REF_TX>",
        "TxIndex": 0
      }
    }
  }
}
```

## Summary

| Validator | Hash | Params | Source |
|-----------|------|--------|--------|
| buy_logic | `87568b50...` | None | `plutusFinal.json` |
| cancel_logic | `622aa0c9...` | None | `plutusFinal.json` |
| simple_dex_merkelized | `07d3f5a3...` | buy_hash + cancel_hash applied | `plutusFinal.json` |
