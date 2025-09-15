using EmmyLua.LanguageServer.Framework.Protocol.Message.Client.PublishDiagnostics;
using EmmyLua.LanguageServer.Framework.Protocol.Model.Diagnostic;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Robust.LanguageServer.Provider;

public sealed class DiagnosticProvider : IPostInjectInit
{
    [Dependency] private readonly DocumentCache _cache = null!;
    [Dependency] private readonly LanguageServerContext _server = null!;
    [Dependency] private readonly ILogManager _log = null!;

    private ISawmill _logger = null!;
    public void PostInject()
    {
        _cache.DocumentChanged += OnDocumentChanged;
        _logger = _log.GetSawmill("DiagnosticProvider");
    }

    private void OnDocumentChanged(Uri uri, int documentVersion)
    {
        _logger.Info($"Document changed! Uri: {uri}");

        List<Diagnostic> diagnosticList = new();

        if (_cache.GetErrors(uri) is {} errors)
        {
            foreach (var errorNode in errors)
            {
                _logger.Error($"Error in file: {uri}");

                _logger.Error(
                    $"* {errorNode.Node} - {errorNode.ErrorReason} - {errorNode.AlwaysRelevant} - {errorNode.Node.Start} -> {errorNode.Node.End}");


                diagnosticList.Add(new Diagnostic()
                {
                    Message = errorNode.ErrorReason,
                    Range = Helpers.LspRangeForNode(errorNode.Node),
                    Severity = DiagnosticSeverity.Error,
                    Source = "SS14 LSP",
                    Code = "12313",
                });
            }
        }

        _server.LanguageServer.Client.PublishDiagnostics(new PublishDiagnosticsParams()
        {
            Uri = uri,
            Diagnostics = diagnosticList,
            Version = documentVersion,
        });
    }
}
