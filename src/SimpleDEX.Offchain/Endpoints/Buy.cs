using Chrysalis.Cbor.Serialization;
using SimpleDEX.Data.Extensions;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using FastEndpoints;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Templates;
using Address = Chrysalis.Cbor.Types.Plutus.Address.Address;
using Credential = Chrysalis.Cbor.Types.Plutus.Address.Credential;
using Transaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;
using TransactionInput = Chrysalis.Cbor.Types.Cardano.Core.Transaction.TransactionInput;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Buy(ICardanoDataProvider provider) : Endpoint<BuyRequest, BuyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/transactions/buy");
        AllowAnonymous();
    }

    public override async Task HandleAsync(BuyRequest req, CancellationToken ct)
    {
        string scriptAddress = Config["ScriptAddress"]!;
        string scriptRefTxHash = Config["ScriptRef:TxHash"]!;
        ulong scriptRefTxIndex = ulong.Parse(Config["ScriptRef:TxIndex"]!);

        // Parse OrderOutRef (format: "txhash#index")
        string[] outRefParts = req.OrderOutRef.Split('#');
        string orderTxHash = outRefParts[0];
        ulong orderTxIndex = ulong.Parse(outRefParts[1]);

        // Build UTxO references
        TransactionInput orderUtxoRef = new(Convert.FromHexString(orderTxHash), orderTxIndex);
        TransactionInput scriptRefUtxo = new(Convert.FromHexString(scriptRefTxHash), scriptRefTxIndex);

        // TODO: Replace with indexed DB lookup once we have the indexer
        // Fetch order UTxO to read seller address from datum (source of truth)
        List<ResolvedInput> utxos = await provider.GetUtxosAsync([scriptAddress]);
        ResolvedInput orderUtxo = utxos.First(u =>
            u.Outref.TransactionId.SequenceEqual(Convert.FromHexString(orderTxHash))
            && u.Outref.Index == orderTxIndex);

        OrderDatum orderDatum = CborSerializer.Deserialize<OrderDatum>(orderUtxo.Output.Datum()!);
        string sellerAddress = PlutusAddressToBech32(orderDatum.Destination, provider.NetworkType);

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
            req, provider, scriptAddress, sellerAddress, orderUtxoRef, scriptRefUtxo, paymentValue, orderTag);
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.ResponseAsync(new BuyResponse(unsignedTxCbor), cancellation: ct);
    }

    private static string PlutusAddressToBech32(Address plutusAddr, NetworkType networkType)
    {
        VerificationKey vk = (VerificationKey)plutusAddr.PaymentCredential;
        byte[] paymentHash = vk.VerificationKeyHash;

        byte[]? stakeHash = plutusAddr.StakeCredential switch
        {
            Some<Inline<Credential>> some => ((VerificationKey)some.Value.Value).VerificationKeyHash,
            _ => null
        };

        AddressType addrType = stakeHash is not null
            ? AddressType.Base
            : AddressType.EnterprisePayment;

        // WalletAddress header only supports Testnet/Mainnet nibbles;
        // Preview/Preprod are testnet variants
        NetworkType headerNetwork = networkType is NetworkType.Mainnet
            ? NetworkType.Mainnet
            : NetworkType.Testnet;

        return new WalletAddress(headerNetwork, addrType, paymentHash, stakeHash).ToBech32();
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
