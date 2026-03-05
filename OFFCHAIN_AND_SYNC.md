# SimpleDEX Off-Chain API & Chain Sync

Comprehensive line-by-line documentation of the off-chain transaction-building API (`SimpleDEX.Offchain`), the chain indexer (`SimpleDEX.Sync`), and the shared data library (`SimpleDEX.Data`).

---

## Table of Contents

1. [SimpleDEX.Data — Shared Library](#1-simpledexdata--shared-library)
2. [SimpleDEX.Offchain — Transaction API](#2-simpledexoffchain--transaction-api)
3. [SimpleDEX.Sync — Chain Indexer](#3-simpledexsync--chain-indexer)
4. [Configuration](#4-configuration)

---

## 1. SimpleDEX.Data — Shared Library

**Path:** `src/SimpleDEX.Data/`

The bridge between on-chain and off-chain. Referenced by both Offchain and Sync projects.

### 1.1 CBOR Types (`Models/Cbor/`)

C# mirrors of the Aiken types using Chrysalis attributes for compile-time CBOR code generation. Every `[CborSerializable]` record gets a source-generated serializer. `[CborConstr(N)]` maps to Plutus constructor index N.

#### `OrderDatum.cs`

```csharp
[CborSerializable]
[CborConstr(0)]                               // Plutus Constr(0) — matches Aiken's OrderDatum
public partial record OrderDatum(
    byte[] Owner,                              // Field 0: seller's payment key hash (28 bytes)
    Address Destination,                       // Field 1: seller's full Plutus address for receiving payment
    TokenId Offer,                             // Field 2: token being sold
    TokenId Ask,                               // Field 3: token the seller wants
    RationalC Price                            // Field 4: price as numerator/denominator
) : CborBase;
```

- `Owner` is a raw PKH (not a full address) — used on-chain for `extra_signatories` cancel check.
- `Destination` is the full Plutus `Address` — used for payment routing on Buy.
- Split design: the validator needs the PKH for efficient signer checks, but payments go to the full address (which may include a staking credential).

#### `OrderRedeemer.cs`

```csharp
// --- Redeemers for simple_dex and simple_dex_withdraw validators ---

[CborSerializable]
[CborUnion]                                    // Discriminated union — serialized by constructor index
public abstract partial record OrderRedeemer : CborBase;

[CborSerializable]
[CborConstr(0)]                                // Constr(0) = Buy, no fields
public partial record Buy : OrderRedeemer;

[CborSerializable]
[CborConstr(1)]                                // Constr(1) = Cancel, no fields
public partial record Cancel : OrderRedeemer;

// --- Redeemers for simple_dex_indexed validator ---

[CborSerializable]
[CborUnion]
public abstract partial record IndexedOrderRedeemer : CborBase;

[CborSerializable]
[CborConstr(0)]                                // Constr(0) = IndexedBuy, one field
public partial record IndexedBuy(
    ulong OutputIndex                          // Field 0: index into tx.outputs for the payment
) : IndexedOrderRedeemer;

[CborSerializable]
[CborConstr(1)]                                // Constr(1) = IndexedCancel, no fields
public partial record IndexedCancel : IndexedOrderRedeemer;
```

Two separate union hierarchies:
- `OrderRedeemer` (`Buy`/`Cancel`) — used by `simple_dex`, `simple_dex_withdraw`, and the sync reducer for status detection.
- `IndexedOrderRedeemer` (`IndexedBuy`/`IndexedCancel`) — used exclusively by `simple_dex_indexed`. `IndexedBuy` carries `OutputIndex` telling the validator which output pays the seller.

#### `TokenId.cs`

```csharp
[CborSerializable]
[CborConstr(0)]
public partial record TokenId(
    byte[] PolicyId,                           // 28-byte policy hash (empty for ADA)
    byte[] AssetName                           // Asset name bytes (empty for ADA)
) : CborBase;
```

#### `RationalC.cs`

```csharp
[CborSerializable]
[CborConstr(0)]
public partial record RationalC(
    ulong Num,                                 // Numerator
    ulong Den                                  // Denominator
) : CborBase;
```

Price representation. Required payment = `ceil(offer_qty * Num / Den)`. Ceiling division on-chain: `(offer_qty * num + den - 1) / den`.

#### `CborOutRef.cs`

```csharp
[CborSerializable]
[CborIndefinite]                               // Indefinite-length CBOR encoding to match Aiken's cbor.serialise()
[CborConstr(0)]
public partial record CborOutRef(
    [CborOrder(0)] byte[] Id,                  // Transaction hash
    [CborOrder(1)] ulong Index                 // Output index
) : CborBase;
```

Used to compute the anti-double-satisfaction tag: `blake2b_256(CborSerializer.Serialize(CborOutRef))`. The `[CborIndefinite]` attribute is critical — it must produce the same byte encoding as Aiken's `cbor.serialise(OutputReference)` for the hash to match on-chain.

#### `DatumTag.cs`

```csharp
[CborSerializable]
public partial record DatumTag(
    byte[] Tag                                 // 32-byte blake2b hash of CborOutRef
) : CborBase;
```

Attached as inline datum to payment outputs in the `simple_dex` and `simple_dex_withdraw` validators. Not used by `simple_dex_indexed` (which uses output-index redeemers instead).

#### `PlutusVoid.cs`

```csharp
[CborSerializable]
[CborConstr(0)]                                // Constr(0, []) = Plutus unit type
public partial record PlutusVoid : CborBase;
```

Used as the withdrawal redeemer for the staking validator entry point. The staking validator ignores its redeemer (`_redeemer: Data`) and reads spend redeemers from `self.redeemers` instead.

### 1.2 Database Models (`Models/`)

#### `Order.cs`

```csharp
public record Order(
    string OutRef,                             // PK: "{txHash}#{outputIndex}" — globally unique
    string OwnerPkh,                           // Hex of seller's payment key hash
    string DestinationAddress,                 // Bech32 of seller's destination address
    string OfferSubject,                        // Hex: policyId + assetName (concatenated)
    string AskSubject,                          // Hex: policyId + assetName (concatenated)
    ulong PriceNum,                            // Rational price numerator
    ulong PriceDen,                            // Rational price denominator
    string ScriptHash,                         // Hex of the validator script hash
    ulong Slot,                                // Block slot when the order UTxO was created
    OrderStatus Status,                        // Open | Filled | Cancelled
    ulong? SpentSlot                           // Block slot when spent (null if still Open)
) : IReducerModel;                             // Argus.Sync marker interface
```

- `OutRef` as PK ensures uniqueness — each UTxO can only exist once.
- `Slot`/`SpentSlot` enable rollback: orders created at `Slot >= rollback_slot` are deleted; orders spent at `SpentSlot >= rollback_slot` are reverted to Open.
- `ScriptHash` identifies which validator this order belongs to (the system supports multiple validators simultaneously).

#### `OrderStatus.cs`

```csharp
public enum OrderStatus { Open, Filled, Cancelled }
```

### 1.3 Database Context

#### `SimpleDEXDbContext.cs`

```csharp
public class SimpleDEXDbContext(
    DbContextOptions<SimpleDEXDbContext> options,     // EF Core options (connection string, provider)
    IConfiguration configuration                      // App config (for CardanoDbContext base)
) : CardanoDbContext(options, configuration)           // Inherits ReducerStates table from Argus.Sync
{
    public DbSet<Order> Orders => Set<Order>();        // Order table

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);            // CardanoDbContext sets up ReducerStates

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OutRef);              // PK = OutRef ("{txHash}#{index}")
        });
    }
}
```

`CardanoDbContext` from Argus.Sync adds a `ReducerStates` table that tracks the last processed slot for each reducer, enabling crash recovery and incremental sync.

### 1.4 Extensions

#### `TransactionOutputExtensions.cs`

- `Datum()` — Extracts inline datum bytes from a `TransactionOutput`, handling Chrysalis CBOR `CborTag(24)` wrapping (Plutus V2/V3 inline datum encoding).
- `DatumInfo()` — Returns `(DatumType, byte[])` supporting both Alonzo and post-Alonzo transaction output formats.

#### `PlutusAddressExtensions.cs`

- `ToBech32(NetworkType)` — Converts a Plutus `Address` (payment credential + optional stake credential) to a bech32 address string. Handles both `VerificationKey` and `Script` payment credentials, and optional `Inline<Credential>` staking.

---

## 2. SimpleDEX.Offchain — Transaction API

**Path:** `src/SimpleDEX.Offchain/`

A FastEndpoints web API that builds unsigned Cardano transactions. Uses Blockfrost as the chain data provider and Chrysalis for transaction construction.

### 2.1 Transaction Lifecycle

```
Client → POST /order, /buy, or /cancel → unsigned tx CBOR hex
Client → POST /submit (unsigned CBOR)  → server signs → submits → tx hash
```

In this preview-testnet setup, the server holds the mnemonic and signs transactions. In production, signing would be client-side (wallet integration).

### 2.2 Endpoints

#### `Deploy.cs` — `POST /api/v1/transactions/deploy`

Publishes a validator on-chain as a reference script UTxO.

```csharp
public class Deploy(ICardanoDataProvider provider)
    : Endpoint<DeployRequest, DeployResponse>
```

**Line-by-line:**

```csharp
// L25-26: Read plutus.json path and validator name from config
string plutusJsonPath = Config["PlutusJsonPath"]!;
string validatorName = req.ValidatorName ?? Config["ValidatorName"]!;

// L29-43: Parse plutus.json, find validator by title, extract compiledCode and hash
string plutusJson = await File.ReadAllTextAsync(plutusJsonPath, ct);
JsonNode root = JsonNode.Parse(plutusJson)!;
JsonArray validators = root["validators"]!.AsArray();
// Loop until title matches validatorName...

// L52-56: Build PlutusV3Script from compiled code, derive enterprise script address
PlutusV3Script script = new(new Value3(3), Convert.FromHexString(compiledCode));
byte[] scriptHashBytes = Convert.FromHexString(scriptHash);
WalletAddress contractAddr = new(
    NetworkType.Testnet,
    AddressType.EnterpriseScriptPayment,    // Type 7: script payment, no staking
    scriptHashBytes, null);
string contractAddress = contractAddr.ToBech32();

// L59-60: Build unsigned tx via DeployTemplate
TransactionTemplate<DeployRequest> template =
    DeployTemplate.Create(req, provider, contractAddress, script);
Transaction unsignedTx = await template(req);

// L64: Return unsigned CBOR hex + contract address + script hash
await Send.ResponseAsync(new DeployResponse(unsignedTxCbor, contractAddress, scriptHash));
```

#### `Order.cs` — `POST /api/v1/transactions/order`

Creates new sell orders by locking tokens at the script address.

```csharp
public class Order(ICardanoDataProvider provider)
    : Endpoint<OrderRequest, OrderResponse>
```

**Line-by-line:**

```csharp
// L32-33: Resolve script address from config using the request's ScriptHash
string scriptAddress = Config[$"Validators:{req.ScriptHash}:Address"]
    ?? throw new InvalidOperationException($"Validator {req.ScriptHash} not configured");

// L36-44: Parse buyer's bech32 address into Plutus Address components
WalletAddress addr = new(req.ChangeAddress);
byte[] ownerPkh = addr.GetPaymentKeyHash()!;         // 28-byte PKH for on-chain signer checks
byte[]? stakeKeyHash = addr.GetStakeKeyHash();        // Optional staking key

// Build Plutus Address preserving staking credential if present
Option<Inline<Credential>> stakeCredential = stakeKeyHash is not null
    ? new Some<Inline<Credential>>(new Inline<Credential>(new VerificationKey(stakeKeyHash)))
    : new None<Inline<Credential>>();
Address ownerAddress = new(new VerificationKey(ownerPkh), stakeCredential);

// L48-80: For each order item, build OrderDatum + output value
foreach (OrderItem orderItem in req.Orders) {
    (byte[] offerPolicyId, byte[] offerAssetName) = ParseSubject(orderItem.OfferSubject);
    (byte[] askPolicyId, byte[] askAssetName) = ParseSubject(orderItem.AskSubject);

    OrderDatum datum = new(
        Owner: ownerPkh,              // PKH for cancel authorization
        Destination: ownerAddress,    // Full address for payment routing
        Offer: new TokenId(offerPolicyId, offerAssetName),
        Ask: new TokenId(askPolicyId, askAssetName),
        Price: new RationalC(orderItem.PriceNum, orderItem.PriceDen)
    );

    // Build Value: Lovelace for ADA, LovelaceWithMultiAsset for native tokens
    Value outputValue;
    if (offerPolicyId.Length == 0)                    // Empty policyId = ADA
        outputValue = new Lovelace(orderItem.OfferAmount);
    else {
        // Native token: wrap in MultiAsset structure
        TokenBundleOutput tokenBundle = new(new Dictionary<byte[], ulong>
            { { offerAssetName, orderItem.OfferAmount } });
        MultiAssetOutput multiAsset = new(new Dictionary<byte[], TokenBundleOutput>
            { { offerPolicyId, tokenBundle } });
        outputValue = new LovelaceWithMultiAsset(new Lovelace(0), multiAsset);
    }

    items.Add(new OrderOutputItem(datum, outputValue));
}

// L83-84: Build unsigned tx — outputs locked at script address with inline datum
TransactionTemplate<OrderRequest> template =
    OrderTemplate.Create(req, provider, scriptAddress, items);
```

**`ParseSubject` helper** (L91-99): Splits a hex subject string (56-char policy ID + remaining asset name) into `(byte[] PolicyId, byte[] AssetName)`. Empty string = ADA.

#### `Buy.cs` — `POST /api/v1/transactions/buy`

Fills one or more orders by paying sellers and consuming order UTxOs.

```csharp
public class Buy(ICardanoDataProvider provider, SimpleDEXDbContext db)
    : Endpoint<BuyRequest, BuyResponse>
```

**Line-by-line:**

```csharp
// L41-52: Look up all requested orders from DB, validate they exist
List<string> outRefs = req.Orders.Select(o => o.OutRef).ToList();
var orders = await db.Orders.AsNoTracking()
    .Where(o => outRefs.Contains(o.OutRef))
    .ToListAsync(ct);
if (orders.Count != req.Orders.Count)              // Some orders not found
    await Send.NotFoundAsync(ct);

// L54-60: Validate all orders belong to the same validator
string scriptHash = orders[0].ScriptHash;
if (orders.Any(o => o.ScriptHash != scriptHash))
    ThrowError("All orders must belong to the same validator");

// L62-67: Resolve validator config — address, script reference UTxO
string scriptAddress = Config[$"Validators:{scriptHash}:Address"];
string scriptRefTxHash = Config[$"Validators:{scriptHash}:ScriptRef:TxHash"]!;
ulong scriptRefTxIndex = ulong.Parse(Config[$"Validators:{scriptHash}:ScriptRef:TxIndex"]!);
TransactionInput scriptRefUtxo = new(Convert.FromHexString(scriptRefTxHash), scriptRefTxIndex);

// L70: Fetch all UTxOs at the script address from Blockfrost
List<ResolvedInput> utxos = await provider.GetUtxosAsync([scriptAddress]);

// L74-123: For each order, compute payment amount and build BuyOrderItem
foreach (BuyOrderRequest orderReq in req.Orders) {
    // Parse outRef into tx hash + index
    string[] outRefParts = orderReq.OutRef.Split('#');
    TransactionInput orderUtxoRef = new(Convert.FromHexString(outRefParts[0]),
                                        ulong.Parse(outRefParts[1]));

    // Find the UTxO on-chain and deserialize its datum
    ResolvedInput orderUtxo = utxos.First(u =>
        u.Outref.TransactionId.SequenceEqual(...) && u.Outref.Index == orderTxIndex);
    OrderDatum orderDatum = CborSerializer.Deserialize<OrderDatum>(orderUtxo.Output.Datum()!);

    // Get offer quantity from the UTxO's actual value (not the datum)
    string offerSubject = Convert.ToHexStringLower(orderDatum.Offer.PolicyId)
        + Convert.ToHexStringLower(orderDatum.Offer.AssetName);
    ulong offerQty = orderUtxo.Output.Amount().QuantityOf(offerSubject) ?? 0;

    // Compute required payment: ceil(offer_qty * num / den)
    ulong buyQty = orderReq.Amount ?? offerQty;       // Full fill if no amount specified
    ulong requiredPayment = (buyQty * orderDatum.Price.Num
                             + orderDatum.Price.Den - 1)
                            / orderDatum.Price.Den;    // Ceiling division

    // Build payment Value (ADA or native token)
    Value paymentValue;
    if (askPolicyId.Length == 0)
        paymentValue = new Lovelace(requiredPayment);
    else
        paymentValue = new LovelaceWithMultiAsset(new Lovelace(0),
            new MultiAssetOutput(...));

    // Compute anti-double-satisfaction tag (used by non-indexed validators)
    CborOutRef outRef = new(Convert.FromHexString(orderTxHash), orderTxIndex);
    byte[] outRefCbor = CborSerializer.Serialize(outRef);
    byte[] orderTag = HashUtil.Blake2b256(outRefCbor);

    items.Add(new BuyOrderItem(orderUtxoRef, sellerAddress, paymentValue, orderTag));
}

// L126-132: Route to correct template based on validator type
string validatorType = Config[$"Validators:{scriptHash}:Type"] ?? "spend";
TransactionTemplate<BuyRequest> template = validatorType switch {
    "withdraw"         => BuyWithdrawTemplate.Create(...),
    "indexed-withdraw" => BuyIndexedWithdrawTemplate.Create(...),
    _                  => BuyTemplate.Create(...),              // default: spend
};
Transaction unsignedTx = await template(req);
```

The routing logic is the key integration point — the same endpoint serves all validator variants. The `orderTag` is computed for all orders but only used by `BuyTemplate` and `BuyWithdrawTemplate` (which attach it as `DatumTag`). `BuyIndexedWithdrawTemplate` ignores it.

#### `Cancel.cs` — `POST /api/v1/transactions/cancel`

Cancels one or more orders, returning locked tokens to the seller.

```csharp
public class Cancel(ICardanoDataProvider provider, SimpleDEXDbContext db)
    : Endpoint<CancelRequest, CancelResponse>
```

**Line-by-line:**

```csharp
// L41-50: Look up all requested orders from DB
List<string> outRefs = req.Orders.Select(o => $"{o.TxHash}#{o.Index}").ToList();
var orders = await db.Orders.AsNoTracking()
    .Where(o => outRefs.Contains(o.OutRef))
    .ToListAsync(ct);

// L52-58: Validate all orders share the same ScriptHash
string scriptHash = orders[0].ScriptHash;
if (orders.Any(o => o.ScriptHash != scriptHash))
    ThrowError("All orders must belong to the same validator");

// L60-65: Resolve validator config
string scriptAddress = Config[$"Validators:{scriptHash}:Address"];
TransactionInput scriptReference = new(...);

// L70-76: Read owner address from the first order's on-chain datum
CancelOrderRef firstRef = req.Orders[0];
ResolvedInput firstUtxo = utxos.First(u => ...);
OrderDatum firstDatum = CborSerializer.Deserialize<OrderDatum>(firstUtxo.Output.Datum()!);
string ownerAddress = firstDatum.Destination.ToBech32(provider.NetworkType);

// L79-81: Build TransactionInput list for all orders being cancelled
List<TransactionInput> orderReferences = req.Orders
    .Select(o => new TransactionInput(Convert.FromHexString(o.TxHash), o.Index))
    .ToList();

// L84-90: Route to correct template
string validatorType = Config[$"Validators:{scriptHash}:Type"] ?? "spend";
TransactionTemplate<CancelRequest> template = validatorType switch {
    "withdraw"         => CancelWithdrawTemplate.Create(...),
    "indexed-withdraw" => CancelIndexedWithdrawTemplate.Create(...),
    _                  => CancelTemplate.Create(...),
};
```

#### `SubmitTx.cs` — `POST /api/v1/transactions/submit`

Signs an unsigned transaction with the server's mnemonic and submits to the network.

```csharp
public class SubmitTx(ICardanoDataProvider provider, IConfiguration config)
    : Endpoint<SubmitTxRequest, SubmitTxResponse>
```

**Line-by-line:**

```csharp
// L26: Read mnemonic from config
string mnemonic = config["Mnemonic"]!;

// L29-36: Derive payment signing key using BIP44/CIP-1852 path
Mnemonic mnemonicObj = Mnemonic.Restore(mnemonic, wordLists: English.Words);
PrivateKey rootKey = mnemonicObj.GetRootKey();
PrivateKey paymentKey = rootKey
    .Derive(1852, DerivationType.HARD)    // Purpose: CIP-1852 (Cardano)
    .Derive(1815, DerivationType.HARD)    // Coin type: ADA
    .Derive(0, DerivationType.HARD)       // Account 0
    .Derive(0, DerivationType.SOFT)       // Role: external (payment)
    .Derive(0, DerivationType.SOFT);      // Address index 0

// L39: Deserialize the unsigned transaction from CBOR hex
CborTransaction unsignedTx = CborTransaction.Read(Convert.FromHexString(req.UnsignedTxCborHex));

// L41: Sign — adds VKey witness to the witness set
CborTransaction signedTx = unsignedTx.Sign(paymentKey);

// L43-50: Clear cached Raw bytes so Chrysalis re-serializes with the new witness
if (signedTx is PostMaryTransaction pmt) {
    signedTx = pmt with {
        Raw = null,                                    // Force re-serialization of tx body
        TransactionWitnessSet = pmt.TransactionWitnessSet with { Raw = null }
    };
}

// L51: Submit to the network via Blockfrost
string txHash = await provider.SubmitTransactionAsync(signedTx);
```

The `Raw = null` step is critical. Chrysalis caches the original CBOR bytes; after adding a witness, the cached bytes are stale. Setting `Raw = null` forces re-serialization with the witness included.

#### `GetOrder.cs` — `GET /api/v1/orders/{OutRef}`

Returns a single order by its `OutRef`.

```csharp
// L23-25: Simple DB lookup by primary key
var order = await db.Orders.AsNoTracking()
    .FirstOrDefaultAsync(o => o.OutRef == req.OutRef, ct);

// L33-45: Map to OrderDto (Status enum converted to string)
await Send.ResponseAsync(new OrderDto(
    order.OutRef, order.OwnerPkh, order.DestinationAddress,
    order.OfferSubject, order.AskSubject,
    order.PriceNum, order.PriceDen,
    order.ScriptHash, order.Slot,
    order.Status.ToString(), order.SpentSlot
));
```

#### `ListOrders.cs` — `POST /api/v1/orders`

Paginated order listing with filters.

```csharp
// L21-23: Validate and default pagination params
if (req.Page < 1) req.Page = 1;
if (req.PageSize < 1) req.PageSize = 20;
if (req.PageSize > 100) req.PageSize = 100;          // Cap at 100

// L25-43: Build query with optional filters
IQueryable<OrderEntity> query = db.Orders.AsNoTracking();

if (!string.IsNullOrEmpty(req.Status))                // Filter by status
    query = query.Where(o => o.Status == status);
if (!string.IsNullOrEmpty(req.OfferSubject))          // Filter by offered token
    query = query.Where(o => o.OfferSubject == req.OfferSubject);
if (!string.IsNullOrEmpty(req.AskSubject))            // Filter by asked token
    query = query.Where(o => o.AskSubject == req.AskSubject);
if (!string.IsNullOrEmpty(req.OwnerAddress)) {        // Filter by owner
    // Convert bech32 address to PKH for DB lookup
    string ownerPkh = Convert.ToHexStringLower(
        new Address(req.OwnerAddress).GetPaymentKeyHash()!);
    query = query.Where(o => o.OwnerPkh == ownerPkh);
}
if (!string.IsNullOrEmpty(req.ScriptHash))            // Filter by validator
    query = query.Where(o => o.ScriptHash == req.ScriptHash);

// L45-64: Execute with pagination (newest first)
int totalCount = await query.CountAsync(ct);
var items = await query
    .OrderByDescending(o => o.Slot)                   // Most recent first
    .Skip((req.Page - 1) * req.PageSize)
    .Take(req.PageSize)
    .Select(o => new OrderDto(...))
    .ToListAsync(ct);

await Send.ResponseAsync(new ListOrdersResponse(items, req.Page, req.PageSize, totalCount));
```

### 2.3 Templates

Templates use Chrysalis `TransactionTemplateBuilder<T>` — a declarative builder that handles coin selection, fee estimation, and change calculation. Each template returns a `TransactionTemplate<T>` (a function `T -> Task<Transaction>`).

#### `DeployTemplate.cs`

```csharp
public static TransactionTemplate<DeployRequest> Create(
    DeployRequest request, ICardanoDataProvider provider,
    string contractAddress, PlutusV3Script script)
{
    return TransactionTemplateBuilder<DeployRequest>
        .Create(provider)
        .AddStaticParty("change", request.ChangeAddress, isChange: true)  // Fee source + change
        .AddStaticParty("contract", contractAddress)                       // Script address
        .AddOutput((options, _, _) => {
            options.To = "contract";
            options.Amount = new Lovelace(5_000_000);    // 5 ADA minimum UTxO
            options.Script = script;                      // PlutusV3Script stored as reference script
        })
        .Build();                                         // Build(true) = auto coin selection
}
```

Produces a single output at the contract address with 5 ADA and the compiled validator as a reference script. This UTxO is then referenced by all future transactions via `AddReferenceInput`.

#### `OrderTemplate.cs`

```csharp
public static TransactionTemplate<OrderRequest> Create(
    OrderRequest request, ICardanoDataProvider provider,
    string scriptAddress, List<OrderOutputItem> items)
{
    var builder = TransactionTemplateBuilder<OrderRequest>
        .Create(provider)
        .AddStaticParty("change", request.ChangeAddress, isChange: true)
        .AddStaticParty("contract", scriptAddress);

    foreach (OrderOutputItem item in items) {
        builder.AddOutput((options, _, _) => {
            options.To = "contract";                  // Lock at script address
            options.Amount = item.OutputValue;         // Offer tokens
            options.SetDatum(item.Datum);              // Inline OrderDatum
        });
    }

    return builder.Build();                            // Auto coin selection for ADA fees
}
```

Creates one output per order, each locked at the script address with the offer tokens and an inline `OrderDatum`. Supports batch order creation.

#### `BuyTemplate.cs` — Spend-only validator

```csharp
public record BuyOrderItem(
    TransactionInput OrderUtxoRef,       // The order UTxO being consumed
    string SellerAddress,                 // Where to send payment
    Value PaymentValue,                   // How much to pay
    byte[] OrderTag);                     // blake2b hash for anti-double-satisfaction

public static TransactionTemplate<BuyRequest> Create(
    BuyRequest request, ICardanoDataProvider provider,
    string scriptAddress, TransactionInput scriptRefUtxo,
    List<BuyOrderItem> items)
{
    var builder = TransactionTemplateBuilder<BuyRequest>
        .Create(provider)
        .AddStaticParty("change", request.BuyerAddress, isChange: true)
        .AddStaticParty("contract", scriptAddress)
        .AddReferenceInput((options, _) => {           // Reference the deployed script
            options.From = "contract";
            options.UtxoRef = scriptRefUtxo;
        });

    int idx = 0;
    foreach (BuyOrderItem item in items) {
        string sellerParty = $"seller_{idx}";
        string inputId = Convert.ToHexStringLower(item.OrderUtxoRef.TransactionId)
                        + item.OrderUtxoRef.Index;

        builder.AddStaticParty(sellerParty, item.SellerAddress);

        builder.AddInput((options, _) => {
            options.From = "contract";                 // Spend from script address
            options.UtxoRef = item.OrderUtxoRef;       // Specific order UTxO
            options.Id = inputId;                      // Unique ID for this input
            options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
                new Redeemer<CborBase>(
                    RedeemerTag.Spend, 0,              // Tag: Spend, index auto-calculated
                    new Buy(),                         // Constr(0) — Buy redeemer
                    new ExUnits(500000, 200000000));    // Execution budget
        });

        builder.AddOutput((options, _, _) => {
            options.To = sellerParty;                  // Pay to seller
            options.Amount = item.PaymentValue;         // Required payment
            options.SetDatum(new DatumTag(item.OrderTag));  // Anti-double-satisfaction tag
        });

        idx++;
    }

    return builder.Build(false);                       // Build(false) = no auto script input selection
}
```

Key points:
- `Build(false)` prevents Chrysalis from auto-selecting additional inputs from the script address.
- Each output carries a `DatumTag` — the blake2b hash of the consumed UTxO's `OutputReference`.
- `ExUnits(500000, 200000000)` is a budget placeholder — the actual cost is calculated during evaluation.

#### `BuyWithdrawTemplate.cs` — Withdraw zero validator

Same structure as `BuyTemplate` but adds the withdrawal entry point:

```csharp
// Build reward address: 0xF0 header byte + script hash
// 0xF0 = testnet reward address with script credential
byte[] rewardAddressBytes = new byte[1 + scriptHash.Length];
rewardAddressBytes[0] = 0xF0;
Buffer.BlockCopy(scriptHash, 0, rewardAddressBytes, 1, scriptHash.Length);
string rewardAddress = Bech32Util.Encode(rewardAddressBytes, "stake_test");

builder.AddStaticParty("reward", rewardAddress)
    .AddWithdrawal((options, _) => {
        options.From = "reward";
        options.Amount = 0;                            // Withdraw 0 ADA (triggers staking validator)
        options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
            new Redeemer<CborBase>(
                RedeemerTag.Reward, 0,
                new PlutusVoid(),                      // Empty redeemer (staking validator ignores it)
                new ExUnits(500000, 200000000));
    });
```

The withdrawal of 0 ADA triggers the staking validator, which runs the actual business logic once. Spend redeemers are still `Buy()` — the staking validator reads them from `self.redeemers`. Outputs still carry `DatumTag` for on-chain double-satisfaction prevention.

#### `BuyIndexedWithdrawTemplate.cs` — Indexed withdraw validator

Same withdrawal setup as `BuyWithdrawTemplate`, but two key differences:

```csharp
// Difference 1: Spend redeemer is IndexedBuy(outputIndex) instead of Buy()
int outputIndex = idx;                                 // Capture for closure (avoid modified closure bug)
builder.AddInput((options, _) => {
    options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
        new Redeemer<CborBase>(
            RedeemerTag.Spend, 0,
            new IndexedBuy((ulong)outputIndex),        // Points to specific tx output
            new ExUnits(500000, 200000000));
});

// Difference 2: No DatumTag on outputs
builder.AddOutput((options, _, _) => {
    options.To = sellerParty;
    options.Amount = item.PaymentValue;
    // No options.SetDatum() — no DatumTag needed
});
```

`int outputIndex = idx` captures the current loop counter by value. Without this, the lambda closure would capture `idx` by reference, and all redeemers would get the final value of `idx`.

#### `CancelTemplate.cs` — Spend-only validator

```csharp
foreach (TransactionInput orderRef in orderReferences) {
    builder.AddInput((options, _) => {
        options.From = "contract";
        options.UtxoRef = orderRef;
        options.Id = $"cancel_{idx}";
        options.SetRedeemerBuilder((mapping, parameters, txBuilder) =>
            new Cancel());                             // Constr(1) — Cancel redeemer
    });
    idx++;
}

builder.AddRequiredSigner("change");                   // Owner PKH in required_signers
return builder.Build(false);
```

`AddRequiredSigner("change")` populates `required_signers` in the tx body with the owner's PKH. The ledger enforces that all `required_signers` must appear in the witness set.

#### `CancelWithdrawTemplate.cs` — Withdraw zero validator

Same as `CancelTemplate` but adds the withdrawal entry point (same pattern as `BuyWithdrawTemplate`). Spend redeemers are `Cancel()` with `ExUnits`.

#### `CancelIndexedWithdrawTemplate.cs` — Indexed withdraw validator

Same as `CancelWithdrawTemplate` but spend redeemers use `IndexedCancel()` instead of `Cancel()`.

### 2.4 Request/Response Models

#### `BuyParams.cs`

```csharp
public record BuyOrderRequest(
    string OutRef,                    // "{txHash}#{index}" of the order to buy
    ulong? Amount = null);            // Optional: partial fill amount (null = full fill)

public record BuyRequest(
    string BuyerAddress,              // Bech32 address for change outputs
    List<BuyOrderRequest> Orders);    // Batch: multiple orders in one tx

public record BuyResponse(
    string UnsignedTxCborHex);        // Unsigned transaction CBOR
```

#### `OrderParams.cs`

```csharp
public record OrderItem(
    string OfferSubject,              // Hex: policyId + assetName (empty = ADA)
    ulong OfferAmount,                // Amount of offer tokens to lock
    string AskSubject,                // Hex: policyId + assetName being asked for
    ulong PriceNum,                   // Rational price numerator
    ulong PriceDen);                  // Rational price denominator

public record OrderRequest(
    string ChangeAddress,             // Seller's bech32 address
    string ScriptHash,                // Which validator to use
    List<OrderItem> Orders);          // Batch: multiple orders in one tx

public record OrderResponse(
    string UnsignedTxCborHex);
```

#### `CancelParam.cs`

```csharp
public record CancelOrderRef(
    string TxHash,                    // Order UTxO tx hash
    ulong Index);                     // Order UTxO output index

public record CancelRequest(
    List<CancelOrderRef> Orders);     // Batch: multiple cancellations in one tx

public record CancelResponse(
    string UnsignedTxCborHex);
```

#### `DeployParams.cs`

```csharp
public record DeployRequest(
    string ChangeAddress,
    string? ValidatorName = null);    // Override validator name from config

public record DeployResponse(
    string UnsignedTxCborHex,
    string ContractAddress,           // Derived bech32 script address
    string ScriptHash);               // Script hash (use for config)
```

#### `OrderDto.cs` and `ListOrdersModels.cs`

```csharp
public record OrderDto(
    string OutRef, string OwnerPkh, string DestinationAddress,
    string OfferSubject, string AskSubject,
    ulong PriceNum, ulong PriceDen,
    string ScriptHash, ulong Slot,
    string Status,                    // Enum as string: "Open", "Filled", "Cancelled"
    ulong? SpentSlot);

public class ListOrdersRequest {
    public string? Status { get; set; }
    public string? OfferSubject { get; set; }
    public string? AskSubject { get; set; }
    public string? OwnerAddress { get; set; }     // Converted to PKH for DB lookup
    public string? ScriptHash { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;       // Max 100
}

public record ListOrdersResponse(
    IEnumerable<OrderDto> Items,
    int Page, int PageSize, int TotalCount);
```

---

## 3. SimpleDEX.Sync — Chain Indexer

**Path:** `src/SimpleDEX.Sync/`

A dedicated chain-follower that connects to a Cardano node via the Ouroboros N2C (node-to-client) protocol and populates the database through `OrderReducer`.

### 3.1 Program.cs

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register chain indexer with Argus.Sync — connects to Cardano node via N2C
builder.Services.AddCardanoIndexer<SimpleDEXDbContext>(builder.Configuration);

// Register all reducers implementing IReducerModel from config
builder.Services.AddReducers<SimpleDEXDbContext, IReducerModel>(builder.Configuration);

WebApplication app = builder.Build();
app.Run();                                     // Starts the chain sync loop
```

Argus.Sync manages the chain sync lifecycle: connecting to the node, following the chain tip, calling `RollForwardAsync` for new blocks, and `RollBackwardAsync` for rollbacks.

### 3.2 OrderReducer.cs

```csharp
public class OrderReducer(
    IDbContextFactory<SimpleDEXDbContext> dbContextFactory,    // Factory for scoped DB contexts
    IConfiguration configuration
) : IReducer<Order>                                            // Argus.Sync reducer interface
```

#### Constructor initialization

```csharp
// Pre-load configured script hashes into a HashSet for O(1) lookup
private readonly HashSet<string> _scriptHashes = configuration.GetSection("ScriptHashes")
    .Get<List<string>>()
    ?.ToHashSet()
    ?? throw new InvalidOperationException("ScriptHashes not configured");

private readonly NetworkType _networkType =
    Enum.Parse<NetworkType>(configuration["NetworkType"] ?? "Preview");
```

#### `RollForwardAsync(Block block)` — Process new block

```csharp
public async Task RollForwardAsync(Block block)
{
    ulong slot = block.Header().HeaderBody().Slot();       // Extract slot number

    IEnumerable<Order> newOrders = CollectNewOrders(block, slot);       // Step 1: new orders
    Dictionary<string, TransactionInput> inputsByOutRef =
        CollectInputOutRefs(block);                                     // Step 2: all spent inputs

    if (!newOrders.Any() && inputsByOutRef.Count == 0) return;          // Early exit: nothing relevant

    await using SimpleDEXDbContext dbContext =
        await dbContextFactory.CreateDbContextAsync();                  // Scoped DB context

    if (newOrders.Any())
        dbContext.Orders.AddRange(newOrders);                           // Insert new orders

    if (inputsByOutRef.Count > 0)
        await ProcessSpentOrders(dbContext, block, inputsByOutRef, slot); // Update spent orders

    await dbContext.SaveChangesAsync();                                  // Single atomic save
}
```

#### `CollectNewOrders` — Scan outputs for new orders

```csharp
private List<Order> CollectNewOrders(Block block, ulong slot) =>
[
    .. block.TransactionBodies()
        .SelectMany(txBody => {
            string txHash = txBody.Hash();                  // Compute tx hash
            return txBody.Outputs()
                .Select((output, index) =>                  // Pair each output with its index
                    (TxHash: txHash, Index: index, Output: output));
        })
        .Select(x => (x.TxHash, x.Index, x.Output,
            ScriptHash: TryMatchScriptOutput(x.Output)))    // Check if output is at a script address
        .Where(x => x.ScriptHash is not null)               // Only script outputs
        .Select(x => TryParseOrder(                         // Deserialize datum → Order
            x.TxHash, x.Index, x.Output, x.ScriptHash!, slot))
        .OfType<Order>()                                     // Filter out nulls (parse failures)
];
```

#### `CollectInputOutRefs` — Build lookup of all consumed inputs

```csharp
private static Dictionary<string, TransactionInput> CollectInputOutRefs(Block block) =>
    block.TransactionBodies()
        .SelectMany(txBody => txBody.Inputs())
        .ToDictionary(
            input => $"{Convert.ToHexStringLower(input.TransactionId())}#{input.Index()}",
            input => input);
```

Creates a dictionary keyed by `"txHash#index"` for O(1) matching against existing orders in the DB.

#### `ProcessSpentOrders` — Update status of consumed orders

```csharp
private static async Task ProcessSpentOrders(
    SimpleDEXDbContext dbContext, Block block,
    Dictionary<string, TransactionInput> inputsByOutRef, ulong slot)
{
    // Find all open orders whose OutRef appears in this block's inputs
    List<Order> matchedOrders = await FindMatchingOrders(dbContext, inputsByOutRef.Keys);
    if (matchedOrders.Count == 0) return;

    // For each matched order, determine Buy or Cancel from the redeemer
    matchedOrders.ForEach(order =>
        ApplySpentStatus(dbContext, block, inputsByOutRef[order.OutRef], order, slot));
}
```

#### `ApplySpentStatus` — Determine Fill vs Cancel from redeemer

```csharp
private static void ApplySpentStatus(
    SimpleDEXDbContext dbContext, Block block,
    TransactionInput input, Order order, ulong slot)
{
    RedeemerEntry? redeemer = input.Redeemer(block);           // Find redeemer for this input
    OrderStatus? status = redeemer is not null
        ? ResolveStatus(redeemer)                              // Buy → Filled, Cancel → Cancelled
        : null;
    if (status.HasValue)
        dbContext.Entry(order).CurrentValues
            .SetValues(order with { Status = status.Value, SpentSlot = slot });
}
```

#### `ResolveStatus` — Map redeemer to OrderStatus

```csharp
private static OrderStatus? ResolveStatus(RedeemerEntry redeemer)
{
    try {
        return CborSerializer.Deserialize<OrderRedeemer>(redeemer.Data.Raw!.Value) switch {
            Buy => OrderStatus.Filled,
            Cancel => OrderStatus.Cancelled,
            _ => null
        };
    }
    catch { return null; }                             // Unknown redeemer format → skip
}
```

Note: This currently deserializes as `OrderRedeemer` (`Buy`/`Cancel`). For `IndexedOrderRedeemer` (`IndexedBuy`/`IndexedCancel`), the CBOR constructor indices are the same (0 and 1), so `Buy` matches `IndexedBuy` and `Cancel` matches `IndexedCancel`. If the indexed redeemer format ever diverges (e.g., different constructor indices), this would need a fallback deserializer.

#### `TryMatchScriptOutput` — Check if output belongs to a tracked validator

```csharp
private string? TryMatchScriptOutput(TransactionOutput output)
{
    try {
        WalletAddress address = new(output.Address());
        if (!address.ToBech32().StartsWith("addr")) return null;   // Skip stake/reward addresses

        byte[]? pkh = address.GetPaymentKeyHash();
        if (pkh is null) return null;                               // Skip non-key addresses

        string hash = Convert.ToHexStringLower(pkh);
        return _scriptHashes.Contains(hash)                         // Is this a tracked script?
            && output.Datum() is not null                           // Does it have an inline datum?
            ? hash : null;
    }
    catch { return null; }                                          // Malformed address → skip
}
```

For script addresses, `GetPaymentKeyHash()` returns the script hash (both use the payment credential field).

#### `TryParseOrder` — Deserialize on-chain datum to DB model

```csharp
private Order? TryParseOrder(
    string txHash, int index, TransactionOutput output,
    string scriptHash, ulong slot)
{
    try {
        OrderDatum datum = CborSerializer.Deserialize<OrderDatum>(output.Datum());

        return new Order(
            OutRef: $"{txHash}#{index}",                           // PK
            OwnerPkh: Convert.ToHexStringLower(datum.Owner),       // PKH hex
            DestinationAddress: datum.Destination.ToBech32(_networkType),  // Full bech32
            OfferSubject: Convert.ToHexStringLower(datum.Offer.PolicyId)
                        + Convert.ToHexStringLower(datum.Offer.AssetName),
            AskSubject: Convert.ToHexStringLower(datum.Ask.PolicyId)
                       + Convert.ToHexStringLower(datum.Ask.AssetName),
            PriceNum: datum.Price.Num,
            PriceDen: datum.Price.Den,
            ScriptHash: scriptHash,
            Slot: slot,
            Status: OrderStatus.Open,
            SpentSlot: null
        );
    }
    catch { return null; }                                          // Bad datum → skip
}
```

#### `RollBackwardAsync(ulong slot)` — Handle chain rollback

```csharp
public async Task RollBackwardAsync(ulong slot)
{
    await using SimpleDEXDbContext dbContext =
        await dbContextFactory.CreateDbContextAsync();

    await RevertSpentOrders(dbContext, slot);              // Step 1: revert spent orders
    await DeleteNewOrders(dbContext, slot);                // Step 2: delete new orders
}
```

**Order matters:** Revert before delete. If an order was created at slot 100 and spent at slot 200, and we roll back to slot 50, we need to revert the spend first (slot 200 >= 50), then delete the order (slot 100 >= 50). If we deleted first, we'd lose the order record entirely.

```csharp
// Revert: any order spent at or after the rollback slot becomes Open again
private static async Task RevertSpentOrders(SimpleDEXDbContext dbContext, ulong slot) =>
    await dbContext.Orders
        .Where(o => o.SpentSlot >= slot)
        .ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, OrderStatus.Open)
            .SetProperty(o => o.SpentSlot, (ulong?)null));

// Delete: any order created at or after the rollback slot is removed
private static async Task DeleteNewOrders(SimpleDEXDbContext dbContext, ulong slot) =>
    await dbContext.Orders
        .Where(o => o.Slot >= slot)
        .ExecuteDeleteAsync();
```

---

## 4. Configuration

### 4.1 Off-chain (`src/SimpleDEX.Offchain/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "SimpleDEX": "Host=localhost;Database=simpledex;Username=postgres;Password=test1234;Port=35432"
  },
  "Provider": "Blockfrost",
  "NetworkType": "Preview",
  "BlockfrostProjectId": "preview...",
  "PlutusJsonPath": "../../src/onchain/plutus.json",
  "Validators": {
    "<script_hash_hex>": {
      "Type": "spend|withdraw|indexed-withdraw",
      "Address": "addr_test1...",
      "ScriptRef": {
        "TxHash": "<deploy_tx_hash>",
        "TxIndex": 0
      }
    }
  },
  "Mnemonic": "..."
}
```

**Validators section:**
- Key = script hash (hex) from `plutus.json`
- `Type` = routing key for Buy/Cancel endpoints (default: `"spend"`)
  - `"spend"` → `BuyTemplate` / `CancelTemplate`
  - `"withdraw"` → `BuyWithdrawTemplate` / `CancelWithdrawTemplate`
  - `"indexed-withdraw"` → `BuyIndexedWithdrawTemplate` / `CancelIndexedWithdrawTemplate`
- `Address` = bech32 script address (derived during deploy)
- `ScriptRef` = the UTxO holding the reference script (created by deploy endpoint)

### 4.2 Sync (`src/SimpleDEX.Sync/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "CardanoContext": "Host=localhost;Database=simpledex;Username=postgres;Password=test1234;Port=35432"
  },
  "CardanoContextSchema": "simpledex",
  "NetworkType": "Preview",
  "ScriptHashes": [
    "<script_hash_1>",
    "<script_hash_2>"
  ],
  "CardanoNodeConnection": {
    "ConnectionType": "UnixSocket",
    "UnixSocketPath": "/tmp/node.socket",
    "NetworkMagic": 2
  },
  "CardanoIndexReducers": {
    "Rollback": { "Enabled": false },
    "Dashboard": { "TuiMode": true, "RefreshInterval": 100 },
    "State": { "ReducerStateSyncIntervalSeconds": 10 }
  }
}
```

**ScriptHashes:** Array of validator script hashes to track. The reducer only indexes outputs at addresses matching these hashes. To add a new validator, append its hash here.

**CardanoNodeConnection:** Connects directly to a Cardano node via Unix socket (N2C protocol). `NetworkMagic: 2` = Preview testnet.

---

## End-to-End Flow

```
1. Deploy    → POST /deploy                 → script ref UTxO on-chain
2. Order     → POST /order                  → tokens locked at script address (inline OrderDatum)
3. Sync      → OrderReducer.RollForward     → Order inserted (Status = Open)
4a. Buy      → POST /buy                    → buyer pays seller, order UTxO consumed
4b. Cancel   → POST /cancel                 → seller reclaims tokens
5. Sync      → OrderReducer.RollForward     → Order updated (Filled or Cancelled)
6. Query     → GET /orders/{outref}         → return order details
              → POST /orders (with filters) → paginated listing
```

## Template Comparison

```
BuyTemplate (spend)
  Input:  [order UTxO + Buy() redeemer]
  Output: [seller payment + DatumTag]
  No withdrawal. Validator runs per input.

BuyWithdrawTemplate (withdraw)
  Input:      [order UTxO + Buy() redeemer]
  Output:     [seller payment + DatumTag]
  Withdrawal: [0 ADA + PlutusVoid redeemer]
  Spend validator: gate check. Staking validator: full logic (once).

BuyIndexedWithdrawTemplate (indexed-withdraw)
  Input:      [order UTxO + IndexedBuy(outputIndex) redeemer]
  Output:     [seller payment, no DatumTag]
  Withdrawal: [0 ADA + PlutusVoid redeemer]
  Spend validator: gate check. Staking validator: O(1) output lookup (once).
```
