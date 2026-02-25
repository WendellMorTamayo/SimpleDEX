using FastEndpoints;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Tx.Models;
using SimpleDEX.Offchain.Models;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using SimpleDEX.Offchain.Templates;

namespace SimpleDEX.Offchain.Endpoints;

public class Cancel(ICardanoDataProvider provider) : Endpoint <CancelRequest, CancelResponse>
{
    public override void Configure()
    {
        Post("api/cancel");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancelRequest req, CancellationToken ct)
    {
        string scriptAddress = Config["ScriptAddress"]!;
        string scriptRefTxHash = Config["ScriptRef:TxHash"]!;
        ulong scriptRefTxIndex = ulong.Parse(Config["ScriptRef:TxIndex"]!);

        TransactionInput scriptReference = new TransactionInput(
            Convert.FromHexString(scriptRefTxHash), 
            scriptRefTxIndex
        );

        TransactionInput orderReference = new TransactionInput(
            Convert.FromHexString(req.OrderTxHash), 
            req.OrderIndex
        );

        // Build unsigned transaction
        TransactionTemplate<CancelRequest> template = CancelTemplate.Create(req, provider, scriptAddress, scriptReference, orderReference);
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.OkAsync(new CancelResponse(unsignedTxCbor), cancellation: ct);

    }
}
    