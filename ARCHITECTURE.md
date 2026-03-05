# SimpleDEX Architecture

## Overview

SimpleDEX is a Cardano-based decentralized exchange with an on-chain smart contract (Aiken/Plutus V3), an off-chain transaction-building API (.NET 10.0), a chain indexer, and a shared data library.

```
┌─────────────────┐     ┌─────────────────────┐     ┌─────────────────┐
│  On-Chain        │     │  SimpleDEX.Offchain  │     │  SimpleDEX.Sync │
│  (Aiken/PlutusV3)│◄────│  (Transaction API)   │     │  (Chain Indexer)│
│                  │     │                      │     │                 │
│  simple_dex.ak   │     │  FastEndpoints       │     │  Argus.Sync     │
│                  │     │  Blockfrost provider  │     │  OrderReducer   │
└─────────────────┘     └──────────┬───────────┘     └────────┬────────┘
                                   │                          │
                                   ▼                          ▼
                         ┌─────────────────────────────────────┐
                         │         SimpleDEX.Data              │
                         │  (Shared Library)                   │
                         │                                     │
                         │  CBOR types · DB models · DbContext │
                         │  EF Core Migrations                 │
                         └─────────────────────────────────────┘
```

---

## 1. On-Chain (Aiken/Plutus V3)

**Path:** `src/onchain/validators/`
**Compiled:** `src/onchain/plutus.json`

Four validator variants govern a simple order book. A seller locks tokens at the script address with an inline datum describing the order. A buyer can fill it or the seller can cancel it.

| Validator | File | Type | Double-Sat Prevention | Batch Support |
|---|---|---|---|---|
| `simple_dex` | `simple_dex.ak` | Spend-only | Datum tagging (blake2b) | No (runs per input) |
| `simple_dex_no_ds` | `simple_dex_no_ds.ak` | Spend-only | None | No |
| `simple_dex_withdraw` | `simple_dex_withdraw.ak` | Withdraw zero | Datum tagging (blake2b) | Yes (logic runs once) |
| `simple_dex_indexed` | `simple_dex_indexed.ak` | Withdraw zero + output indexing | Output-index redeemer | Yes (logic runs once) |

### Types

| Aiken Type | CBOR Constructor | Fields |
|---|---|---|
| `OrderDatum` | Constr(0) | `owner: ByteArray, destination: Address, offer: TokenId, ask: TokenId, price: RationalC` |
| `TokenId` | Constr(0) | `policy_id: PolicyId, asset_name: AssetName` |
| `RationalC` | Constr(0) | `num: Int, den: Int` |
| `Buy` (redeemer) | Constr(0) | none |
| `Cancel` (redeemer) | Constr(1) | none |
| `IndexedBuy` (redeemer) | Constr(0) | `output_index: Int` |
| `IndexedCancel` (redeemer) | Constr(1) | none |

### Buy Redeemer

The validator iterates transaction outputs and requires at least one that satisfies all three checks:

| Check | Condition |
|---|---|
| `is_sent_to_owner` | `output.address == order.owner` |
| `is_price_met` | `quantity_of(output.value, ask.policy_id, ask.asset_name) >= price` |
| `is_correct_tag` | `output.inline_datum == blake2b_256(cbor.serialise(utxo_ref))` |

### Cancel Redeemer

| Check | Condition |
|---|---|
| Owner is key credential | `owner.payment_credential` must be `VerificationKey(pkh)` |
| Owner signed | `list.has(tx.extra_signatories, pkh)` |

No output constraints — the seller can reclaim tokens to any address.

### Double-Satisfaction Prevention

Each UTxO has a unique `OutputReference` (tx hash + output index). The validator computes:

```
tag = blake2b_256(cbor.serialise(output_reference))
```

The Buy branch requires the payment output to carry this tag as its inline datum. Since each order produces a different tag, a single payment output can only satisfy one order validation — preventing an attacker from spending two orders with one payment.

### How Transactions Connect to the Validator

A Cardano transaction is a bundle of inputs (UTxOs being consumed), outputs (new UTxOs being created), and metadata (signers, validity range, etc). When a UTxO locked at a script address is spent, the validator runs and receives the full transaction context:

```
spend(datum, redeemer, utxo_ref, transaction)
```

The validator can inspect everything in the transaction — outputs, inputs, `extra_signatories`, validity range, etc. The redeemer itself carries no logic; it is just a branch selector that tells the validator which code path to run.

#### Buy Transaction Structure

What the buyer builds off-chain (via `BuyTemplate`):

```
Transaction:
  Reference Inputs: [script ref UTxO]            -- validator code lives here
  Inputs:           [order UTxO]                  -- triggers the validator
  Redeemer:         Buy (Constr 0, no fields)     -- selects the Buy branch
  Outputs:
    [0] To: seller  | Value: ask tokens           -- payment
                    | Datum: DatumTag(blake2b(...)) -- anti-double-satisfaction tag
    [1] To: buyer   | Value: offer tokens + change
```

