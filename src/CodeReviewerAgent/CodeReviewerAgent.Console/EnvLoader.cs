/// <summary>
/// Minimal loader for .env files that sets each entry as a process environment variable.
/// Supports blank lines, '#' comments, and optionally quoted values.
/// </summary>
public static class EnvLoader
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
            return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"');
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
