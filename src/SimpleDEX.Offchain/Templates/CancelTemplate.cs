// User Cancelling their order must receive as output the cancelled order
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Models.Cbor;

namespace SimpleDEX.Offchain.Templates;

public static class CancelTemplate
{
    public static TransactionTemplate<CancelRequest> Create(
        CancelRequest request,
        ICardanoDataProvider provider,
        string scriptAddress, //why is this string and not type Address?
        TransactionInput scriptReference,
        TransactionInput orderReference
    )
    {
        return TransactionTemplateBuilder<CancelRequest>
            .Create(provider)
            .AddStaticParty("owner", request.Owner, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptReference;
                options.Id = "deployRef";

            })
            .AddInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = orderReference;
                options.SetRedeemerBuilder<Cancel>((_, _, _) => new Cancel());
                options.Id = "unlockUtxo";
            })
            .Build();
    }
}