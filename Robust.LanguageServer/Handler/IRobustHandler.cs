using EmmyLua.LanguageServer.Framework.Server.Handler;
using Robust.Shared.Log;

namespace Robust.LanguageServer.Handler;

internal interface IRobustHandler : IJsonHandler
{
    public void Init(ISawmill logger);
}
