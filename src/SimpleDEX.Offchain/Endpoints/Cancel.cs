using Chrysalis.Cbor.Serialization;
using SimpleDEX.Data.Extensions;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SimpleDEX.Data;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Templates;
using Address = Chrysalis.Cbor.Types.Plutus.Address.Address;
using Credential = Chrysalis.Cbor.Types.Plutus.Address.Credential;
using Transaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;
using TransactionInput = Chrysalis.Cbor.Types.Cardano.Core.Transaction.TransactionInput;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Cancel(ICardanoDataProvider provider, SimpleDEXDbContext db) : Endpoint<CancelRequest, CancelResponse>
{
    public override void Configure()
    {
        Post("/api/v1/transactions/cancel");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancelRequest req, CancellationToken ct)
    {
        if (req.Orders.Count == 0)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Look up all orders in one query
        List<string> outRefs = req.Orders.Select(o => $"{o.TxHash}#{o.Index}").ToList();
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
        TransactionInput scriptReference = new(Convert.FromHexString(scriptRefTxHash), scriptRefTxIndex);

        // Fetch UTxOs from the script address once
        List<ResolvedInput> utxos = await provider.GetUtxosAsync([scriptAddress]);

        // Read owner address from the first order's datum (all must share same owner for cancel)
        CancelOrderRef firstRef = req.Orders[0];
        ResolvedInput firstUtxo = utxos.First(u =>
            u.Outref.TransactionId.SequenceEqual(Convert.FromHexString(firstRef.TxHash))
            && u.Outref.Index == firstRef.Index);
        OrderDatum firstDatum = CborSerializer.Deserialize<OrderDatum>(firstUtxo.Output.Datum()!);
        string ownerAddress = firstDatum.Destination.ToBech32(provider.NetworkType);

        // Build order references
        List<TransactionInput> orderReferences = req.Orders
            .Select(o => new TransactionInput(Convert.FromHexString(o.TxHash), o.Index))
            .ToList();

        // Build unsigned transaction — route by validator type
        string validatorType = Config[$"Validators:{scriptHash}:Type"] ?? "spend";
        TransactionTemplate<CancelRequest> template = validatorType switch
        {
            "withdraw" => CancelWithdrawTemplate.Create(provider, scriptAddress, ownerAddress, scriptReference, orderReferences, Convert.FromHexString(scriptHash)),
            "indexed-withdraw" => CancelIndexedWithdrawTemplate.Create(provider, scriptAddress, ownerAddress, scriptReference, orderReferences, Convert.FromHexString(scriptHash)),
            _ => CancelTemplate.Create(provider, scriptAddress, ownerAddress, scriptReference, orderReferences),
        };
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.ResponseAsync(new CancelResponse(unsignedTxCbor), cancellation: ct);
    }
}
