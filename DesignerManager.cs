using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Remote.Protocol;
using Avalonia.Remote.Protocol.Designer;
using Avalonia.Remote.Protocol.Viewport;
using Task = System.Threading.Tasks.Task;

namespace AvalonMCP
{
    public class DesignerManager : IDisposable
    {
        private string _assemblyPath;
        private string _executablePath;
        private Process _process;
        private IAvaloniaRemoteTransportConnection _connection;
        private IDisposable _listener;

        public event Action<byte[], int, int, int> OnFrameReceived;
        public event Action<string> OnError;

        public async Task StartAsync(string executablePath, string hostAppPath)
        {
            _executablePath = executablePath;
            
            var port = FreeTcpPort();
            var tcs = new TaskCompletionSource<bool>();

            _listener = new BsonTcpTransport().Listen(
                IPAddress.Loopback,
                port,
                async t =>
                {
                    try
                    {
                        await ConnectionInitializedAsync(t);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error initializing connection: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });

            var executableDir = Path.GetDirectoryName(_executablePath);
            var targetName = Path.GetFileNameWithoutExtension(_executablePath);
            var runtimeConfigPath = Path.Combine(executableDir, targetName + ".runtimeconfig.json");
            var depsPath = Path.Combine(executableDir, targetName + ".deps.json");

            string args = $@"exec --runtimeconfig ""{runtimeConfigPath}"" --depsfile ""{depsPath}"" ""{hostAppPath}"" --transport tcp-bson://127.0.0.1:{port}/ ""{_executablePath}""";

            var processInfo = new ProcessStartInfo
            {
                Arguments = args,
                CreateNoWindow = true,
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            Console.WriteLine($"Starting previewer process: dotnet {args}");

            _process = Process.Start(processInfo);
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine($"[Host] {e.Data}"); };
            _process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine($"[Host ERROR] {e.Data}"); };
            _process.Exited += (s, e) => Console.WriteLine("Previewer process exited.");
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();

            await tcs.Task;
        }

        private async Task ConnectionInitializedAsync(IAvaloniaRemoteTransportConnection connection)
        {
            _connection = connection;
            _connection.OnException += (c, ex) => Console.WriteLine($"Connection error: {ex.Message}");
            _connection.OnMessage += (c, msg) => OnMessageAsync(msg).GetAwaiter().GetResult();

            await SendAsync(new ClientSupportedPixelFormatsMessage
            {
                Formats = new[]
                {
                    Avalonia.Remote.Protocol.Viewport.PixelFormat.Rgba8888,
                }
            });

            await SendAsync(new ClientRenderInfoMessage
            {
                DpiX = 96,
                DpiY = 96,
            });
        }

        public async Task UpdateXamlAsync(string assemblyPath, string xaml)
        {
            _assemblyPath = assemblyPath;
            if (_connection != null)
            {
                await SendAsync(new UpdateXamlMessage
                {
                    AssemblyPath = _assemblyPath,
                    Xaml = xaml,
                });
            }
        }

        private async Task SendAsync(object message)
        {
            if (_connection is IAvaloniaRemoteTransportConnection connection)
                await connection.Send(message);
        }

        private async Task OnMessageAsync(object message)
        {
            switch (message)
            {
                case FrameMessage frame:
                    OnFrameReceived?.Invoke(frame.Data, frame.Width, frame.Height, frame.Stride);
                    await SendAsync(new FrameReceivedMessage { SequenceId = frame.SequenceId });
                    break;
                case UpdateXamlResultMessage update:
                    if (update.Exception != null)
                    {
                        OnError?.Invoke(update.Exception.Message);
                    }
                    else if (!string.IsNullOrWhiteSpace(update.Error))
                    {
                        OnError?.Invoke(update.Error);
                    }
                    break;
            }
        }

        private static int FreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _listener?.Dispose();
            if (_connection != null)
            {
                _connection.Dispose();
            }
            if (_process?.HasExited == false)
            {
                _process.Kill();
            }
        }
    }
}
