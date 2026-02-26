using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class OrderTemplate
{
    public static TransactionTemplate<OrderRequest> Create(
        OrderRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        OrderDatum datum,
        Value outputValue)
    {
        return TransactionTemplateBuilder<OrderRequest>
            .Create(provider)
            .AddStaticParty("change", request.ChangeAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddOutput((options, _, _) =>
            {
                options.To = "contract";
                options.Amount = outputValue;
                options.SetDatum(datum);
            })
            .Build();
    }
}