The validator iterates `transaction.outputs` and checks that at least one satisfies all three conditions (correct recipient, sufficient payment, correct tag). The redeemer is just a tag — all security comes from inspecting the transaction outputs.

**Why DatumTag is needed:**

Without it, an attacker could spend two orders from the same seller in one transaction with only one payment output. Both validators would see the same output and both would pass — the seller loses tokens. With the tag, each order's `OutputReference` produces a unique hash, so a single output can only satisfy one validator:

```
Inputs: [order_A, order_B]
Outputs:
  [0] To: seller | Value: 100 ADA | Datum: tag_A   -- only satisfies order_A

Order B's validator computes tag_B, sees tag_A != tag_B, and fails.
```

#### Cancel Transaction Structure

What the seller builds off-chain (via `CancelTemplate`):

```
Transaction:
  Reference Inputs:  [script ref UTxO]
  Inputs:            [order UTxO]
  Redeemer:          Cancel (Constr 1, no fields)
  Required Signers:  [owner_pkh]                  -- explicit authorization
  Outputs:
    [0] To: owner   | Value: offer tokens + change
```

**Why RequiredSigner is needed:**

On Cardano, `extra_signatories` is not automatically populated from who signed the transaction. A wallet signing a transaction adds its key to the witness set (proves the signature is valid), but `extra_signatories` is a separate, explicit field in the transaction body.

If the validator just checked the witness set, any transaction that happens to include the owner's UTxO as a fee input would implicitly have the owner's signature — creating unintended authorization. `extra_signatories` is an intentional declaration: "I explicitly authorize this script action."

The chain of trust:

```
1. CancelTemplate calls .AddRequiredSigner("change")
   -> owner_pkh added to tx.required_signers (tx body field)

2. Cardano ledger enforces:
   -> all required_signers must appear in the witness set
   -> if owner didn't sign, the tx is invalid before the script even runs

3. Validator checks:
   -> list.has(self.extra_signatories, owner_pkh)
   -> confirms the owner explicitly authorized this spend
```

#### Why No Input Amount Checks?

The validator protects the **counterparty**, not the initiator:

- **Buy** protects the **seller** — payment is enforced on-chain (correct recipient, correct amount, correct tag)
- **Cancel** protects the **protocol** — only the owner can reclaim

The initiating party controls transaction construction. If a buyer doesn't route the unlocked offer tokens to themselves, they only hurt themselves. The validator doesn't need to enforce this.

| Redeemer | Protects | Key Mechanism | Off-Chain Requirement |
|---|---|---|---|
| `Buy` | Seller | `DatumTag` on payment output | Compute `blake2b(cbor(outref))`, attach as inline datum |
| `Cancel` | Protocol | `extra_signatories` contains owner PKH | Call `AddRequiredSigner` to populate `required_signers` |

### Withdraw Zero Validators (`simple_dex_withdraw`, `simple_dex_indexed`)

Both withdraw validators use the same two-part architecture:

**Spend handler** — trivial gate that checks the staking credential is present in the transaction's withdrawals:

```aiken
spend(_datum, _redeemer, utxo, self) {
  expect Some(input) = self.inputs |> transaction.find_input(utxo)
  pairs.has_key(self.withdrawals, input.output.address.payment_credential)
}
```

**Withdraw handler** — runs once, iterates all script inputs, deserializes each input's redeemer from `self.redeemers`, and validates:

```aiken
withdraw(_redeemer, account, self) {
  let script_inputs = self.inputs |> list.filter(fn(i) { i.output.address.payment_credential == account })
  list.all(script_inputs, fn(input) {
    // deserialize datum + redeemer, then validate
  })
}
```

The `publish` handler always returns `True` (needed for staking credential registration).

#### `simple_dex_withdraw` — Datum-tagged

Uses `OrderRedeemer` (`Buy`/`Cancel`). The `Buy` branch calls `validate_buy`, which iterates all outputs searching for one with the correct recipient, price, and blake2b datum tag. O(n) per order.

#### `simple_dex_indexed` — Output-index redeemer

Uses `IndexedOrderRedeemer`:
- `IndexedBuy { output_index }` — the buyer's redeemer explicitly names which tx output pays the seller
- `IndexedCancel` — same cancel logic (owner signature check)

The `IndexedBuy` branch calls `validate_buy_indexed`, which uses `list.at(self.outputs, output_index)` for O(1) lookup instead of searching. No datum tag needed — the redeemer itself creates the input-to-output binding.

