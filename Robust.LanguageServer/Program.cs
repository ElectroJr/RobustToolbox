using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using Robust.LanguageServer.Parsing;
using Robust.LanguageServer.Provider;
using Robust.Shared.IoC;
using Robust.Server;
using ELLanguageServer = EmmyLua.LanguageServer.Framework.Server.LanguageServer;

namespace Robust.LanguageServer;

internal static class Program
{
    static async Task Main(string[] args)
    {
        if (!CommandLineArgs.TryParse(args, out var cliArgs))
            return;

        var deps = IoCManager.InitThread();
        ServerIoC.RegisterIoC(deps);
        deps.Register<DocumentCache>();
        deps.Register<Loader>();
        deps.Register<DocsManager>();
        deps.Register<Parser>();
        deps.Register<DiagnosticProvider>();
        deps.Register<LanguageServerContext>();
        deps.BuildGraph();

        deps.Resolve<Loader>().Init();

        // ClientIoC.RegisterIoC(GameController.DisplayMode.Headless, deps);


        var ctx = deps.Resolve<LanguageServerContext>();
        await ctx.Initialize(cliArgs);
        await ctx.Run();
    }
}
