using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using SimpleDEX.Data.Models;

namespace SimpleDEX.Data.Extensions;

public static class TransactionOutputExtensions
{
    public static byte[]? Datum(this TransactionOutput transactionOutput)
    {
        (DatumType DatumType, byte[]? RawData) datum = transactionOutput.DatumInfo();

        if (datum.DatumType != DatumType.Inline)
            return datum.RawData;

        return datum.RawData?.ToArray();
    }

    public static (DatumType DatumType, byte[]? RawData) DatumInfo(this TransactionOutput transactionOutput)
    {
        return transactionOutput switch
        {
            AlonzoTransactionOutput a => a.DatumHash switch
            {
                null => (DatumType.None, null),
                _ => (DatumType.Hash, a.DatumHash)
            },
            PostAlonzoTransactionOutput b => b.Datum switch
            {
                InlineDatumOption inline => (DatumType.Inline, inline.Data.Value),
                DatumHashOption hash => (DatumType.Hash, hash.DatumHash),
                _ => (DatumType.None, null)
            },
            _ => (DatumType.None, null)
        };
    }
}
