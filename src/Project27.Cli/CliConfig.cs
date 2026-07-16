using Microsoft.Extensions.Configuration;

namespace Project27.Cli;

internal static class CliConfig
{
    private static IConfiguration? _configuration;

    internal static void Initialize(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    internal static int CliLoopbackPort =>
        _configuration?.GetValue<int>("CliLoopbackPort") ?? 64703;

    internal static string? CliClientId =>
        _configuration?["CliClientId"];
}
