using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Utils;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class BuyWithdrawTemplate
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
