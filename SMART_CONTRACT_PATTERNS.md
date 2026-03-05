# Cardano Smart Contract Patterns

Common design patterns and vulnerabilities for Plutus/Aiken smart contracts on Cardano's eUTxO model.

## High-Level Overview

Cardano uses the **extended UTxO (eUTxO) model**. Unlike Ethereum where contracts hold state and execute code, Cardano validators are **stateless functions** that observe a transaction and return pass/fail. This has fundamental implications:

- Validators **validate**, they don't **execute**. They can't make transfers — they can only check that the transaction meets their conditions.
- Each validator runs in **isolation**. It sees the full transaction but doesn't know what other validators are checking.
- All transaction inputs, outputs, and metadata are **determined before submission**. There are no runtime surprises.

These properties create unique vulnerabilities (double satisfaction) but also unique optimizations (withdraw zero, UTxO indexing) that don't exist in account-based models.

```
┌─────────────────────────────────────────────────────┐
│                    Transaction                       │
│                                                      │
│  Inputs:   [utxo_A, utxo_B, utxo_C]                │
│  Outputs:  [out_0, out_1, out_2]                    │
│  Signers:  [pkh_1, pkh_2]                           │
│  Withdrawals: [staking_cred → 0 ADA]               │
│                                                      │
│  ┌──────────────┐  ┌──────────────┐                 │
│  │ Validator A   │  │ Validator B   │                │
│  │ sees ENTIRE   │  │ sees ENTIRE   │                │
│  │ transaction   │  │ transaction   │   (isolation)  │
│  │ returns ✓/✗   │  │ returns ✓/✗   │                │
│  └──────────────┘  └──────────────┘                 │
│                                                      │
│  ALL validators must pass for the tx to be valid     │
└─────────────────────────────────────────────────────┘
```

## SimpleDEX Example: All Patterns Combined

Here's how SimpleDEX's `simple_dex_indexed` validator batch-fills 3 orders in a single transaction:

```
simple_dex_indexed (batch 3 orders in one tx):
  Inputs:
    [0] order_A → spend: IndexedBuy { output_index: 0 }  → "staking cred in withdrawals?" ✓
    [1] order_B → spend: IndexedBuy { output_index: 1 }  → "staking cred in withdrawals?" ✓
    [2] order_C → spend: IndexedBuy { output_index: 2 }  → "staking cred in withdrawals?" ✓

  Outputs:
    [0] payment to seller_A
    [1] payment to seller_B
    [2] payment to seller_C

  Withdrawal: 0 ADA from staking script (PlutusVoid redeemer)
    Staking validator runs ONCE, iterates script_inputs with list.all:
      for each input:
        1. Deserialize OrderDatum from input's inline datum
        2. Look up spend redeemer from self.redeemers[Spend(utxo)]
        3. IndexedBuy { output_index } →
             output = list.at(self.outputs, output_index)     (O(1) lookup)
             Verify output.address == order.destination
             Verify quantity_of(output.value, ask) >= required_payment
        4. IndexedCancel →
             Verify list.has(self.extra_signatories, order.owner)

  Result: 3 orders filled in 1 tx, validator logic runs once, O(1) lookups.
```

Compared to `simple_dex` (spend-only): each order would run the full validator with an O(n) output search — 3 inputs = 3x cost.

| Pattern | Role in the Example |
|---|---|
| **Withdraw Zero** | Business logic runs once in the staking validator, not 3x in the spending validator |
| **UTxO Indexing** | Each `IndexedBuy` redeemer carries `output_index` — direct O(1) access via `list.at` |
| **No Datum Tagging** | Known tradeoff: double satisfaction not prevented on-chain (off-chain builder assigns unique indices) |

---

## Double Satisfaction

The most commonly found vulnerability in Cardano smart contract audits. It exploits the fact that validators **validate** rather than **execute** — they can only check that their conditions are met somewhere in the transaction, but cannot distinguish whether an output was "meant" for them.

### The Attack

