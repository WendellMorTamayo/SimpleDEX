using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Utils;
using FastEndpoints;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Models.Cbor;
using SimpleDEX.Offchain.Templates;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Buy(ICardanoDataProvider provider) : Endpoint<BuyRequest, BuyResponse>
{
    public override void Configure()
    {
        Post("/api/buy");
        AllowAnonymous();
    }

    public override async Task HandleAsync(BuyRequest req, CancellationToken ct)
    {
        string scriptAddress = Config["ScriptAddress"]!;
        string scriptRefTxHash = Config["ScriptRef:TxHash"]!;
        ulong scriptRefTxIndex = ulong.Parse(Config["ScriptRef:TxIndex"]!);

        // Extract owner PKH from owner address
        WalletAddress ownerAddr = new(req.OwnerAddress);
        string ownerAddress = req.OwnerAddress;

        // Parse OrderOutRef (format: "txhash#index")
        string[] outRefParts = req.OrderOutRef.Split('#');
        string orderTxHash = outRefParts[0];
        ulong orderTxIndex = ulong.Parse(outRefParts[1]);

        // Build UTxO references
        TransactionInput orderUtxoRef = new(Convert.FromHexString(orderTxHash), orderTxIndex);
        TransactionInput scriptRefUtxo = new(Convert.FromHexString(scriptRefTxHash), scriptRefTxIndex);

        // Parse ask subject
        (byte[] askPolicyId, byte[] askAssetName) = ParseSubject(req.AskSubject);

        Value paymentValue;
        if (askPolicyId.Length == 0)
        {
            paymentValue = new Lovelace(req.AskPrice);
        }
        else
        {
            TokenBundleOutput tokenBundle = new(new Dictionary<byte[], ulong>
            {
                { askAssetName, req.AskPrice }
            });
            MultiAssetOutput multiAsset = new(new Dictionary<byte[], TokenBundleOutput>
            {
                { askPolicyId, tokenBundle }
            });
            paymentValue = new LovelaceWithMultiAsset(new Lovelace(0), multiAsset);
        }

        // Compute order tag: blake2b_256(cbor(OutputReference))
        // Matches the on-chain validator's datum_tag computation
        CborOutRef outRef = new(Convert.FromHexString(orderTxHash), orderTxIndex);
        byte[] outRefCbor = CborSerializer.Serialize(outRef);
        byte[] orderTag = HashUtil.Blake2b256(outRefCbor);

        // Build unsigned transaction
        TransactionTemplate<BuyRequest> template = BuyTemplate.Create(
            req, provider, scriptAddress, ownerAddress, orderUtxoRef, scriptRefUtxo, paymentValue, orderTag);
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.OkAsync(new BuyResponse(unsignedTxCbor), cancellation: ct);
    }

    private static (byte[] PolicyId, byte[] AssetName) ParseSubject(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            return ([], []);

        string policyIdHex = subject[..56];
        string assetNameHex = subject[56..];
        return (Convert.FromHexString(policyIdHex), Convert.FromHexString(assetNameHex));
    }
}
