namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal static class BindingSqlChecks
{
    // UTF-8 binary collation also prevents best-fit conversion of non-ASCII hash input.
    internal const string HashCollation = "Latin1_General_100_BIN2_UTF8";
    internal static string Hash(string column) =>
        $"DATALENGTH([{column}]) = 64 AND LEN([{column}]) = 64 AND [{column}] COLLATE {HashCollation} NOT LIKE '%[^0-9a-f]%'";
    internal static string Text(string column) =>
        $"LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([{column}], CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''), CHAR(13), ''), NCHAR(160), '')))) > 0";
}
