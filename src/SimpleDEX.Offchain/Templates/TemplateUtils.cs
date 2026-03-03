using Chrysalis.Wallet.Utils;

namespace SimpleDEX.Offchain.Templates;

public static class TemplateUtils
{
    public static string BuildRewardAddress(byte[] scriptHash)
    {
        byte[] rewardAddressBytes = new byte[1 + scriptHash.Length];
        rewardAddressBytes[0] = 0xF0;
        Buffer.BlockCopy(scriptHash, 0, rewardAddressBytes, 1, scriptHash.Length);
        return Bech32Util.Encode(rewardAddressBytes, "stake_test");
    }
}