**Tradeoff:** Output-index redeemers do not prevent double satisfaction on-chain. Two inputs can both point `output_index: 0` at the same output. This is a known tradeoff — the off-chain transaction builder is responsible for assigning unique output indices. The datum-tagged variant (`simple_dex_withdraw`) prevents this on-chain.

### Constraints

- No partial fills — the entire UTxO is consumed
- No minimum ADA enforcement on-chain — handled off-chain
- `else(_)` branch rejects all non-spend script purposes

---

## 2. SimpleDEX.Data (Shared Library)

**Path:** `src/SimpleDEX.Data/`

The bridge between on-chain and off-chain. Referenced by both Offchain and Sync.

### CBOR Types (`Models/Cbor/`)

C# mirrors of the Aiken types, using Chrysalis attributes for compile-time CBOR code generation:

| C# Type | Aiken Equivalent | Usage |
|---|---|---|
| `OrderDatum` | `OrderDatum` | Offchain: build datums. Sync: deserialize from chain. |
| `OrderRedeemer` (Buy/Cancel) | `OrderRedeemer` | Offchain: set redeemers. Sync: determine fill/cancel. |
| `TokenId` | `TokenId` | Token identification (policy ID + asset name). |
| `CborOutRef` | `OutputReference` | Serialized and hashed for the anti-double-satisfaction tag. `[CborIndefinite]` matches Aiken's encoding. |
| `DatumTag` | `ByteArray` | The blake2b hash attached to payment outputs. |
| `PlutusVoid` | `()` | Plutus unit type. |

### DB Models (`Models/`)

**Order:**

```csharp
public record Order(
    string OutRef,        // PK — "{txHash}#{index}"
    string OwnerAddress,  // Bech32 seller address
    string OfferSubject,  // Hex: policyId + assetName
    string AskSubject,    // Hex: policyId + assetName
    ulong Price,          // Ask price
    ulong Slot,           // Creation slot (for rollback deletion)
    OrderStatus Status,   // Open | Filled | Cancelled
    ulong? SpentSlot      // Spending slot (for rollback reversion)
) : IReducerModel;
```

**OrderStatus:**

```csharp
public enum OrderStatus { Open, Filled, Cancelled }
```

### DbContext

`SimpleDEXDbContext` extends Argus.Sync's `CardanoDbContext`, which adds a `ReducerStates` table for tracking chain sync progress per reducer.

### Dependencies

- Chrysalis 1.1.0-alpha (CBOR serialization)
- Argus.Sync 1.0.11-alpha (IReducerModel, CardanoDbContext)
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
- Microsoft.EntityFrameworkCore.Design 10.0.3

---

## 3. SimpleDEX.Sync (Chain Indexer)

**Path:** `src/SimpleDEX.Sync/`

A dedicated chain-follower that connects to a Cardano node via the Ouroboros N2C protocol and populates the database through `OrderReducer`.

### Configuration (`appsettings.json`)

- **Database:** PostgreSQL (`Host=localhost;Database=simpledex;Port=5432`)
- **Schema:** `simpledex`
- **Node:** Unix socket at `/tmp/node.socket`, Preview testnet (magic 2)
- **Active reducer:** `SimpleDEX.Sync.Reducers.OrderReducer`

### OrderReducer

Implements `IReducer<Order>` from Argus.Sync.

#### RollForward (new block)

1. **Collect new orders:** Scan all transaction outputs. If the address matches the configured `ScriptAddress` and has an inline datum, deserialize as `OrderDatum` and insert as `Order(Status=Open, SpentSlot=null)`.

2. **Detect spent orders:** Collect all transaction input outRefs into a dictionary. Query the DB for matching open orders. For each match, deserialize the redeemer:
   - `Buy` → `Status = Filled`
   - `Cancel` → `Status = Cancelled`
   - Set `SpentSlot` to the current block's slot.

3. Single `SaveChangesAsync()` for atomicity. Early exit if the block has no relevant outputs or inputs.

#### RollBackward (chain reorg)

1. **Revert spent orders:** Any order with `SpentSlot >= slot` is reset to `Status = Open, SpentSlot = null`.
2. **Delete new orders:** Any order with `Slot >= slot` is deleted.

Order matters — revert before delete, otherwise you could delete an order that should merely be reopened.

### Dependencies

- Argus.Sync 1.0.11-alpha (IReducer, chain sync)
- SimpleDEX.Data (CBOR types, DbContext, Order model)

---

## 4. SimpleDEX.Offchain (Transaction API)

**Path:** `src/SimpleDEX.Offchain/`

A FastEndpoints web API that builds unsigned Cardano transactions. Uses Blockfrost as the chain data provider and Chrysalis for transaction construction.

### Transaction Lifecycle

```
Client → POST /order, /buy, or /cancel → unsigned tx CBOR hex
Client → POST /submit (unsigned CBOR)  → server signs → submits → tx hash
```

