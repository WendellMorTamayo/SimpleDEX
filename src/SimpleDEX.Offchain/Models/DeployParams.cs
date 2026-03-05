namespace SimpleDEX.Offchain.Models;

public record DeployRequest(
    string ChangeAddress,
    string? ValidatorName = null,
    string? PlutusJsonPath = null);

public record DeployResponse(
    string UnsignedTxCborHex,
    string ContractAddress,
    string ScriptHash);
