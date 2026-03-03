using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using SimpleDEX.Data.Extensions;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SimpleDEX.Data;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Templates;
using Address = Chrysalis.Cbor.Types.Plutus.Address.Address;
using Transaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;
using TransactionInput = Chrysalis.Cbor.Types.Cardano.Core.Transaction.TransactionInput;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Buy(ICardanoDataProvider provider, SimpleDEXDbContext db) : Endpoint<BuyRequest, BuyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/transactions/buy");
        AllowAnonymous();
    }

    public override async Task HandleAsync(BuyRequest req, CancellationToken ct)
    {
        if (req.Orders.Count == 0)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        List<string> outRefs = req.Orders.Select(o => o.OutRef).ToList();

        // Look up all orders in one query
        var orders = await db.Orders.AsNoTracking()
            .Where(o => outRefs.Contains(o.OutRef))
            .ToListAsync(ct);

        if (orders.Count != req.Orders.Count)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Validate all orders share the same ScriptHash
        string scriptHash = orders[0].ScriptHash;
        if (orders.Any(o => o.ScriptHash != scriptHash))
        {
            ThrowError("All orders must belong to the same validator");
            return;
        }

        // Resolve validator config once
        string scriptAddress = Config[$"Validators:{scriptHash}:Address"]
            ?? throw new InvalidOperationException($"Validator {scriptHash} not configured");
        string scriptRefTxHash = Config[$"Validators:{scriptHash}:ScriptRef:TxHash"]!;
        ulong scriptRefTxIndex = ulong.Parse(Config[$"Validators:{scriptHash}:ScriptRef:TxIndex"]!);
        TransactionInput scriptRefUtxo = new(Convert.FromHexString(scriptRefTxHash), scriptRefTxIndex);

        // Fetch UTxOs from the script address once
        List<ResolvedInput> utxos = await provider.GetUtxosAsync([scriptAddress]);

        // Build per-order items
        List<BuyOrderItem> items = [];
        foreach (BuyOrderRequest orderReq in req.Orders)
        {
            string[] outRefParts = orderReq.OutRef.Split('#');
            string orderTxHash = outRefParts[0];
            ulong orderTxIndex = ulong.Parse(outRefParts[1]);

            TransactionInput orderUtxoRef = new(Convert.FromHexString(orderTxHash), orderTxIndex);

            ResolvedInput orderUtxo = utxos.First(u =>
                u.Outref.TransactionId.SequenceEqual(Convert.FromHexString(orderTxHash))
                && u.Outref.Index == orderTxIndex);

            OrderDatum orderDatum = CborSerializer.Deserialize<OrderDatum>(orderUtxo.Output.Datum()!);
            string sellerAddress = orderDatum.Destination.ToBech32(provider.NetworkType);

            byte[] askPolicyId = orderDatum.Ask.PolicyId;
            byte[] askAssetName = orderDatum.Ask.AssetName;

            string offerSubject = Convert.ToHexStringLower(orderDatum.Offer.PolicyId)
                + Convert.ToHexStringLower(orderDatum.Offer.AssetName);
            ulong offerQty = orderUtxo.Output.Amount().QuantityOf(offerSubject) ?? 0;

            // Use requested amount if provided, otherwise full offer quantity
            ulong buyQty = orderReq.Amount ?? offerQty;
            ulong requiredPayment = (buyQty * orderDatum.Price.Num + orderDatum.Price.Den - 1) / orderDatum.Price.Den;

            Value paymentValue;
            if (askPolicyId.Length == 0)
            {
                paymentValue = new Lovelace(requiredPayment);
            }
            else
            {
                TokenBundleOutput tokenBundle = new(new Dictionary<byte[], ulong>
                {
                    { askAssetName, requiredPayment }
                });
                MultiAssetOutput multiAsset = new(new Dictionary<byte[], TokenBundleOutput>
                {
                    { askPolicyId, tokenBundle }
                });
                paymentValue = new LovelaceWithMultiAsset(new Lovelace(0), multiAsset);
            }

            CborOutRef outRef = new(Convert.FromHexString(orderTxHash), orderTxIndex);
            byte[] outRefCbor = CborSerializer.Serialize(outRef);
            byte[] orderTag = HashUtil.Blake2b256(outRefCbor);

            items.Add(new BuyOrderItem(orderUtxoRef, sellerAddress, paymentValue, orderTag));
        }

        // Build unsigned transaction — route by validator type
        string validatorType = Config[$"Validators:{scriptHash}:Type"] ?? "spend";
        TransactionTemplate<BuyRequest> template = validatorType switch
        {
            "withdraw" => BuyWithdrawTemplate.Create(req, provider, scriptAddress, scriptRefUtxo, items, Convert.FromHexString(scriptHash)),
            "indexed-withdraw" => BuyIndexedWithdrawTemplate.Create(req, provider, scriptAddress, scriptRefUtxo, items, Convert.FromHexString(scriptHash)),
            _ => BuyTemplate.Create(req, provider, scriptAddress, scriptRefUtxo, items),
        };
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.ResponseAsync(new BuyResponse(unsignedTxCbor), cancellation: ct);
    }
}
