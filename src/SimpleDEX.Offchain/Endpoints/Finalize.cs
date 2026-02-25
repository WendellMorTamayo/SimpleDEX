using Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using FastEndpoints;
using SimpleDEX.Offchain.Models;
using CborTransaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;

namespace SimpleDEX.Offchain.Endpoints;

public class Finalize(
    ILogger<Finalize> logger
) : Endpoint<FinalizeTransactionRequest>
{
    public override void Configure()
    {
        Post("/transactions/finalize");
        AllowAnonymous();

        Description(d => d
            .WithTags("Transaction")
            .Produces<string>(StatusCodes.Status200OK)
            .ProducesProblemFE(StatusCodes.Status400BadRequest)
            .ProducesProblemFE(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblemFE(StatusCodes.Status500InternalServerError)
            .WithName("FinalizeTransaction")
        );
    }

    public override async Task HandleAsync(
        FinalizeTransactionRequest request,
        CancellationToken cancellationToken
    )
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.TxCborHex) || string.IsNullOrWhiteSpace(request.Signature))
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        try
        {
            CborTransaction unsignedTx = CborSerializer.Deserialize<CborTransaction>(Convert.FromHexString(request.TxCborHex));
            TransactionWitnessSet witnessSet = CborSerializer.Deserialize<TransactionWitnessSet>(Convert.FromHexString(request.Signature));
            List<VKeyWitness> vKeyWitnesses = witnessSet.VKeyWitnessSet()?.ToList() ?? [];
            CborTransaction signedTx = unsignedTx.Sign(vKeyWitnesses);

            await Send.OkAsync(Convert.ToHexString(CborSerializer.Serialize(signedTx)), cancellationToken);
        }
        catch (Exception ex) when (ex is FormatException || ex is ArgumentNullException)
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finalizing transaction");
            await Send.ErrorsAsync(StatusCodes.Status500InternalServerError, cancellationToken);
        }
    }
}