```
Scenario: Two orders from Alice, each demanding 100 ADA payment.

Honest transaction (two separate buys):
  Input:  order_A (100 tokens)     Input:  order_B (100 tokens)
  Output: Alice gets 100 ADA       Output: Alice gets 100 ADA
  Total cost to buyer: 200 ADA

Attack transaction (one buy satisfies both):
  Inputs:  [order_A, order_B]      -- spend both in one tx
  Outputs: [Alice gets 100 ADA]    -- only ONE payment

  order_A's validator: "Is there an output to Alice with >= 100 ADA?" → YES ✓
  order_B's validator: "Is there an output to Alice with >= 100 ADA?" → YES ✓
  Both pass. Attacker gets 200 tokens for 100 ADA.
```

### Why This Happens

In Ethereum's account model, smart contracts **execute transfers** — two contracts making two transfers results in two transfers. In Cardano's eUTxO model, validators can only **observe** the transaction and check conditions. They cannot distinguish whether an output was placed there for them or for another validator.

### Prevention Techniques

**1. Datum Tagging (what SimpleDEX uses)**

Attach a unique identifier to each payment output. The validator checks that the output's datum matches a hash derived from the UTxO being spent:

```
tag = blake2b_256(cbor.serialise(output_reference))
```

Since each UTxO has a globally unique `OutputReference`, each order produces a different tag. A single output can only carry one datum, so it can only satisfy one validator.

```
Inputs: [order_A, order_B]
Outputs:
  [0] To: Alice | 100 ADA | Datum: tag_A   -- satisfies order_A only

Order A validator: tag_A == tag_A → ✓
Order B validator: tag_A == tag_B → ✗ (different OutputReference → different hash)
```

The attacker must add a second properly-tagged payment output to satisfy both validators.

**2. Forbidding Multiple Script Inputs**

The validator rejects transactions that spend more than one script UTxO. Simple but prevents legitimate batching.

**3. Unique Addresses**

Use different payment addresses for each interaction. If each order expects payment to a different address, no single output can satisfy both. Wallets naturally generate unique addresses, but this doesn't help when the same seller creates multiple orders.

**4. UTxO Indexing (see below)**

Pair each input with a specific output via indices in the redeemer, creating a 1:1 mapping that prevents sharing.

---

## Withdraw Zero Trick (Stake Validator Pattern)

A pattern that offloads protocol business logic from spending validators to staking validators, solving scalability issues with multi-UTxO transactions.

### The Problem

When multiple UTxOs are spent from the same script in one transaction, the spending validator runs **once per UTxO**. If the validator contains complex business logic, this quickly exhausts the transaction's CPU/memory budget:

```
Traditional approach (3 inputs):
  Input 1 → full validator logic runs     (expensive)
  Input 2 → full validator logic runs     (expensive)
  Input 3 → full validator logic runs     (expensive)
  Total: 3x the cost
```

### The Solution

Split validation into two parts:

1. **Spending validator** — stripped down to a single check: "Is the staking credential present in this transaction's withdrawals?"
2. **Staking validator** — runs **once per transaction**, contains all the complex business logic

```
Withdraw zero approach (3 inputs):
  Input 1 → "is staking cred in withdrawals?" (cheap)
  Input 2 → "is staking cred in withdrawals?" (cheap)
  Input 3 → "is staking cred in withdrawals?" (cheap)
  Staking validator → full business logic   (runs once)
  Total: 1x the cost + 3x trivial checks
```

### How It Works

The spending validator becomes minimal:

```haskell
mkValidator stakingCred _datum _redeemer ctx =
  case lookup stakingCred (txInfoWdrl txinfo) of
    Just _  -> True      -- staking validator ran, we're good
    Nothing -> error ()  -- staking validator didn't run, reject
```

The trick relies on an implementation detail: the Cardano ledger does **not** filter out zero-amount entries in `txInfoWdrl` (unlike `txInfoMint` which filters zero quantities). So you can withdraw 0 lovelace from the staking credential, which triggers the staking validator without actually withdrawing any rewards.

### Architecture

```
Transaction
├─ Input 1 (spending script) ──→ checks staking cred in withdrawals ──┐
├─ Input 2 (spending script) ──→ checks staking cred in withdrawals ──┤
├─ Input 3 (spending script) ──→ checks staking cred in withdrawals ──┤
│                                                                      │
└─ Withdrawal: 0 ADA from staking script ═════════════════════════════
                (runs ONCE, executes ALL business logic)
```

