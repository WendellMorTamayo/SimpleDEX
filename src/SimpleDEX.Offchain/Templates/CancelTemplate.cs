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
        TransactionInput orderReference
    )
    {
        return TransactionTemplateBuilder<CancelRequest>
            .Create(provider)
            .AddStaticParty("change", ownerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptReference;

            })
            .AddInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = orderReference;
                options.Id = "unlockUtxo";
                options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new Cancel());
            })
            .AddRequiredSigner("change")
            .Build(Eval: false);
    }
}