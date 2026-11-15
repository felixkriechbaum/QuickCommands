using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace QuickCommands.Server.Services;

internal static class DbErrors
{
    /// <summary>MySQL error 1062: a unique index rejected the row (e.g. a concurrent duplicate star or revision).</summary>
    public static bool IsDuplicateKey(this DbUpdateException e) =>
        e.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
