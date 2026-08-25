using System.Text;
using Dataverse.SolutionDiff.Attribution;
using Dataverse.SolutionDiff.Configuration;
using Dataverse.SolutionDiff.Reporting;

namespace Dataverse.SolutionDiff.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.Out.Write(CliOptions.Usage + "\n");
                return 0;
            }

            if (options.ShowVersion)
            {
                Console.Out.Write((typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown") + "\n");
                return 0;
            }

            if (options.Baseline is null || options.Current is null)
            {
                Console.Error.WriteLine("dvdrift: missing <baseline> and <current> arguments. Run 'dvdrift --help'.");
                return 3;
            }

            var config = DiffConfig.Load(options.ConfigPath);

            IAttributionSource? attributionSource = null;
            if (!options.Offline && options.Url is not null)
            {
                attributionSource = await DataverseAttributionSource.CreateAsync(options).ConfigureAwait(false);
            }
            else if (!options.Offline)
            {
                Console.Error.WriteLine("dvdrift: no --url (or DATAVERSE_URL) given; running the diff without attribution/state. Use --offline to silence this note.");
            }

            try
            {
                var report = await DiffEngine.RunAsync(options.Baseline, options.Current, config, attributionSource).ConfigureAwait(false);

                var json = string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase);
                var output = (json, options.SummaryOnly) switch
                {
                    (true, true) => JsonReporter.RenderSummary(report),
                    (true, false) => JsonReporter.Render(report),
                    (false, true) => MarkdownReporter.RenderSummary(report),
                    (false, false) => MarkdownReporter.Render(report),
                };

                if (options.OutFile is not null)
                {
                    await File.WriteAllTextAsync(options.OutFile, output, new UTF8Encoding(false)).ConfigureAwait(false);
                }
                else
                {
                    Console.Out.Write(output);
                }

                if (options.FailOnChange && report.Added + report.Modified + report.Deleted > 0)
                {
                    return 1;
                }

                return 0;
            }
            finally
            {
                (attributionSource as IDisposable)?.Dispose();
            }
        }
        catch (DiffException ex)
        {
            Console.Error.WriteLine("dvdrift: " + ex.Message);
            return 3;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine("dvdrift: " + ex.Message);
            return 3;
        }
    }
}
