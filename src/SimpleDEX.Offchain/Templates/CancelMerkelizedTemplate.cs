using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class CancelMerkelizedTemplate
{
    public static TransactionTemplate<CancelRequest> Create(
        ICardanoDataProvider provider,
        string scriptAddress,
        string ownerAddress,
        TransactionInput mainScriptRefUtxo,
        List<TransactionInput> orderReferences,
        TransactionInput logicScriptRefUtxo,
        byte[] logicScriptHash,
        string logicAddress)
    {
        string rewardAddress = TemplateUtils.BuildRewardAddress(logicScriptHash);

        TransactionTemplateBuilder<CancelRequest> builder = TransactionTemplateBuilder<CancelRequest>
            .Create(provider)
            .AddStaticParty("change", ownerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddStaticParty("logic", logicAddress)
            .AddStaticParty("reward", rewardAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = mainScriptRefUtxo;
            })
            .AddReferenceInput((options, _) =>
            {
                options.From = "logic";
                options.UtxoRef = logicScriptRefUtxo;
            })
            .AddWithdrawal((options, _) => SetupWithdrawal(options))
            .ProcessCancelOrders(orderReferences);

        builder.AddRequiredSigner("change");
        return builder.Build(false);
    }

    #region Withdrawal Setup

    private static void SetupWithdrawal(WithdrawalOptions<CancelRequest> options)
    {
        options.From = "reward";
        options.Amount = 0;
        options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new PlutusVoid());
    }

    #endregion

    #region Input Setup

    private static TransactionTemplateBuilder<CancelRequest> ProcessCancelOrders(
        this TransactionTemplateBuilder<CancelRequest> builder,
        List<TransactionInput> orderReferences)
    {
        int idx = 0;
        foreach (TransactionInput orderRef in orderReferences)
        {
            builder.AddInput(CreateInput(orderRef, idx));
            idx++;
        }

        return builder;
    }

    private static InputConfig<CancelRequest> CreateInput(TransactionInput orderRef, int idx)
    {
        string inputId = $"cancel_{idx}";

        return (options, _) =>
        {
            options.From = "contract";
            options.UtxoRef = orderRef;
            options.Id = inputId;
            options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new MerkelizedCancel());
        };
    }

    #endregion
}
