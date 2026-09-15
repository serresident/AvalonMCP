using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace AvalonMCP;

public sealed class ProjectInspector
{
    private const int MaxFiles = 500;
    private const int MaxOutputCharacters = 100_000;

    public string Discover(string path)
    {
        var root = ResolveDirectory(path);
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(IncludePath).Take(MaxFiles)
            .Select(ReadProject).ToArray();
        var solutions = Directory.EnumerateFiles(root, "*.sln", SearchOption.AllDirectories)
            .Where(IncludePath).Take(MaxFiles).Select(Path.GetFullPath).ToArray();
        var axaml = Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(IncludePath).Take(MaxFiles).Select(Path.GetFullPath).ToArray();

        return JsonSerializer.Serialize(new
        {
            root,
            solutions,
            projects,
            axamlFiles = axaml,
            truncated = projects.Length >= MaxFiles || solutions.Length >= MaxFiles || axaml.Length >= MaxFiles
        }, JsonOptions);
    }

    public async Task<string> BuildAsync(string path, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var target = ResolveBuildTarget(path);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 600);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{target}\" --nologo --verbosity minimal",
                WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var stopwatch = Stopwatch.StartNew();
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await SafeReadAsync(stdoutTask);
        var stderr = await SafeReadAsync(stderrTask);
        return JsonSerializer.Serialize(new
        {
            target,
            success = !timedOut && process.ExitCode == 0,
            timedOut,
            exitCode = timedOut ? (int?)null : process.ExitCode,
            durationMs = stopwatch.ElapsedMilliseconds,
            stdout = Limit(stdout),
            stderr = Limit(stderr)
        }, JsonOptions);
    }

    private static object ReadProject(string file)
    {
        try
        {
            var document = XDocument.Load(file);
            var properties = document.Descendants("PropertyGroup").Elements()
                .GroupBy(x => x.Name.LocalName).ToDictionary(g => g.Key, g => g.Last().Value);
            var packages = document.Descendants("PackageReference").Select(x => new
            {
                name = x.Attribute("Include")?.Value,
                version = x.Attribute("Version")?.Value ?? x.Element("Version")?.Value
            }).ToArray();
            return new
            {
                path = Path.GetFullPath(file),
                targetFramework = properties.GetValueOrDefault("TargetFramework"),
                outputType = properties.GetValueOrDefault("OutputType"),
                isAvalonia = packages.Any(x => x.name?.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) == true),
                packages
            };
        }
        catch (Exception ex)
        {
            return new { path = Path.GetFullPath(file), error = ex.Message };
        }
    }

    private static string ResolveDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required");
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static string ResolveBuildTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required");
        var full = Path.GetFullPath(path);
        if (File.Exists(full) && (full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))) return full;
        if (!Directory.Exists(full)) throw new FileNotFoundException("Build target was not found.", full);
        var solutions = Directory.GetFiles(full, "*.sln", SearchOption.TopDirectoryOnly);
        if (solutions.Length == 1) return solutions[0];
        var projects = Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projects.Length == 1) return projects[0];
        throw new InvalidOperationException("Directory must contain exactly one top-level .sln or .csproj, or pass its path explicitly.");
    }

    private static bool IncludePath(string path) => !path.Split(Path.DirectorySeparatorChar)
        .Any(x => x.Equals("bin", StringComparison.OrdinalIgnoreCase) || x.Equals("obj", StringComparison.OrdinalIgnoreCase) || x.Equals(".git", StringComparison.OrdinalIgnoreCase));
    private static string Limit(string text) => text.Length <= MaxOutputCharacters ? text : text[..MaxOutputCharacters] + "\n[output truncated]";
    private static async Task<string> SafeReadAsync(Task<string> task) { try { return await task; } catch (OperationCanceledException) { return string.Empty; } }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
