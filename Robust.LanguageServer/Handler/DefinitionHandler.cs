using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Definition;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Server.Handler;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Manager.Definition;
using Robust.Shared.Serialization.Markdown.Value;

namespace Robust.LanguageServer.Handler;

public sealed class DefinitionHandler : DefinitionHandlerBase, IRobustHandler
{
    [Dependency] private readonly DocumentCache _cache = null!;
    [Dependency] private readonly LanguageServerContext _context = null!;

    private ISawmill _logger = default!;

    public void Init(ISawmill logger)
    {
        _logger = logger;
    }

    protected override Task<DefinitionResponse?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        _logger.Debug($"DefinitionHandler.Handle - {_context.RootDirectory?.LocalPath}");

        // NOTE: Copy-paste from HoverHandler here
        // TODO handle prototype-type fields

        // TODO how best to resolve types into source code?
        //
        // Surely theres some sane way of doing that.
        // Maybe exporting the path information alongside debug builds? Maybe alongside xml docs?
        // Or is there some way to store/retrieve source file info in assembly metadata/pdb files?
        //
        // Currently, the best I can think of is to give all classes/fields an attribute that has an argument with a `[CallerFilePath]` attribute.
        // This could easily be used to cover explicit DataFields, DataDefinition, Prototypes, Components
        // but it can't easily handle anything that is implicitly given one of those attributes
        // I.e., anything with:
        // - ImplicitDataDefinitionForInheritorsAttribute
        // - MeansDataDefinitionAttribute
        // - MeansDataRecordAttribute
        // - data fields implicitly defined within a dataRecord (though going to the declaring type instead of the field definition is probably fine?)

        // TODO handle:
        // - go-to C# prototype (i.e., handle the LHS of the " type: " part.
        // - go-to yaml prototype (i.e., go to yaml file that defines parent prototype)
        // - go-to fluent / localization string. Might require giving loc strings a specific type, not just "string".
        // - go-to RSI, audio, or other resources?

        var fields = _cache.GetFields(request.TextDocument.Uri);
        if (fields == null || _context.RootDirectory is not { } root)
            return Task.FromResult<DefinitionResponse?>(null);

        if (GetFieldAtPosition(fields, request.Position) is not { } field)
            return Task.FromResult<DefinitionResponse?>(null);

        // This check should always pass, as `IncludeDataFieldAttribute` shouldn't come up.
        if (field.Attribute is not DataFieldAttribute df)
            return Task.FromResult<DefinitionResponse?>(null);

        _logger.Debug($"Attempting to resolve definition of datafield '{df.Tag ?? "unknown"}' on type: {field.FieldInfo.DeclaringType?.Name ?? "unknown"}");

        if (TryHandleDefinitionSource(field.Attribute.Source, root, out var result))
            return Task.FromResult(result);

        return Task.FromResult(HandleDefinitionFallback(field, root));
    }

    private bool TryHandleDefinitionSource((string File, int Line)? src, Uri root, out DefinitionResponse? result)
    {
        result = null;
        if (src == null)
            return false;

        _logger.Debug($"Handling definition via attribute source: L{src.Value.Line}: {src.Value.File}");
        var file = new Uri(src.Value.File);

        if (!file.IsAbsoluteUri)
        {
            file = new Uri(root, file);
        }
        else if (!root.IsBaseOf(file))
        {
            _logger.Error("Got absolute uri pointing outside of the root directory?");
            result = null;
            return false;
        }

        var pos = new Position(src.Value.Line - 1, 0);
        var location = new Location(file, new DocumentRange {Start = pos, End = pos} );
        result = new DefinitionResponse(location);
        return true;
    }

    private DefinitionResponse? HandleDefinitionFallback(FieldDefinition field, Uri root)
    {
        // TODO better definition handler
        // You can't just resolve a namespace into a path like this
        // This fallback probably shouldn't even exist.

        if (field.FieldInfo.DeclaringType is not { } type || type.Namespace is not { } ns)
            return null;

        _logger.Debug($"Field {field.FieldInfo.Name} declared in type {type.Name} in {type.Namespace}");

        var parts = ns.Split(".");

        // Namespace is assumed to start with a Foo.Bar assembly name
        if (parts.Length <= 2)
            return null;

        var assembly = parts[0] + "." + parts[1];
        _logger.Debug($"Assembly {assembly}");

        UriBuilder uriBuilder = new UriBuilder(root);
        uriBuilder.Path += Path.DirectorySeparatorChar + assembly;
        uriBuilder.Path += Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts.Skip(2));
        uriBuilder.Path += Path.DirectorySeparatorChar + $"{type.Name}.cs";
        _logger.Debug($"UriBuilder: {uriBuilder.Uri.LocalPath}");

        var path = assembly + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts.Skip(2)) + Path.DirectorySeparatorChar + $"{type.Name}.cs";

        // var fullPath = new Uri(_context.RootDirectory, path);
        var fullPath = uriBuilder.Uri;
        _logger.Debug($"Path {_context.RootDirectory} + {path}");
        _logger.Debug($"Path {fullPath}");
        foreach (var part in parts.Skip(2))
        {
            _logger.Debug($"Part: [{part}]");
        }

        _logger.Debug($"Path {fullPath}");
        _logger.Debug($"Path {fullPath.LocalPath}");
        _logger.Debug($"Path {fullPath.AbsolutePath}");
        if (!File.Exists(fullPath.LocalPath))
            return null;

        return new DefinitionResponse(new Location(uriBuilder.Uri,
            new DocumentRange()
            {
                Start = new Position(0, 0),
                End = new Position(0, 1)
            }
        ));
    }

    public override void RegisterCapability(
        ServerCapabilities serverCapabilities,
        ClientCapabilities clientCapabilities)
    {
        serverCapabilities.DefinitionProvider = true;
    }

    // NOTE: More copy-paste, move to shared code if used
    private static FieldDefinition? GetFieldAtPosition(
        List<(ValueDataNode, FieldDefinition)> fields,
        Position position)
    {
        foreach (var (node, field) in fields)
        {
            if (node.Start.Line - 1 == position.Line &&
                position.Character >= node.Start.Column - 1
                && position.Character <= node.End.Column - 1)
            {
                return field;
            }
        }

        return null;
    }
}
