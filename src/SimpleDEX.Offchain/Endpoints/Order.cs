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
        if (req.Orders.Count == 0)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        string scriptAddress = Config[$"Validators:{req.ScriptHash}:Address"]
            ?? throw new InvalidOperationException($"Validator {req.ScriptHash} not configured");

        // Build Plutus Address from change address (preserving staking credential if present)
        WalletAddress addr = new(req.ChangeAddress);
        byte[] ownerPkh = addr.GetPaymentKeyHash()!;
        byte[]? stakeKeyHash = addr.GetStakeKeyHash();

        Option<Inline<Credential>> stakeCredential = stakeKeyHash is not null
            ? new Some<Inline<Credential>>(new Inline<Credential>(new VerificationKey(stakeKeyHash)))
            : new None<Inline<Credential>>();

        Address ownerAddress = new(new VerificationKey(ownerPkh), stakeCredential);

        // Build per-order output items
        List<OrderOutputItem> items = [];
        foreach (OrderItem orderItem in req.Orders)
        {
            (byte[] offerPolicyId, byte[] offerAssetName) = ParseSubject(orderItem.OfferSubject);
            (byte[] askPolicyId, byte[] askAssetName) = ParseSubject(orderItem.AskSubject);

            OrderDatum datum = new(
                Owner: ownerPkh,
                Destination: ownerAddress,
                Offer: new Unit(offerPolicyId, offerAssetName, orderItem.OfferAmount),
                Ask: new TokenId(askPolicyId, askAssetName),
                Price: new RationalC(orderItem.PriceNum, orderItem.PriceDen)
            );

            Value outputValue;
            if (offerPolicyId.Length == 0)
            {
                outputValue = new Lovelace(orderItem.OfferAmount);
            }
            else
            {
                TokenBundleOutput tokenBundle = new(new Dictionary<byte[], ulong>
                {
                    { offerAssetName, orderItem.OfferAmount }
                });
                MultiAssetOutput multiAsset = new(new Dictionary<byte[], TokenBundleOutput>
                {
                    { offerPolicyId, tokenBundle }
                });
                outputValue = new LovelaceWithMultiAsset(new Lovelace(0), multiAsset);
            }

            items.Add(new OrderOutputItem(datum, outputValue));
        }

        // Build unsigned transaction
        TransactionTemplate<OrderRequest> template = OrderTemplate.Create(req, provider, scriptAddress, items);
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
