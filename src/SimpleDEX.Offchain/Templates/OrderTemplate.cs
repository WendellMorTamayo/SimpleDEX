using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public record OrderOutputItem(OrderDatum Datum, Value OutputValue);

public static class OrderTemplate
{
    public static TransactionTemplate<OrderRequest> Create(
        OrderRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        List<OrderOutputItem> items)
    {
        TransactionTemplateBuilder<OrderRequest> builder = TransactionTemplateBuilder<OrderRequest>
            .Create(provider)
            .AddStaticParty("change", request.ChangeAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress);

        foreach (OrderOutputItem item in items)
        {
            builder.AddOutput((options, _, _) =>
            {
                options.To = "contract";
                options.Amount = item.OutputValue;
                options.SetDatum(item.Datum);
            });
        }

        return builder.Build();
    }
}
