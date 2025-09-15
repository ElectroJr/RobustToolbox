using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Message.DocumentSymbol;
using EmmyLua.LanguageServer.Framework.Server.Handler;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Robust.LanguageServer.Handler;

public sealed class DocumentSymbolHandler : DocumentSymbolHandlerBase, IRobustHandler
{
    [Dependency] private readonly DocumentCache _cache = null!;

    private ISawmill _logger = default!;

    public void Init(ISawmill logger)
    {
        _logger = logger;
    }

    protected override Task<DocumentSymbolResponse> Handle(DocumentSymbolParams request, CancellationToken token)
    {
        _logger.Debug("DocumentSymbol");
        var symbols = _cache.GetSymbols(request.TextDocument.Uri);

        // TODO fix
        if (symbols == null)
            throw new Exception("Symbol not found");

        List<DocumentSymbol> documentSymbols = new();

        foreach (var symbol in symbols)
        {
            documentSymbols.Add(new()
            {
                Name = symbol.Name,
                Kind = SymbolKind.Class,
                Range = new()
                {
                    Start = Helpers.ToLsp(symbol.NodeStart),
                    End = Helpers.ToLsp(symbol.NodeEnd)
                },
                SelectionRange = new()
                {
                    Start = Helpers.ToLsp(symbol.NodeStart),
                    End = Helpers.ToLsp(symbol.NodeEnd)
                }
            });
        }

        return Task.FromResult(new DocumentSymbolResponse(documentSymbols));
    }

    public override void RegisterCapability(
        ServerCapabilities serverCapabilities,
        ClientCapabilities clientCapabilities)
    {
        serverCapabilities.DocumentSymbolProvider = true;
    }
}
