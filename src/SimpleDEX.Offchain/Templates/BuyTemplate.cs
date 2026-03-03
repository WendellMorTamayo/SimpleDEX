using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public record BuyOrderItem(
    TransactionInput OrderUtxoRef,
    string SellerAddress,
    Value PaymentValue,
    byte[] OrderTag);

public static class BuyTemplate
{
    public static TransactionTemplate<BuyRequest> Create(
        BuyRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        TransactionInput scriptRefUtxo,
        List<BuyOrderItem> items)
    {
        TransactionTemplateBuilder<BuyRequest> builder = TransactionTemplateBuilder<BuyRequest>
            .Create(provider)
            .AddStaticParty("change", request.BuyerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptRefUtxo;
            })
            .ProcessBuyOrders(items);

        return builder.Build(false);
    }

    #region Input/Output Setup

    private static TransactionTemplateBuilder<BuyRequest> ProcessBuyOrders(
        this TransactionTemplateBuilder<BuyRequest> builder,
        List<BuyOrderItem> items)
    {
        int idx = 0;
        foreach (BuyOrderItem item in items)
        {
            string sellerParty = $"seller_{idx}";

            builder.AddStaticParty(sellerParty, item.SellerAddress);
            builder.AddInput(CreateInput(item));
            builder.AddOutput(CreateOutput(item, sellerParty));
            idx++;
        }

        return builder;
    }

    private static InputConfig<BuyRequest> CreateInput(BuyOrderItem item)
    {
        string inputId = Convert.ToHexStringLower(item.OrderUtxoRef.TransactionId) + item.OrderUtxoRef.Index;

        return (options, _) =>
        {
            options.From = "contract";
            options.UtxoRef = item.OrderUtxoRef;
            options.Id = inputId;
            options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new Buy());
        };
    }

    private static OutputConfig<BuyRequest> CreateOutput(BuyOrderItem item, string sellerParty)
    {
        return (options, _, _) =>
        {
            options.To = sellerParty;
            options.Amount = item.PaymentValue;
            options.SetDatum(new DatumTag(item.OrderTag));
        };
    }

    #endregion
}
