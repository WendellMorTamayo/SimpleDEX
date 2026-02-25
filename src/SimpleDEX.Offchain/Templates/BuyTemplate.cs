using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Models.Cbor;

namespace SimpleDEX.Offchain.Templates;

public static class BuyTemplate
{
    public static TransactionTemplate<BuyRequest> Create(
        BuyRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        string ownerAddress,
        TransactionInput orderUtxoRef,
        TransactionInput scriptRefUtxo,
        Value paymentValue,
        byte[] orderTag)
    {
        return TransactionTemplateBuilder<BuyRequest>
            .Create(provider)
            .AddStaticParty("change", request.OwnerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddStaticParty("owner", ownerAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptRefUtxo;
            })
            .AddInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = orderUtxoRef;
                options.SetRedeemerBuilder<Buy>(
                    (_, _, _) => new Buy(),
                    RedeemerTag.Spend);
            })
            .AddOutput((options, _, _) =>
            {
                options.To = "owner";
                options.Amount = paymentValue;
                options.SetDatum(new DatumTag(orderTag));
            })
            .AddRequiredSigner("change")
            .Build();
    }
}