### Advantages

- **Performance**: Complex logic runs once instead of N times
- **Script size**: Spending validator is tiny, reducing tx size
- **Batching**: Enables processing many UTxOs in a single transaction
- **Separation of concerns**: Input authorization vs business logic are cleanly split

### When to Use

- Protocols that batch multiple UTxO spends in one transaction
- Complex validation logic shared across inputs
- Resource-constrained transactions approaching execution limits
- DEXs, lending protocols, or any multi-user settlement

### When NOT to Use

- Single-UTxO transactions (no benefit, adds complexity)
- Simple validators where the logic is already cheap
- When the spending validator needs input-specific logic that can't be generalized

### Future: CIP-0112 (Observe Script Type)

The withdraw zero trick relies on an obscure ledger mechanic. CIP-0112 proposes a cleaner alternative: `required_observers`, a new transaction field that explicitly lists scripts that must validate the transaction — no need for the withdrawal workaround.

---

## UTxO Indexing (Output Indexing)

A pattern that eliminates expensive on-chain searches by moving index computation off-chain and passing indices through the redeemer.

### The Problem

Validators often need to find a specific input or output in the transaction. The naive approach iterates through the entire list:

```aiken
-- O(n) search: iterate all outputs to find the one we care about
list.any(tx.outputs, fn(output) { output.address == target && ... })
```

This is wasteful — the transaction builder already knows which output is which. Why make the validator search for it again?

### The Solution

Pass the index directly in the redeemer:

```
Redeemer: Buy { output_index: 0 }

-- O(1) lookup: go directly to the output
let output = list.at(tx.outputs, redeemer.output_index)
expect output.address == target && ...
```

The off-chain code knows the transaction structure, computes the correct index, and embeds it in the redeemer. The on-chain code trusts the index but **verifies** the element at that position meets all requirements.

### For Multiple Inputs/Outputs

The redeemer can carry pairs of indices mapping each input to its corresponding output:

```
Redeemer: BatchSwap {
  pairs: [(0, 0), (1, 1), (2, 2)]  -- input_index → output_index
}
```

### Security: Duplicate Index Attack

A malicious redeemer could provide `[(0, 0), (0, 0)]` — pointing two inputs to the same output. The validator processes both "successfully" but one output does double duty (another form of double satisfaction).

Prevention: check that all indices are unique, typically via a bitmask:

```
seen = 0
for each index:
  if bit(seen, index) is set → reject (duplicate)
  seen = set_bit(seen, index)
```

### Important: Input Ordering

Cardano sorts transaction inputs lexicographically (by tx hash, then output index) **before** passing them to validators. The off-chain code must sort inputs the same way when computing indices, or the mapping will be wrong.

### Advantages

- **Performance**: O(1) lookup instead of O(n) search
- **Budget savings**: Significant reduction in CPU/memory for validators with many outputs
- **Composability**: Works well with the withdraw zero trick — the staking validator can process all index pairs in one execution

### When to Use

- Any validator that searches through inputs/outputs
- Batch processing (DEX order matching, multi-user settlements)
- Performance-critical validators near execution budget limits
- Combined with the withdraw zero trick for maximum efficiency

---

## Combining Patterns

These patterns are most powerful when combined. SimpleDEX demonstrates two combinations:

### Withdraw Zero + Datum Tagging (`simple_dex_withdraw`)

```
Logic runs once. Each Buy still does O(n) output search, but uses datum tags for on-chain
double-satisfaction prevention. Best when correctness > performance.
```

### Withdraw Zero + Output Indexing (`simple_dex_indexed`)

```
Logic runs once. Each IndexedBuy does O(1) lookup via list.at(outputs, output_index).
No datum tags — off-chain builder ensures unique indices. Best for maximum throughput.
```

A production DEX wanting both guarantees could add a bitmask check to the indexed variant — rejecting duplicate `output_index` values in the same transaction. SimpleDEX intentionally omits this to keep the validator minimal and demonstrate the tradeoff.

### How SimpleDEX Relates

SimpleDEX implements all three patterns across its four validator variants:

