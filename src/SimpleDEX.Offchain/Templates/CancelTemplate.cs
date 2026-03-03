using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class CancelTemplate
{
    public static TransactionTemplate<CancelRequest> Create(
        ICardanoDataProvider provider,
        string scriptAddress,
        string ownerAddress,
        TransactionInput scriptReference,
        List<TransactionInput> orderReferences)
    {
        TransactionTemplateBuilder<CancelRequest> builder = TransactionTemplateBuilder<CancelRequest>
            .Create(provider)
            .AddStaticParty("change", ownerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptReference;
            })
            .ProcessCancelOrders(orderReferences);

        builder.AddRequiredSigner("change");
        return builder.Build(false);
    }

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
            options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new Cancel());
        };
    }

    #endregion
}
