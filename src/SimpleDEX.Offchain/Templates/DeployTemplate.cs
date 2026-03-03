using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class DeployTemplate
{
    public static TransactionTemplate<DeployRequest> Create(
        DeployRequest request,
        ICardanoDataProvider provider,
        string contractAddress,
        PlutusV3Script script)
    {
        return TransactionTemplateBuilder<DeployRequest>
            .Create(provider)
            .AddStaticParty("change", request.ChangeAddress, isChange: true)
            .AddStaticParty("contract", contractAddress)
            .AddOutput((options, _, _) =>
            {
                options.To = "contract";
                options.Amount = new Lovelace(5_000_000);
                options.Script = script;
            })
            .Build();
    }
}
