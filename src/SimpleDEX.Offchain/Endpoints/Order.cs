using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using FastEndpoints;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Models.Cbor;
using SimpleDEX.Offchain.Templates;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Order(ICardanoDataProvider provider) : Endpoint<OrderRequest, OrderResponse>
{
    public override void Configure()
    {
        Post("/api/order");
        AllowAnonymous();
    }

    public override async Task HandleAsync(OrderRequest req, CancellationToken ct)
    {
        string scriptAddress = Config["ScriptAddress"]!;

        // Extract owner PKH from change address
        WalletAddress addr = new(req.ChangeAddress);
        byte[] ownerPkh = addr.GetPaymentKeyHash()!;

        // Parse offer subject (empty = ADA, otherwise first 56 hex = policyId, rest = assetName)
        (byte[] offerPolicyId, byte[] offerAssetName) = ParseSubject(req.OfferSubject);
        (byte[] askPolicyId, byte[] askAssetName) = ParseSubject(req.AskSubject);

        // Construct OrderDatum
        OrderDatum datum = new(
            Owner: ownerPkh,
            Offer: new TokenId(offerPolicyId, offerAssetName),
            Ask: new TokenId(askPolicyId, askAssetName),
            Price: req.AskPrice
        );

        // Build output value
        Value outputValue;
        if (offerPolicyId.Length == 0)
        {
            outputValue = new Lovelace(req.OfferAmount);
        }
        else
        {
            TokenBundleOutput tokenBundle = new(new Dictionary<byte[], ulong>
            {
                { offerAssetName, req.OfferAmount }
            });
            MultiAssetOutput multiAsset = new(new Dictionary<byte[], TokenBundleOutput>
            {
                { offerPolicyId, tokenBundle }
            });
            outputValue = new LovelaceWithMultiAsset(new Lovelace(0), multiAsset);
        }

        // Build unsigned transaction
        TransactionTemplate<OrderRequest> template = OrderTemplate.Create(req, provider, scriptAddress, datum, outputValue);
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.OkAsync(new OrderResponse(unsignedTxCbor), cancellation: ct);
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
