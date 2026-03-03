using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class BuyIndexedWithdrawTemplate
{
    public static TransactionTemplate<BuyRequest> Create(
        BuyRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        TransactionInput scriptRefUtxo,
        List<BuyOrderItem> items,
        byte[] scriptHash)
    {
        string rewardAddress = TemplateUtils.BuildRewardAddress(scriptHash);

        TransactionTemplateBuilder<BuyRequest> builder = TransactionTemplateBuilder<BuyRequest>
            .Create(provider)
            .AddStaticParty("change", request.BuyerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddStaticParty("reward", rewardAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptRefUtxo;
            })
            .AddWithdrawal((options, _) => SetupWithdrawal(options))
            .ProcessBuyOrders(items);

        return builder.Build(false);
    }

    #region Withdrawal Setup

    private static void SetupWithdrawal(WithdrawalOptions<BuyRequest> options)
    {
        options.From = "reward";
        options.Amount = 0;
        options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new PlutusVoid());
    }

    #endregion

    #region Input/Output Setup

    private static TransactionTemplateBuilder<BuyRequest> ProcessBuyOrders(
        this TransactionTemplateBuilder<BuyRequest> builder,
        List<BuyOrderItem> items)
    {
        int idx = 0;
        foreach (BuyOrderItem item in items)
        {
            string sellerParty = $"seller_{idx}";
            string inputId = Convert.ToHexStringLower(item.OrderUtxoRef.TransactionId) + item.OrderUtxoRef.Index;
            string outputId = $"payment_{idx}";

            builder.AddStaticParty(sellerParty, item.SellerAddress);
            builder.AddInput(CreateInput(item, inputId, outputId));
            builder.AddOutput(CreateOutput(item, sellerParty, inputId, outputId));
            idx++;
        }

        return builder;
    }

    private static InputConfig<BuyRequest> CreateInput(BuyOrderItem item, string inputId, string outputId)
    {
        return (options, _) =>
        {
            options.From = "contract";
            options.UtxoRef = item.OrderUtxoRef;
            options.Id = inputId;
            options.SetRedeemerBuilder((mapping, parameters, txBuilder) => CreateIndexedBuyRedeemer(mapping, inputId, outputId));
        };
    }

    private static OutputConfig<BuyRequest> CreateOutput(BuyOrderItem item, string sellerParty, string inputId, string outputId)
    {
        return (options, _, _) =>
        {
            options.To = sellerParty;
            options.Amount = item.PaymentValue;
            options.AssociatedInputId = inputId;
            options.Id = outputId;
        };
    }

    #endregion

    #region Redeemer Creation

    private static IndexedBuy CreateIndexedBuyRedeemer(InputOutputMapping mapping, string inputId, string outputId)
    {
        (ulong _, Dictionary<string, ulong> outputIndexes) = mapping.GetInput(inputId);
        ulong resolvedOutputIndex = outputIndexes[outputId];
        return new IndexedBuy(resolvedOutputIndex);
    }

    #endregion
}
