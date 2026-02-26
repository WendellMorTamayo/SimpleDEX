using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class BuyTemplate
{
    public static TransactionTemplate<BuyRequest> Create(
        BuyRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        string sellerAddress,
        TransactionInput orderUtxoRef,
        TransactionInput scriptRefUtxo,
        Value paymentValue,
        byte[] orderTag)
    {
        return TransactionTemplateBuilder<BuyRequest>
            .Create(provider)
            .AddStaticParty("change", request.BuyerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddStaticParty("seller", sellerAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptRefUtxo;
            })
            .AddInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = orderUtxoRef;
                options.Id = "order";
                options.SetRedeemerBuilder((mapping, parameters, txBuilder) => new Buy());
            })
            .AddOutput((options, _, _) =>
            {
                options.To = "seller";
                options.Amount = paymentValue;
                options.SetDatum(new DatumTag(orderTag));
            })
            .Build(Eval: false);
    }
}
