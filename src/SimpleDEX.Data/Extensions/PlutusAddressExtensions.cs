using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using PlutusAddress = Chrysalis.Cbor.Types.Plutus.Address.Address;
using Chrysalis.Cbor.Types;

namespace SimpleDEX.Data.Extensions;

public static class PlutusAddressExtensions
{
    public static string ToBech32(this PlutusAddress plutusAddr, NetworkType networkType)
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

        NetworkType headerNetwork = networkType is NetworkType.Mainnet
            ? NetworkType.Mainnet
            : NetworkType.Testnet;

        return new WalletAddress(headerNetwork, addrType, paymentHash, stakeHash).ToBech32();
    }
}
