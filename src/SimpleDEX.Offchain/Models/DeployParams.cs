namespace SimpleDEX.Offchain.Models;

public record DeployRequest(
    string ChangeAddress,
    ulong LockAmount = 5_000_000);

public record DeployResponse(
    string UnsignedTxCborHex,
    string ContractAddress,
    string ScriptHash);