| Pattern | Validator | Status | Implementation |
|---|---|---|---|
| Datum Tagging | `simple_dex` | Implemented | `blake2b_256(cbor(output_reference))` on payment output |
| Datum Tagging | `simple_dex_withdraw` | Implemented | Same, via withdraw zero |
| Withdraw Zero | `simple_dex_withdraw` | Implemented | Spend gate delegates to staking validator |
| Withdraw Zero | `simple_dex_indexed` | Implemented | Spend gate delegates to staking validator |
| UTxO Indexing | `simple_dex_indexed` | Implemented | `IndexedBuy { output_index }` redeemer, `list.at` lookup |
| No DS Prevention | `simple_dex_no_ds` | Implemented | Intentionally omitted (reference/educational) |
| CIP-0112 Observe | — | Not available yet | Cleaner alternative to withdraw zero when adopted |

The `simple_dex_indexed` validator combines all three patterns (withdraw zero + output indexing + batch support) for maximum efficiency. It trades on-chain double-satisfaction prevention for O(1) output lookup — the off-chain transaction builder assigns unique output indices.

---

## The Cardano Ledger

The ledger is the **state machine implemented in the Cardano node software** — Haskell code in the `cardano-ledger` repository that defines the rules for what makes a transaction valid.

When a transaction is submitted, the node runs it through a series of validation rules written as pure Haskell functions:

- Do the inputs exist in the current UTxO set?
- Do the signatures match the required credentials?
- Is the fee sufficient?
- Do all referenced scripts pass?
- Are mint/burn policies satisfied?
- Are withdrawal credentials authorized?

This is not a smart contract. It's the **node binary** that every stake pool operator and relay runs. These rules are baked into the software and can only change via hard forks.

When we say "the ledger enforces that scripts in withdrawals must execute," there is literally a Haskell function in `cardano-ledger` that iterates over the withdrawals map, finds script credentials, and invokes the Plutus evaluator on each one. If any returns `False`, the transaction is marked invalid. Every node on the network runs the same code, so they all agree on what's valid. That's consensus.

### How Withdrawals Enforce Script Execution

The `withdrawals` field is a **request to the ledger** to move ADA from a reward account. The ledger processes it:

1. Transaction says: "I want to withdraw X ADA from reward account belonging to credential C"
2. Ledger checks: "Does credential C authorize this?"
   - Key credential → is there a matching signature?
   - Script credential → execute the script, does it return `True`?
3. If not authorized → entire transaction is invalid, rejected at phase 2 validation

The ledger doesn't know upfront if a script will pass. It **executes the script to find out**:

1. Node sees `withdrawals: [Pair(Script(hash), 0)]`
2. Node looks up the script with that hash (from tx witness set or reference script)
3. Node executes the script with purpose = `Withdraw`, passing the transaction context
4. Script returns `True` → withdrawal authorized
5. Script returns `False` or crashes → transaction fails phase 2 validation

This is the same mechanism as spending a script UTxO. The ledger doesn't trust anything — it runs the code and checks the boolean result.

### Why Registration is Required

The withdrawals map references reward accounts. A reward account must exist in the ledger state before you can withdraw from it (even 0 ADA). Reward accounts are created via stake registration certificates. For a script credential, submitting this certificate triggers the `publish` purpose — the ledger calls the validator to authorize the registration. Without a `publish` handler, registration fails and the reward account never exists, making withdraw-zero impossible.

---

## Merkelized Validators

### The Problem

Plutus scripts have a size limit and an execution budget. Complex validators with many code paths (buy, partial buy, cancel, update price, admin override, etc.) can exceed these limits. The entire script gets loaded into memory even if only one code path runs.

### The Idea

Instead of one monolithic validator containing all logic, **split** the logic into separate smaller scripts. The main validator doesn't contain the actual validation logic — it just verifies that the correct logic script **was executed** in the same transaction.

The name comes from Merkle trees: you can commit to a set of possible validation functions via their hashes, and the main validator only needs to check "was the correct function hash executed?" rather than containing all the functions inline.

### How It Works

Each logic branch becomes its own standalone validator deployed separately on-chain:

- **Main validator** — sits on the script address where UTxOs live, knows the hashes of approved logic scripts. Very small — just checks "did the right logic script run?"
- **Buy script** — standalone withdraw validator, contains only buy validation logic
- **Cancel script** — standalone withdraw validator, contains only cancel validation logic

