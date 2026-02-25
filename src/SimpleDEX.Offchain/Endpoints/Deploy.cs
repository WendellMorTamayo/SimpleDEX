using System.Text.Json.Nodes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;
using FastEndpoints;
using SimpleDEX.Offchain.Models;
using SimpleDEX.Offchain.Templates;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Endpoints;

public class Deploy(ICardanoDataProvider provider) : Endpoint<DeployRequest, DeployResponse>
{
    public override void Configure()
    {
        Post("/api/deploy");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DeployRequest req, CancellationToken ct)
    {
        string plutusJsonPath = Config["PlutusJsonPath"]!;
        string validatorName = Config["ValidatorName"]!;

        // Load validator from plutus.json
        string plutusJson = await File.ReadAllTextAsync(plutusJsonPath, ct);
        JsonNode root = JsonNode.Parse(plutusJson)!;
        JsonArray validators = root["validators"]!.AsArray();

        string? compiledCode = null;
        string? scriptHash = null;
        foreach (JsonNode? v in validators)
        {
            if (v!["title"]!.ToString() == validatorName)
            {
                compiledCode = v["compiledCode"]!.ToString();
                scriptHash = v["hash"]!.ToString();
                break;
            }
        }

        if (compiledCode is null || scriptHash is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Build script and derive contract address
        PlutusV3Script script = new(new Value3(3), Convert.FromHexString(compiledCode));
        byte[] scriptHashBytes = Convert.FromHexString(scriptHash);

        WalletAddress contractAddr = new(NetworkType.Testnet, AddressType.EnterpriseScriptPayment, scriptHashBytes, null);
        string contractAddress = contractAddr.ToBech32();

        // Build unsigned transaction
        TransactionTemplate<DeployRequest> template = DeployTemplate.Create(req, provider, contractAddress, script);
        Transaction unsignedTx = await template(req);

        string unsignedTxCbor = Convert.ToHexString(CborSerializer.Serialize(unsignedTx)).ToLowerInvariant();

        await Send.OkAsync(new DeployResponse(unsignedTxCbor, contractAddress, scriptHash), cancellation: ct);
    }
}
