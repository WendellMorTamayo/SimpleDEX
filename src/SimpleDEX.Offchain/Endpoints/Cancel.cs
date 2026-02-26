using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
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

public class Cancel(ICardanoDataProvider provider) : Endpoint<CancelRequest, CancelResponse>
{
    public override void Configure()
    {
        Post("/api/v1/transactions/cancel");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancelRequest req, CancellationToken ct)
    {
        string scriptAddress = Config["ScriptAddress"]!;
        string scriptRefTxHash = Config["ScriptRef:TxHash"]!;
        ulong scriptRefTxIndex = ulong.Parse(Config["ScriptRef:TxIndex"]!);

        TransactionInput scriptReference = new(
            Convert.FromHexString(scriptRefTxHash),
            scriptRefTxIndex
        );

        TransactionInput orderReference = new(
            Convert.FromHexString(req.OrderTxHash),
            req.OrderIndex
        );

        // TODO: Replace with indexed DB lookup once we have the indexer
        // Fetch order UTxO to read owner address from datum (source of truth)
        List<ResolvedInput> utxos = await provider.GetUtxosAsync([scriptAddress]);
        ResolvedInput orderUtxo = utxos.First(u =>
            u.Outref.TransactionId.SequenceEqual(Convert.FromHexString(req.OrderTxHash))
            && u.Outref.Index == req.OrderIndex);

        OrderDatum orderDatum = CborSerializer.Deserialize<OrderDatum>(orderUtxo.Output.DatumOption()!.Data());
        string ownerAddress = PlutusAddressToBech32(orderDatum.Owner, provider.NetworkType);

        // Build unsigned transaction
        TransactionTemplate<CancelRequest> template = CancelTemplate.Create(
            provider, scriptAddress, ownerAddress, scriptReference, orderReference);
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.ResponseAsync(new CancelResponse(unsignedTxCbor), cancellation: ct);
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
}
