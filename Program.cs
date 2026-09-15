using System;
using System.IO;
using System.Threading.Tasks;
using AvalonMCP;

class Program
{
    static async Task Main(string[] args)
    {
        using var designerManager = new DesignerManager();
        using var treeDumper = new HeadlessTreeDumper();
        
        try
        {
            if (args.Length >= 2)
            {
                var executablePath = args[0];
                var hostAppPath = args[1];
                if (!File.Exists(executablePath) || !File.Exists(hostAppPath))
                    throw new FileNotFoundException("Designer executable or HostApp was not found.");
                _ = designerManager.StartAsync(executablePath, hostAppPath);
            }

            var server = new McpServer(designerManager, treeDumper);
            await server.StartAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
        }
    }
}
