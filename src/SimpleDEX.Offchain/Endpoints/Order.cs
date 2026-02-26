using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Models;
using FastEndpoints;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Templates;
using Address = Chrysalis.Cbor.Types.Plutus.Address.Address;
using Transaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Order(ICardanoDataProvider provider) : Endpoint<OrderRequest, OrderResponse>
{
    public override void Configure()
    {
        Post("/api/v1/transactions/order");
        AllowAnonymous();
    }

    public override async Task HandleAsync(OrderRequest req, CancellationToken ct)
    {
        string scriptAddress = Config["ScriptAddress"]!;

        // Build Plutus Address from change address (preserving staking credential if present)
        WalletAddress addr = new(req.ChangeAddress);
        byte[] ownerPkh = addr.GetPaymentKeyHash()!;
        byte[]? stakeKeyHash = addr.GetStakeKeyHash();

        Option<Inline<Credential>> stakeCredential = stakeKeyHash is not null
            ? new Some<Inline<Credential>>(new Inline<Credential>(new VerificationKey(stakeKeyHash)))
            : new None<Inline<Credential>>();

        Address ownerAddress = new(new VerificationKey(ownerPkh), stakeCredential);

        // Parse offer subject (empty = ADA, otherwise first 56 hex = policyId, rest = assetName)
        (byte[] offerPolicyId, byte[] offerAssetName) = ParseSubject(req.OfferSubject);
        (byte[] askPolicyId, byte[] askAssetName) = ParseSubject(req.AskSubject);

        // Construct OrderDatum
        OrderDatum datum = new(
            Owner: ownerPkh,
            Destination: ownerAddress,
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

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx));

        await Send.ResponseAsync(new OrderResponse(unsignedTxCbor), cancellation: ct);
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
