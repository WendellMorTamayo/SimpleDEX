namespace SimpleDEX.Offchain.Models;

public record FinalizeTransactionRequest(string TxCborHex, string Signature);