In this preview-testnet setup, the server holds the mnemonic and signs transactions. In production, signing would be client-side.

### Endpoints

| Endpoint | Route | Request | Purpose |
|---|---|---|---|
| Deploy | `POST /api/v1/transactions/deploy` | `ChangeAddress, LockAmount` | Publishes the validator on-chain as a reference script |
| Order | `POST /api/v1/transactions/order` | `ChangeAddress, OfferSubject, OfferAmount, AskSubject, AskPrice` | Locks offer tokens at the script address with `OrderDatum` |
| Buy | `POST /api/v1/transactions/buy` | `BuyerAddress, OrderOutRef, AskSubject, AskPrice` | Spends order UTxO, pays seller with `DatumTag` |
| Cancel | `POST /api/v1/transactions/cancel` | `OrderTxHash, OrderIndex` | Spends order UTxO, owner reclaims tokens |
| SubmitTx | `POST /api/v1/transactions/submit` | `UnsignedTxCborHex` | Signs with server mnemonic (BIP44 `m/1852'/1815'/0'/0/0`) and submits |

### Templates

Templates use Chrysalis `TransactionTemplateBuilder` — a declarative builder that handles coin selection, fee estimation, and change calculation.

| Template | Validator Type | Redeemer | Anti-Double-Sat |
|---|---|---|---|
| `DeployTemplate` | — | — | — |
| `OrderTemplate` | — | — | — |
| `BuyTemplate` | `spend` | `Buy()` | `DatumTag` on output |
| `BuyWithdrawTemplate` | `withdraw` | `Buy()` + withdrawal `PlutusVoid` | `DatumTag` on output |
| `BuyIndexedWithdrawTemplate` | `indexed-withdraw` | `IndexedBuy(outputIndex)` + withdrawal `PlutusVoid` | Output index in redeemer |
| `CancelTemplate` | `spend` | `Cancel()` | — |
| `CancelWithdrawTemplate` | `withdraw` | `Cancel()` + withdrawal `PlutusVoid` | — |
| `CancelIndexedWithdrawTemplate` | `indexed-withdraw` | `IndexedCancel()` + withdrawal `PlutusVoid` | — |

The Buy and Cancel endpoints route to the correct template based on `Validators:{hash}:Type` in config:

```csharp
TransactionTemplate template = validatorType switch
{
    "withdraw"         => BuyWithdrawTemplate.Create(...),
    "indexed-withdraw" => BuyIndexedWithdrawTemplate.Create(...),
    _                  => BuyTemplate.Create(...),
};
```

All buy/cancel templates use `Build(false)` to avoid auto-selecting script inputs and reference the deployed script via `AddReferenceInput` to reduce transaction size.

### Off-Chain ↔ On-Chain Alignment

- `CborOutRef` encoding must match Aiken's `cbor.serialise(OutputReference)` for the tag hash to match
- `CborConstr` indices must match Aiken constructor ordering
- `spend`/`withdraw` validators: Buy endpoint computes `blake2b_256(Serialize(CborOutRef))` and attaches it as `DatumTag`
- `indexed-withdraw` validator: Buy endpoint assigns sequential `output_index` values (0, 1, 2...) matching the output order in the transaction. No `DatumTag` needed.

### Dependencies

- FastEndpoints 8.0.0 (API framework)
- Chrysalis 1.1.0-alpha (tx building, CBOR, wallet)
- Blockfrost provider
- SimpleDEX.Data (CBOR types, DbContext)

---

## End-to-End Flow

```
1. Deploy    → script reference UTxO published on-chain
2. Order     → seller locks tokens at script address (inline OrderDatum)
3. Sync      → OrderReducer indexes it as Status = Open
4. Buy       → buyer pays seller, spends order UTxO (Buy redeemer + DatumTag)
   or Cancel → seller reclaims tokens (Cancel redeemer + required signer)
5. Sync      → OrderReducer updates status to Filled or Cancelled
```

## Order Lifecycle

```
[UTxO created at script address]
        │
        ▼
    Order inserted (Status = Open, SpentSlot = null)
        │
        ├── Buy redeemer ──────► Status = Filled, SpentSlot = slot
        │
        ├── Cancel redeemer ───► Status = Cancelled, SpentSlot = slot
        │
        └── Chain rollback ────► SpentSlot >= slot: revert to Open
                                 Slot >= slot: delete record
```

See [SMART_CONTRACT_PATTERNS.md](SMART_CONTRACT_PATTERNS.md) for advanced Cardano smart contract patterns (double satisfaction, withdraw zero trick, UTxO indexing).
See [OFFCHAIN_AND_SYNC.md](OFFCHAIN_AND_SYNC.md) for comprehensive line-by-line documentation of the off-chain API and chain indexer.
