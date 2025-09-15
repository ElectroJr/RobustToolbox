using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Initialize;
using Robust.LanguageServer.Handler;
using Robust.LanguageServer.Notifications;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Reflection;

namespace Robust.LanguageServer;

using ELLanguageServer = EmmyLua.LanguageServer.Framework.Server.LanguageServer;

public sealed class LanguageServerContext
{
    [Dependency] private readonly ILogManager _logMan = null!;
    [Dependency] private readonly IReflectionManager _reflection = null!;
    [Dependency] private readonly IDynamicTypeFactoryInternal _factory = null!;

    private ISawmill _logger = default!;
    public ELLanguageServer LanguageServer { get; private set; } = default!;

    public Uri? RootDirectory { get; private set; }

    private bool _initialized;

    internal async Task Initialize(CommandLineArgs cliArgs)
    {
        if (_initialized)
            return;

        _initialized = true;
        _logger = _logMan.GetSawmill("LanguageServer");

        LanguageServer = await CreateLanguageServer(cliArgs);
        LanguageServer.OnInitialize(OnInitialize);
        LanguageServer.OnInitialized(OnInitialized);
        LanguageServer.AddJsonSerializeContext(JsonGenerateContext.Default);

        foreach (var handler in _reflection.GetAllChildren<IRobustHandler>())
        {
            var instance = (IRobustHandler)_factory.CreateInstanceUnchecked(handler, oneOff: true);
            instance.Init(_logMan.GetSawmill(handler.Name));
            LanguageServer.AddHandler(instance);
        }
    }

    private async Task<ELLanguageServer> CreateLanguageServer(CommandLineArgs args)
    {
        if (args.Mode == CommandLineArgs.Transport.Tcp)
            return await CreateTcpLanguageServer(args.Port);

        if (args.Mode == CommandLineArgs.Transport.Pipe)
        {
            if (args.CommunicationPipe is not { } pipe)
                throw new Exception("Missing pipe");

            return await CreateNamedPipeLanguageServer(pipe);
        }

        return await CreateStdOutLanguageServer();
    }

    private async Task<ELLanguageServer> CreateNamedPipeLanguageServer(string pipe)
    {
        _logger.Info("Communicating using pipe: {0}", pipe);

        var stream = new NamedPipeClientStream(pipe);
        await stream.ConnectAsync();
        _logger.Info("Pipe connected");

        return ELLanguageServer.From(stream, stream);
    }

    private Task<ELLanguageServer> CreateStdOutLanguageServer()
    {
        _logger.Info("Communicating using standard in/out");

        Stream inputStream = Console.OpenStandardInput();
        Stream outputStream = Console.OpenStandardOutput();

        return Task.FromResult(ELLanguageServer.From(inputStream, outputStream));
    }

    private async Task<ELLanguageServer> CreateTcpLanguageServer(int port)
    {
        var tcpServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var ipAddress = new IPAddress(new byte[] {127, 0, 0, 1});
        EndPoint endPoint = new IPEndPoint(ipAddress, port);

        tcpServer.Bind(endPoint);

        _logger.Info($"Listening on port {port}.");
        tcpServer.Listen(1);

        var languageClientSocket = await tcpServer.AcceptAsync();
        _logger.Info($"Port Connected.");

        var networkStream = new NetworkStream(languageClientSocket);
        var input = networkStream;
        var output = networkStream;

        return ELLanguageServer.From(input, output);
    }

    private Task OnInitialize(InitializeParams c, ServerInfo s)
    {
        if (c.RootUri is { } rootUri)
            RootDirectory = rootUri.Uri;

        s.Name = "SS14 LSP";
        s.Version = "0.0.1";
        _logger.Info("server initializing");
        return Task.CompletedTask;
    }

    private async Task OnInitialized(InitializedParams c)
    {

        _logger.Info("server initialized");
        /*try
        {

            // Here we should be trying to load data based on the client.rootUri
            _logger.Error("Starting loader…");

            await ShowProgress("Loading Prototypes…");

            // IMO this should be moved into Program.cs
            // I.e. initialize and then start the language server
            // instead of waiting for the TCP connection to be made.
            _deps.Resolve<Loader>().Init();

            await HideProgress();

            _logger.Error("Loaded");
        }
        catch (Exception e)
        {
            _logger.Error($"Error while starting: {e}");
            await _languageServer.Client.ShowMessage(new()
                {Message = $"Error during startup: {e.Message}", Type = MessageType.Error,});
            Environment.Exit(1);
        }*/
    }

    public Task Run()
    {
        _logger.Info("Running server");
        return LanguageServer.Run();
    }

    private async Task ShowProgress(string text)
    {
        await LanguageServer.SendNotification(new("rt/showProgress",
            JsonSerializer.SerializeToDocument(
                new ProgressInfo()
                {
                    Text = text,
                },
                LanguageServer.JsonSerializerOptions)
        ));
    }

    private async Task HideProgress()
    {
        await LanguageServer.SendNotification(new("rt/hideProgress", null));
    }
}
