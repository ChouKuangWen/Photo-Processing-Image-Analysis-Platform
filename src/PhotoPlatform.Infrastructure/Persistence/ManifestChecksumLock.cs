using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Infrastructure.Persistence;

// Must be acquired before any Job/Run/checkpoint locks, and held through the binding INSERT/commit.
internal static class ManifestChecksumLock
{
    internal static async Task VerifyAsync(PhotoPlatformDbContext db, long jobId, string manifestId,
        string checksum, int lockTimeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lockTimeoutMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Manifest verification requires an active transaction.");
        var resource = "PhotoPlatform.Manifest:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(manifestId)));
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandTimeout = checked(lockTimeoutMilliseconds / 1000 + 5);
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=@timeout, @DbPrincipal='public';
                SELECT @result;
                """;
            Parameter(command, "@resource", resource, DbType.String);
            Parameter(command, "@timeout", lockTimeoutMilliseconds, DbType.Int32);
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (result < 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.SourceUnavailable);
            }
            command.Parameters.Clear();
            // Compare full UTF-16 representations, including trailing spaces, independent of DB collation.
            // Update-range locks prevent concurrent first-binding scans from taking shared locks
            // and then deadlocking on conversion. Without an identity index this can serialize other IDs.
            command.CommandText = """
                SELECT ManifestChecksum FROM processing_rule_manifest_bindings WITH (UPDLOCK,HOLDLOCK)
                WHERE CONVERT(varbinary(max), CONVERT(nvarchar(100), ManifestId)) =
                    CONVERT(varbinary(max), @manifestId);
                """;
            Parameter(command, "@manifestId", manifestId, DbType.String);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                if (!string.Equals(reader.GetString(0), checksum, StringComparison.Ordinal))
                    throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Mismatch);
        }
        catch (DbException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.SourceUnavailable);
        }
    }

    private static void Parameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
