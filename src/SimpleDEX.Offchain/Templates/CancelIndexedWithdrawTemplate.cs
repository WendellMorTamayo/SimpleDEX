using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Utils;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class CancelIndexedWithdrawTemplate
{
    public static TransactionTemplate<CancelRequest> Create(
        ICardanoDataProvider provider,
        string scriptAddress,
        string ownerAddress,
        TransactionInput scriptReference,
        List<TransactionInput> orderReferences,
        byte[] scriptHash)
    {
        // Build reward address: 0xF0 header + script hash, bech32 with stake_test prefix
        byte[] rewardAddressBytes = new byte[1 + scriptHash.Length];
        rewardAddressBytes[0] = 0xF0;
        Buffer.BlockCopy(scriptHash, 0, rewardAddressBytes, 1, scriptHash.Length);
        string rewardAddress = Bech32Util.Encode(rewardAddressBytes, "stake_test");

        TransactionTemplateBuilder<CancelRequest> builder = TransactionTemplateBuilder<CancelRequest>
            .Create(provider)
            .AddStaticParty("change", ownerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddStaticParty("reward", rewardAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptReference;
            })
            .AddWithdrawal((options, _) =>
            {
                options.From = "reward";
                options.Amount = 0;
                options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
                    new Redeemer<CborBase>(RedeemerTag.Reward, 0, new PlutusVoid(), new ExUnits(500000, 200000000));
            });

        int idx = 0;
        foreach (TransactionInput orderRef in orderReferences)
        {
            string inputId = $"cancel_{idx}";
            builder.AddInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = orderRef;
                options.Id = inputId;
                options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
                    new Redeemer<CborBase>(RedeemerTag.Spend, 0, new Cancel(), new ExUnits(500000, 200000000));
            });
            idx++;
        }

        builder.AddRequiredSigner("change");
        return builder.Build(false);
    }
}
