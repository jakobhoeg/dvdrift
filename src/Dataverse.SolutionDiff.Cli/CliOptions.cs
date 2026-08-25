namespace Dataverse.SolutionDiff.Cli;

/// <summary>
/// Hand-rolled argument parser: verb-less "two snapshots in, report out" shape with
/// zero dependencies, keeping the CLI deterministic and the dependency graph minimal.
/// </summary>
public sealed class CliOptions
{
    public const string Usage = """
        dvdrift - deterministic diff and accountability reports for Dataverse solutions

        USAGE:
          dvdrift <baseline> <current> [options]

        ARGUMENTS:
          <baseline>   Baseline snapshot: solution .zip, raw zip-extract folder, or pac-unpacked folder
          <current>    Current snapshot (same formats)

        OPTIONS:
          --format <md|json>   Output format (default: md)
          --summary-only       Emit just the counts line, without the per-component tables
          --out <file>         Write report to file instead of stdout
          --config <file>      Config file (default: dvdrift.json in working directory if present)
          --offline            Skip the Dataverse attribution/state join
          --fail-on-change     Exit 1 if any change is detected
          --help, -h           Show this help
          --version            Show the installed version

        ATTRIBUTION (requires --url and either a token or client credentials):
          --url <url>                Environment URL (env: DATAVERSE_URL)
          --tenant-id <guid>         Tenant id (env: DATAVERSE_TENANT_ID)
          --client-id <guid>         App registration client id (env: DATAVERSE_CLIENT_ID)
          --client-secret <secret>   Client secret (env: DATAVERSE_CLIENT_SECRET)
          --access-token <token>     Pre-acquired access token (env: DATAVERSE_ACCESS_TOKEN)

          Without --offline or --url, the diff succeeds without attribution/state
          and writes a warning to stderr.

        EXIT CODES:
          0  no gating condition hit
          1  changes detected and --fail-on-change set
          3  usage or runtime error
        """;

    public string? Baseline { get; private set; }

    public string? Current { get; private set; }

    public string Format { get; private set; } = "md";

    public string? OutFile { get; private set; }

    public string? ConfigPath { get; private set; }

    public bool SummaryOnly { get; private set; }

    public bool FailOnChange { get; private set; }

    public bool Offline { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool ShowVersion { get; private set; }

    public string? Url { get; private set; }

    public string? TenantId { get; private set; }

    public string? ClientId { get; private set; }

    public string? ClientSecret { get; private set; }

    public string? AccessToken { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--format":
                    options.Format = Value(args, ref i, arg);
                    break;
                case "--out":
                    options.OutFile = Value(args, ref i, arg);
                    break;
                case "--config":
                    options.ConfigPath = Value(args, ref i, arg);
                    break;
                case "--url":
                    options.Url = Value(args, ref i, arg);
                    break;
                case "--tenant-id":
                    options.TenantId = Value(args, ref i, arg);
                    break;
                case "--client-id":
                    options.ClientId = Value(args, ref i, arg);
                    break;
                case "--client-secret":
                    options.ClientSecret = Value(args, ref i, arg);
                    break;
                case "--access-token":
                    options.AccessToken = Value(args, ref i, arg);
                    break;
                case "--summary-only":
                    options.SummaryOnly = true;
                    break;
                case "--fail-on-change":
                    options.FailOnChange = true;
                    break;
                case "--offline":
                    options.Offline = true;
                    break;
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                case "--version":
                    options.ShowVersion = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new DiffException($"Unknown option '{arg}'. Run 'dvdrift --help'.");
                    }

                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count > 2)
        {
            throw new DiffException($"Unexpected argument '{positional[2]}'. Exactly two snapshot paths are expected.");
        }

        options.Baseline = positional.Count > 0 ? positional[0] : null;
        options.Current = positional.Count > 1 ? positional[1] : null;

        if (!string.Equals(options.Format, "md", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase))
        {
            throw new DiffException($"Unknown format '{options.Format}'. Expected 'md' or 'json'.");
        }

        // Environment variable fallbacks for CI.
        options.Url ??= Environment.GetEnvironmentVariable("DATAVERSE_URL");
        options.TenantId ??= Environment.GetEnvironmentVariable("DATAVERSE_TENANT_ID");
        options.ClientId ??= Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_ID");
        options.ClientSecret ??= Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_SECRET");
        options.AccessToken ??= Environment.GetEnvironmentVariable("DATAVERSE_ACCESS_TOKEN");

        return options;
    }

    private static string Value(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new DiffException($"Option '{flag}' requires a value.");
        }

        return args[++i];
    }
}
