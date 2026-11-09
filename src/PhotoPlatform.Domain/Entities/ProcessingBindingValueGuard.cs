namespace PhotoPlatform.Domain.Entities;

internal static class ProcessingBindingValueGuard
{
    internal static void Text(string value, int length, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > length) throw new ArgumentOutOfRangeException(name);
    }

    internal static void Hash(string value, string name)
    {
        if (value is null || value.Length != 64 ||
            value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase SHA-256 value is required.", name);
    }
}