They communicate through the transaction structure — the ledger guarantees that if a script hash is in `withdrawals`, that script ran and passed.

#### Without Merkelization (Combined Validator)

```
spend: check withdrawal exists
withdraw:
  for each input:
    parse redeemer
    if Buy → validate payment, recipient, tag, quantity
    if Cancel → validate owner signature
```

All logic lives in one script. The compiled script contains both Buy and Cancel code paths, even if only one runs.

#### With Merkelization (Split Validators)

**Main validator:**
```
withdraw:
  for each input:
    parse redeemer → get logic_script_hash
    check logic_script_hash is in approved_hashes
    check logic_script_hash is in tx.withdrawals
```

**Buy logic script:**
```
withdraw:
  for each script input where redeemer points to me:
    validate payment, recipient, tag, quantity
```

**Cancel logic script:**
```
withdraw:
  for each script input where redeemer points to me:
    validate owner signature
```

#### Transaction Structure (Buy Example)

```
inputs: [script_utxo_1, script_utxo_2]
withdrawals: [
  Pair(Script(main_hash), 0),       -- triggers main validator
  Pair(Script(buy_logic_hash), 0),  -- triggers buy logic script
]
redeemers: [
  Spend(utxo_1) → { logic: buy_logic_hash },
  Spend(utxo_2) → { logic: buy_logic_hash },
]
```

The ledger:
1. Runs each `spend` → checks main withdrawal exists ✓
2. Runs `withdraw` for `main_hash` → checks `buy_logic_hash` is approved and is in withdrawals ✓
3. Runs `withdraw` for `buy_logic_hash` → validates all the buy logic ✓

The cancel script is **not included** in this transaction at all. It doesn't get loaded, doesn't take up space, doesn't cost execution units.

### Why "Merkle"?

The simplest approach is storing approved hashes as a list: `[buy_hash, cancel_hash, update_hash, ...]`. But with many logic branches, you can store them as a **Merkle tree root** instead. The redeemer includes the script hash plus a Merkle proof. The main validator verifies the proof against the root:

```
        root_hash          ← stored in datum or parameter
       /         \
    hash_ab     hash_cd
    /    \      /    \
  buy  cancel  update  partial_buy   ← actual script hashes (leaves)
```

To use the buy script, the redeemer provides: `buy_hash` + proof `[cancel_hash, hash_cd]`. The main validator reconstructs the root and checks it matches — keeping the datum small regardless of branch count.

### Deployment

1. Deploy buy logic script → get `buy_hash`
2. Deploy cancel logic script → get `cancel_hash`
3. Deploy main validator **parameterized** with `[buy_hash, cancel_hash]` → get `main_hash` and script address

If you update any logic script, the main validator must be redeployed with the new hash, which changes the script address. All existing UTxOs on the old address are on a different validator.

The alternative is storing approved hashes in the **datum** instead of as a parameter — the main validator's hash stays stable, but every UTxO must include the approved hashes and you need governance logic to update them.

### When It's Worth It

- **Not worth it** for 2-3 simple code paths — the overhead of multiple scripts and Merkle proofs costs more than it saves
- **Worth it** when you have many complex code paths, or when individual paths are large enough that combining them hits script size limits

---

## Sources

- [Anastasia Labs Design Patterns](https://github.com/Anastasia-Labs/design-patterns)
- [Stake Validator Pattern](https://github.com/Anastasia-Labs/design-patterns/blob/main/stake-validator/STAKE-VALIDATOR.md)
- [UTxO Indexers Pattern](https://github.com/Anastasia-Labs/design-patterns/blob/main/utxo-indexers/UTXO-INDEXERS.md)
- [Plutonomicon: Stake Scripts](https://github.com/Plutonomicon/plutonomicon/blob/main/stake-scripts.md)
- [CIP-0112: Observe Script Type](https://cips.cardano.org/cip/CIP-0112)
- [Double Satisfaction Vulnerability (Vacuumlabs)](https://medium.com/@vacuumlabs_auditing/cardano-vulnerabilities-1-double-satisfaction-219f1bc9665e)
- [Merkelized Validators Pattern](https://github.com/Anastasia-Labs/design-patterns/tree/main/merkelized-validators)
