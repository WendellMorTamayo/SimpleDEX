using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;
using FastEndpoints;
using CborTransaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;

namespace SimpleDEX.Offchain.Endpoints;

public record SubmitTxRequest(string UnsignedTxCborHex);
public record SubmitTxResponse(string TxHash);

public class SubmitTx(ICardanoDataProvider provider, IConfiguration config) : Endpoint<SubmitTxRequest, SubmitTxResponse>
{
    public override void Configure()
    {
        Post("/api/submit");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SubmitTxRequest req, CancellationToken ct)
    {
        string mnemonic = config["Mnemonic"]!;

        // Derive payment signing key: root -> 1852'/1815'/0'/0/0
        Mnemonic mnemonicObj = Mnemonic.Restore(mnemonic, wordLists: English.Words);
        PrivateKey rootKey = mnemonicObj.GetRootKey();
        PrivateKey paymentKey = rootKey
            .Derive(1852, DerivationType.HARD)
            .Derive(1815, DerivationType.HARD)
            .Derive(0, DerivationType.HARD)
            .Derive(0, DerivationType.SOFT)
            .Derive(0, DerivationType.SOFT);

        // Deserialize, sign, and submit
        CborTransaction unsignedTx = CborTransaction.Read(Convert.FromHexString(req.UnsignedTxCborHex));

        CborTransaction signedTx = unsignedTx.Sign(paymentKey);

        if (signedTx is PostMaryTransaction pmt)
        {
            signedTx = pmt with
            {
                Raw = null,
                TransactionWitnessSet = pmt.TransactionWitnessSet with { Raw = null }
            };
        }
        string txHash = await provider.SubmitTransactionAsync(signedTx);

        await Send.OkAsync(new SubmitTxResponse(txHash), cancellation: ct);
    }
}
