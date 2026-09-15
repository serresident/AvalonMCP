using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AvalonMCP
{
    public class McpServer
    {
        private readonly DesignerManager _designerManager;
        private readonly HeadlessTreeDumper _treeDumper;

        public McpServer(DesignerManager designerManager, HeadlessTreeDumper treeDumper)
        {
            _designerManager = designerManager;
            _treeDumper = treeDumper;
        }

        public async Task StartAsync()
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            
            // Log to stderr so we don't mess up stdout which is for JSON-RPC
            Console.Error.WriteLine("AvalonMCP server started.");

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                try
                {
                    await HandleMessageAsync(line);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error handling message: {ex.Message}");
                }
            }
        }

        private async Task HandleMessageAsync(string json)
        {
            var document = JsonNode.Parse(json);
            if (document == null) return;

            var method = document["method"]?.ToString();
            var id = document["id"]?.ToString();

            if (method == "initialize")
            {
                var response = new
                {
                    jsonrpc = "2.0",
                    id = id,
                    result = new
                    {
                        protocolVersion = "2024-11-05",
                        serverInfo = new { name = "AvalonMCP", version = "1.0.0" },
                        capabilities = new
                        {
                            tools = new { }
                        }
                    }
                };
                SendResponse(response);
            }
            else if (method == "tools/list")
            {
                var response = new
                {
                    jsonrpc = "2.0",
                    id = id,
                    result = new
                    {
                        tools = new object[]
                        {
                            new
                            {
                                name = "get_ui_tree",
                                description = "Loads AXAML in Avalonia Headless and returns the visual tree with measured bounds.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        xaml = new { type = "string", description = "AXAML content to inspect." },
                                        width = new { type = "number", description = "Viewport width (default 1024)." },
                                        height = new { type = "number", description = "Viewport height (default 768)." }
                                    },
                                    required = new[] { "xaml" }
                                }
                            },
                            new
                            {
                                name = "update_and_render_xaml",
                                description = "Sends XAML to the designer and returns a base64 encoded screenshot.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        assemblyPath = new { type = "string", description = "Absolute path to the compiled .dll of the target project." },
                                        xaml = new { type = "string", description = "The XAML content to render." }
                                    },
                                    required = new[] { "assemblyPath", "xaml" }
                                }
                            }
                        }
                    }
                };
                SendResponse(response);
            }
            else if (method == "tools/call")
            {
                var toolName = document["params"]?["name"]?.ToString();
                var args = document["params"]?["arguments"];

                if (toolName == "get_ui_tree" && args != null)
                {
                    try
                    {
                        var xaml = args["xaml"]?.ToString() ?? throw new ArgumentException("xaml is required");
                        var width = args["width"]?.GetValue<double>() ?? 1024;
                        var height = args["height"]?.GetValue<double>() ?? 768;
                        SendToolResult(id, _treeDumper.Dump(xaml, width, height));
                    }
                    catch (Exception ex)
                    {
                        SendToolResult(id, $"AXAML inspection failed: {ex}", true);
                    }
                    return;
                }

                if (toolName == "update_and_render_xaml" && args != null)
                {
                    var assemblyPath = args["assemblyPath"]?.ToString();
                    var xaml = args["xaml"]?.ToString();

                    if (assemblyPath != null && xaml != null)
                    {
                        var tcs = new TaskCompletionSource<string>();
                        
                        Action<byte[], int, int, int> frameHandler = (data, w, h, s) =>
                        {
                            var b64 = Convert.ToBase64String(data);
                            tcs.TrySetResult($"[Frame rendered: {w}x{h}]\n{b64.Substring(0, Math.Min(b64.Length, 100))}...");
                        };
                        Action<string> errorHandler = (err) =>
                        {
                            tcs.TrySetResult($"[Error]: {err}");
                        };

                        _designerManager.OnFrameReceived += frameHandler;
                        _designerManager.OnError += errorHandler;

                        await _designerManager.UpdateXamlAsync(assemblyPath, xaml);

                        // Wait for a result or timeout
                        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));
                        
                        _designerManager.OnFrameReceived -= frameHandler;
                        _designerManager.OnError -= errorHandler;

                        string resultString;
                        if (completedTask == tcs.Task)
                        {
                            resultString = await tcs.Task;
                        }
                        else
                        {
                            resultString = "[Timeout waiting for render]";
                        }

                        var response = new
                        {
                            jsonrpc = "2.0",
                            id = id,
                            result = new
                            {
                                content = new[]
                                {
                                    new { type = "text", text = resultString }
                                }
                            }
                        };
                        SendResponse(response);
                    }
                }
            }
        }

        private void SendResponse(object response)
        {
            var json = JsonSerializer.Serialize(response);
            Console.WriteLine(json);
        }

        private void SendToolResult(string? id, string text, bool isError = false)
        {
            SendResponse(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text } }, isError } });
        }
    }
}
