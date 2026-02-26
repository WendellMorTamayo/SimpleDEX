using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using SimpleDEX.Data.Extensions;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Microsoft.EntityFrameworkCore;
using SimpleDEX.Data;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Data.Models;
using Address = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Sync.Reducers;

public class OrderReducer(
    IDbContextFactory<SimpleDEXDbContext> dbContextFactory,
    IConfiguration configuration
) : IReducer<Order>
{
    private readonly string _scriptHash = configuration["ScriptHash"]
        ?? throw new InvalidOperationException("ScriptHash not configured");

    public async Task RollForwardAsync(Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();

        List<Order> newOrders = CollectNewOrders(block, slot);
        Dictionary<string, TransactionInput> inputsByOutRef = CollectInputOutRefs(block);

        if (newOrders.Count == 0 && inputsByOutRef.Count == 0) return;

        await using SimpleDEXDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        if (newOrders.Count > 0)
            dbContext.Orders.AddRange(newOrders);

        if (inputsByOutRef.Count > 0)
            await ProcessSpentOrders(dbContext, block, inputsByOutRef, slot);

        await dbContext.SaveChangesAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        await using SimpleDEXDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        await RevertSpentOrders(dbContext, slot);
        await DeleteNewOrders(dbContext, slot);
    }

    private List<Order> CollectNewOrders(Block block, ulong slot) =>
    [
        .. block.TransactionBodies()
            .SelectMany(txBody =>
            {
                string txHash = txBody.Hash();
                return txBody.Outputs()
                    .Select((output, index) => (TxHash: txHash, Index: index, Output: output));
            })
            .Where(x => IsScriptOutput(x.Output))
            .Select(x => TryParseOrder(x.TxHash, x.Index, x.Output, slot))
            .OfType<Order>()
    ];

    private static Dictionary<string, TransactionInput> CollectInputOutRefs(Block block) =>
        block.TransactionBodies()
            .SelectMany(txBody => txBody.Inputs())
            .ToDictionary(
                input => $"{Convert.ToHexStringLower(input.TransactionId())}#{input.Index()}",
                input => input);

    private static async Task ProcessSpentOrders(
        SimpleDEXDbContext dbContext,
        Block block,
        Dictionary<string, TransactionInput> inputsByOutRef,
        ulong slot)
    {
        List<Order> matchedOrders = await FindMatchingOrders(dbContext, inputsByOutRef.Keys);
        if (matchedOrders.Count == 0) return;

        matchedOrders.ForEach(order =>
            ApplySpentStatus(dbContext, block, inputsByOutRef[order.OutRef], order, slot));
    }

    private static async Task<List<Order>> FindMatchingOrders(
        SimpleDEXDbContext dbContext,
        IEnumerable<string> outRefs) =>
        await dbContext.Orders
            .Where(o => outRefs.Contains(o.OutRef) && o.Status == OrderStatus.Open)
            .ToListAsync();

    private static void ApplySpentStatus(
        SimpleDEXDbContext dbContext,
        Block block,
        TransactionInput input,
        Order order,
        ulong slot)
    {
        RedeemerEntry? redeemer = input.Redeemer(block);
        OrderStatus? status = redeemer is not null ? ResolveStatus(redeemer) : null;
        if (status.HasValue)
            dbContext.Entry(order).CurrentValues
                .SetValues(order with { Status = status.Value, SpentSlot = slot });
    }

    private static async Task RevertSpentOrders(SimpleDEXDbContext dbContext, ulong slot) =>
        await dbContext.Orders
            .Where(o => o.SpentSlot >= slot)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Open)
                .SetProperty(o => o.SpentSlot, (ulong?)null));

    private static async Task DeleteNewOrders(SimpleDEXDbContext dbContext, ulong slot) =>
        await dbContext.Orders
            .Where(o => o.Slot >= slot)
            .ExecuteDeleteAsync();

    private bool IsScriptOutput(TransactionOutput output)
    {
        try
        {
            Address address = new(output.Address());
            if (!address.ToBech32().StartsWith("addr")) return false;

            byte[]? pkh = address.GetPaymentKeyHash();
            if (pkh is null) return false;

            return Convert.ToHexStringLower(pkh) == _scriptHash
                && output.Datum() is not null;
        }
        catch { return false; }
    }

    private static Order? TryParseOrder(string txHash, int index, TransactionOutput output, ulong slot)
    {
        try
        {
            OrderDatum datum = CborSerializer.Deserialize<OrderDatum>(output.Datum());

            return new Order(
                OutRef: $"{txHash}#{index}",
                OwnerAddress: Address.FromBytes(output.Address()).ToBech32(),
                OfferSubject: Convert.ToHexStringLower(datum.Offer.PolicyId) + Convert.ToHexStringLower(datum.Offer.AssetName),
                AskSubject: Convert.ToHexStringLower(datum.Ask.PolicyId) + Convert.ToHexStringLower(datum.Ask.AssetName),
                Price: datum.Price,
                Slot: slot,
                Status: OrderStatus.Open,
                SpentSlot: null
            );
        }
        catch { return null; }
    }

    private static OrderStatus? ResolveStatus(RedeemerEntry redeemer)
    {
        try
        {
            return CborSerializer.Deserialize<OrderRedeemer>(redeemer.Data.Raw!.Value) switch
            {
                Buy => OrderStatus.Filled,
                Cancel => OrderStatus.Cancelled,
                _ => null
            };
        }
        catch { return null; }
    }
}
