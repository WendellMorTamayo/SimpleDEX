using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using SimpleDEX.Offchain.Models;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace SimpleDEX.Offchain.Templates;

public static class DeployTemplate
{
    public static TransactionTemplate<DeployParams> Create(DeployParams deployParams, ICardanoDataProvider provider)
    {
        PlutusV3Script script = new(new Value3(3), Convert.FromHexString(deployParams.CompiledCode));

        // Calculate script hash (prefix 0x03 for PlutusV3)
        byte[] prefix = [0x03];
        byte[] scriptHashBytes = HashUtil.Blake2b224([.. prefix, .. script.ScriptBytes]);
        string scriptHashHex = Convert.ToHexStringLower(scriptHashBytes);

        WalletAddress contractAddr = new(NetworkType.Testnet, AddressType.EnterpriseScriptPayment, scriptHashBytes, null);
        string contractAddress = contractAddr.ToBech32();

        TransactionTemplateBuilder<DeployParams> txBuilder = TransactionTemplateBuilder<DeployParams>
            .Create(provider)
            .AddStaticParty("change", deployParams.ChangeAddress, isChange: true)
            .AddStaticParty("contract", contractAddress)
            .AddOutput((options, _, _) =>
            {
                options.To = "contract";
                options.Amount = new Lovelace(deployParams.LockAmount);
                options.Script = script;
            });
        
        return txBuilder.Build();
    }
}