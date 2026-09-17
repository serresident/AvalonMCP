using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AvalonMCP
{
    public class McpServer
    {
        private readonly DesignerManager _designerManager;
        private readonly HeadlessTreeDumper _treeDumper;
        private readonly ProjectInspector _projectInspector;

        public McpServer(DesignerManager designerManager, HeadlessTreeDumper treeDumper, ProjectInspector? projectInspector = null)
        {
            _designerManager = designerManager;
            _treeDumper = treeDumper;
            _projectInspector = projectInspector ?? new ProjectInspector();
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
            JsonNode? document;
            try { document = JsonNode.Parse(json); }
            catch (JsonException) { SendError(null, -32700, "Parse error"); return; }
            if (document is not JsonObject request || request["jsonrpc"]?.ToString() != "2.0" || request["method"] is not JsonValue methodValue || !methodValue.TryGetValue<string>(out _))
            { SendError(null, -32600, "Invalid Request"); return; }

            // Notifications must never receive a response or execute tool calls.
            if (!request.ContainsKey("id")) return;

            var method = document["method"]?.ToString();
            var idNode = document["id"]?.DeepClone();

            if (method == "ping") { SendResponse(new { jsonrpc = "2.0", id = idNode, result = new { } }); return; }
            if (method == "initialize")
            {
                var response = new
                {
                    jsonrpc = "2.0",
                    id = idNode,
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
                    id = idNode,
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
                                        height = new { type = "number", description = "Viewport height (default 768)." },
                                        theme = new { type = "string", description = "Optional theme variant ('light' or 'dark')." },
                                        assemblyPath = new { type = "string", description = "Optional path to compiled project .dll for resolving custom controls, styles, and x:Class." }
                                    },
                                    required = new[] { "xaml" }
                                }
                            },
                            new
                            {
                                name = "lint_ui",
                                description = "Performs instant in-memory layout diagnostics on AXAML without rendering screenshots. Detects collapsed 0x0 elements, text clipping, grid overlap collisions, and alignment bugs.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        xaml = new { type = "string", description = "AXAML content to lint." },
                                        width = new { type = "number", description = "Viewport width (default 1024)." },
                                        height = new { type = "number", description = "Viewport height (default 768)." },
                                        theme = new { type = "string", description = "Optional theme variant ('light' or 'dark')." },
                                        assemblyPath = new { type = "string", description = "Optional path to compiled project .dll." }
                                    },
                                    required = new[] { "xaml" }
                                }
                            },
                            new
                            {
                                name = "discover_project",
                                description = "Discovers Avalonia projects, solutions and AXAML files under a directory.",
                                inputSchema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } }
                            },
                            new
                            {
                                name = "render_ui_snapshot",
                                description = "Renders standalone or project-backed AXAML and returns an MCP PNG image. Optionally draws debug bounding boxes on detected layout issues.",
                                inputSchema = new { type = "object", properties = new { xaml = new { type = "string" }, width = new { type = "number" }, height = new { type = "number" }, theme = new { type = "string", description = "Optional theme variant ('light' or 'dark')." }, assemblyPath = new { type = "string", description = "Optional path to compiled project .dll." }, annotateErrors = new { type = "boolean", description = "Draw colored bounding boxes on detected layout issues (default true)." } }, required = new[] { "xaml" } }
                            },
                            new
                            {
                                name = "inspect_ui",
                                description = "Returns an annotated PNG image and the measured visual tree with layout diagnostics in one call.",
                                inputSchema = new { type = "object", properties = new { xaml = new { type = "string" }, width = new { type = "number" }, height = new { type = "number" }, theme = new { type = "string", description = "Optional theme variant ('light' or 'dark')." }, assemblyPath = new { type = "string", description = "Optional path to compiled project .dll." }, annotateErrors = new { type = "boolean", description = "Draw colored bounding boxes on detected layout issues (default true)." } }, required = new[] { "xaml" } }
                            },
                            new
                            {
                                name = "build_project",
                                description = "Builds a .sln or .csproj and returns structured output with timeout handling.",
                                inputSchema = new { type = "object", properties = new { path = new { type = "string" }, timeoutSeconds = new { type = "integer" } }, required = new[] { "path" } }
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
                if (args is not JsonObject)
                { SendError(idNode, -32602, "Tool arguments must be an object."); return; }

                if (toolName == "discover_project" && args != null)
                {
                    try { SendToolResult(idNode, _projectInspector.Discover(args["path"]?.ToString() ?? throw new ArgumentException("path is required"))); }
                    catch (Exception ex) { SendToolResult(idNode, $"Project discovery failed: {ex.Message}", true); }
                    return;
                }

                if (toolName == "build_project" && args != null)
                {
                    try
                    {
                        var path = args["path"]?.ToString() ?? throw new ArgumentException("path is required");
                        var timeout = args["timeoutSeconds"]?.GetValue<int>() ?? 120;
                        SendToolResult(idNode, await _projectInspector.BuildAsync(path, timeout, CancellationToken.None));
                    }
                    catch (Exception ex) { SendToolResult(idNode, $"Project build failed: {ex.Message}", true); }
                    return;
                }

                if (toolName == "lint_ui" && args != null)
                {
                    try
                    {
                        var xaml = args["xaml"]?.ToString() ?? throw new ArgumentException("xaml is required");
                        var width = args["width"]?.GetValue<double>() ?? 1024;
                        var height = args["height"]?.GetValue<double>() ?? 768;
                        var theme = args["theme"]?.ToString();
                        var assemblyPath = args["assemblyPath"]?.ToString();

                        var report = await _treeDumper.LintAsync(xaml, width, height, theme, assemblyPath);
                        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions 
                        { 
                            WriteIndented = true,
                            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals 
                        });
                        SendToolResult(idNode, reportJson, isError: !report.Passed);
                    }
                    catch (Exception ex)
                    {
                        SendToolResult(idNode, $"AXAML linting failed: {ex.Message}", true);
                    }
                    return;
                }

                if (toolName == "get_ui_tree" && args != null)
                {
                    try
                    {
                        var xaml = args["xaml"]?.ToString() ?? throw new ArgumentException("xaml is required");
                        var width = args["width"]?.GetValue<double>() ?? 1024;
                        var height = args["height"]?.GetValue<double>() ?? 768;
                        var theme = args["theme"]?.ToString();
                        var assemblyPath = args["assemblyPath"]?.ToString();
                        SendToolResult(idNode, await _treeDumper.DumpAsync(xaml, width, height, theme, assemblyPath));
                    }
                    catch (Exception ex)
                    {
                        SendToolResult(idNode, $"AXAML inspection failed: {ex}", true);
                    }
                    return;
                }

                if ((toolName == "render_ui_snapshot" || toolName == "inspect_ui") && args != null)
                {
                    try
                    {
                        var xaml = args["xaml"]?.ToString() ?? throw new ArgumentException("xaml is required");
                        var width = args["width"]?.GetValue<double>() ?? 1024;
                        var height = args["height"]?.GetValue<double>() ?? 768;
                        var theme = args["theme"]?.ToString();
                        var assemblyPath = args["assemblyPath"]?.ToString();
                        var annotateErrors = args["annotateErrors"]?.GetValue<bool>() ?? true;

                        var snapshot = await _treeDumper.InspectAsync(xaml, width, height, true, theme, assemblyPath, annotateErrors);
                        var content = new List<object>();
                        if (toolName == "inspect_ui")
                        {
                            var combined = new
                            {
                                tree = JsonNode.Parse(snapshot.Tree),
                                diagnostics = snapshot.Report
                            };
                            content.Add(new { type = "text", text = JsonSerializer.Serialize(combined) });
                        }
                        content.Add(new { type = "image", data = snapshot.PngBase64, mimeType = "image/png" });
                        SendResponse(new { jsonrpc = "2.0", id = idNode, result = new { content, isError = false } });
                    }
                    catch (Exception ex) { SendToolResult(idNode, $"AXAML rendering failed: {ex.Message}", true); }
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
                            id = idNode,
                            result = new
                            {
                                content = new[]
                                {
                                    new { type = "text", text = resultString }
                                }
                            }
                        };
                        SendResponse(response);
                        return;
                    }
                    SendError(idNode, -32602, "assemblyPath and xaml are required.");
                    return;
                }
                SendError(idNode, -32602, $"Unknown tool: {toolName}");
            }
            else { SendError(idNode, -32601, $"Unknown method: {method}"); }
        }

        private void SendError(JsonNode? id, int code, string message) =>
            SendResponse(new { jsonrpc = "2.0", id, error = new { code, message } });

        private void SendResponse(object response)
        {
            var json = JsonSerializer.Serialize(response);
            Console.WriteLine(json);
        }

        private void SendToolResult(JsonNode? id, string text, bool isError = false)
        {
            SendResponse(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text } }, isError } });
        }
    }
}
