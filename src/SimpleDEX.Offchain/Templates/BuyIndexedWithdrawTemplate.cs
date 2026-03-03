using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Utils;
using SimpleDEX.Data.Models.Cbor;
using SimpleDEX.Offchain.Models;

namespace SimpleDEX.Offchain.Templates;

public static class BuyIndexedWithdrawTemplate
{
    public static TransactionTemplate<BuyRequest> Create(
        BuyRequest request,
        ICardanoDataProvider provider,
        string scriptAddress,
        TransactionInput scriptRefUtxo,
        List<BuyOrderItem> items,
        byte[] scriptHash)
    {
        // Build reward address: 0xF0 header + script hash, bech32 with stake_test prefix
        byte[] rewardAddressBytes = new byte[1 + scriptHash.Length];
        rewardAddressBytes[0] = 0xF0;
        Buffer.BlockCopy(scriptHash, 0, rewardAddressBytes, 1, scriptHash.Length);
        string rewardAddress = Bech32Util.Encode(rewardAddressBytes, "stake_test");

        TransactionTemplateBuilder<BuyRequest> builder = TransactionTemplateBuilder<BuyRequest>
            .Create(provider)
            .AddStaticParty("change", request.BuyerAddress, isChange: true)
            .AddStaticParty("contract", scriptAddress)
            .AddStaticParty("reward", rewardAddress)
            .AddReferenceInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = scriptRefUtxo;
            })
            .AddWithdrawal((options, _) =>
            {
                options.From = "reward";
                options.Amount = 0;
                options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
                    new Redeemer<CborBase>(RedeemerTag.Reward, 0, new PlutusVoid(), new ExUnits(500000, 200000000));
            });

        // Add inputs and outputs in matching order (positional 1:1)
        int idx = 0;
        foreach (BuyOrderItem item in items)
        {
            string sellerParty = $"seller_{idx}";
            string inputId = Convert.ToHexStringLower(item.OrderUtxoRef.TransactionId) + item.OrderUtxoRef.Index;

            builder.AddStaticParty(sellerParty, item.SellerAddress);
            builder.AddInput((options, _) =>
            {
                options.From = "contract";
                options.UtxoRef = item.OrderUtxoRef;
                options.Id = inputId;
                options.RedeemerBuilder = (mapping, parameters, txBuilder) =>
                    new Redeemer<CborBase>(RedeemerTag.Spend, 0, new Buy(), new ExUnits(500000, 200000000));
            });
            builder.AddOutput((options, _, _) =>
            {
                options.To = sellerParty;
                options.Amount = item.PaymentValue;
            });
            idx++;
        }

        return builder.Build(false);
    }
}
