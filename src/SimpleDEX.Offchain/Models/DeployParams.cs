namespace SimpleDEX.Offchain.Models;

public record DeployParams(
    byte[] CompiledCode,
    string ChangeAddress,
    ulong LockAmount);