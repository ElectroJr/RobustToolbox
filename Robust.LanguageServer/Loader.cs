using System.Globalization;
using System.Reflection;
using System.Text;
using Robust.Client;
using Robust.Server.GameObjects;
using Robust.Server;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;

namespace Robust.LanguageServer;

public sealed class Loader
{
    [Dependency] private readonly IResourceManagerInternal _resources = default!;
    [Dependency] private readonly INetConfigurationManagerInternal _config = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly ILogManager _logMan = default!;
    [Dependency] private readonly IModLoaderInternal _modLoader = default!;
    [Dependency] private readonly IReflectionManager _reflection = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    private ISawmill _logger = default!;

    public void Init()
    {
        // TODO
        // Consider maybe using something like the map renderer and abusing the integration test server-client pair code
        // to create a server & client instance, instead of having to do all this setup?
        //
        // Then again, if this is meant to be lightweight and load as little of the actual game as possible, its probably better to keep as is.

        SetupLogging();
        _logger = _logMan.GetSawmill("loader");

        _config.LoadCVarsFromAssembly(typeof(IConfigurationManager).Assembly); // Robust.Shared

        string? dataDir = null;

        // Set up the VFS
        _resources.Initialize(dataDir);

        var loadServer = true;

        // Why aren't these a shared interface :(
        ServerOptions serverOptions = new();
        GameControllerOptions clientOptions = new();

        if (loadServer)
        {
            ProgramShared.DoMounts(_resources,
                serverOptions.MountOptions,
                serverOptions.ContentBuildDirectory,
                serverOptions.AssemblyDirectory,
                serverOptions.LoadContentResources,
                serverOptions.ResourceMountDisabled);
        }
        else
        {
            ProgramShared.DoMounts(_resources,
                clientOptions.MountOptions,
                clientOptions.ContentBuildDirectory,
                clientOptions.AssemblyDirectory,
                clientOptions.LoadContentResources,
                clientOptions.ResourceMountDisabled);
        }

        // _modLoader.SetUseLoadContext(!ContentStart);

        var resourceManifest = ResourceManifestData.LoadResourceManifest(_resources);

        _logger.Debug($"Options.AssemblyDirectory: {serverOptions.AssemblyDirectory} - {resourceManifest.AssemblyPrefix} - {serverOptions.ContentModulePrefix}");

        if (!_modLoader.TryLoadModulesFrom(serverOptions.AssemblyDirectory,
                resourceManifest.AssemblyPrefix ?? serverOptions.ContentModulePrefix))
        {
           _logger.Error("Errors while loading content assemblies.");
            return;
        }

        foreach (var loadedModule in _modLoader.LoadedModules)
        {
            _config.LoadCVarsFromAssembly(loadedModule);
        }


        InitReflectionManager();
        _reflection.LoadAssemblies(typeof(PointLightComponent).Assembly);
        // deps.Resolve<IReflectionManager>().LoadAssemblies(typeof(SpriteComponent).Assembly);
        // deps.Resolve<IReflectionManager>().Initialize();

        foreach (var asm in _reflection.Assemblies)
        {
            _logger.Info("Loaded: " + asm.FullName);
        }

        _factory.DoAutoRegistrations();

        if (loadServer)
            _factory.IgnoreMissingComponents("Visuals");
        else
            _factory.IgnoreMissingComponents();

        if (loadServer)
            AddServerComponentIgnores(_factory);

        _factory.GenerateNetIds();

        var culture = new CultureInfo("en-US", false);
        _loc.LoadCulture(culture);

        _serialization.Initialize();

        _protoMan.Initialize();

        if (!loadServer)
            AddClientPrototypeIgnores(_protoMan);

        _protoMan.RegisterIgnore("parallax");

        Dictionary<Type, HashSet<string>> changed = new();
        _protoMan.LoadDirectory(new(@"/EnginePrototypes"), false, changed);
        _logger.Debug($"protoMan: engine {_protoMan} - changed = {changed.Count}");
        _protoMan.LoadDirectory(new(@"/Prototypes"), false, changed);
        _protoMan.ResolveResults();

        _logger.Debug($"protoMan: {_protoMan} - changed = {changed.Count}");
    }

    private void InitReflectionManager()
    {
        // gets a handle to the shared and the current (server) dll.
        _reflection.LoadAssemblies(new List<Assembly>(2)
            {
                AppDomain.CurrentDomain.GetAssemblyByName("Robust.Shared"),
                Assembly.GetExecutingAssembly()
            });
    }

    private void SetupLogging()
    {
        // TODO
        // I think this is mostly copy-pasted from server's Program.SetupLogging
        // how much of this is actually required?
        if (OperatingSystem.IsWindows())
        {
#if WINDOWS_USE_UTF8_CONSOLE
                System.Console.OutputEncoding = Encoding.UTF8;
#else
            System.Console.OutputEncoding = Encoding.Unicode;
#endif
        }

        var handler = new ConsoleLogHandler();
        _logMan.RootSawmill.AddHandler(handler);
        _logMan.GetSawmill("res.typecheck").Level = LogLevel.Info;
        _logMan.GetSawmill("go.sys").Level = LogLevel.Info;
        _logMan.GetSawmill("loc").Level = LogLevel.Error;
        // mgr.GetSawmill("szr").Level = LogLevel.Info;

#if DEBUG_ONLY_FCE_INFO
#if DEBUG_ONLY_FCE_LOG
            var fce = mgr.GetSawmill("fce");
#endif
            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                // TODO: record FCE stats
#if DEBUG_ONLY_FCE_LOG
                fce.Fatal(message);
#endif
            }
#endif

        var uh = _logMan.GetSawmill("unhandled");
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var message = ((Exception)args.ExceptionObject).ToString();
            try
            {
                uh.Log(args.IsTerminating ? LogLevel.Fatal : LogLevel.Error, message);
            }
            catch (ObjectDisposedException)
            {
                // Avoid eating the exception if it's during shutdown and the sawmill is already gone.
                System.Console.Error.WriteLine($"UnhandledException but sawmill is disposed! {message}");
            }
        };

        var uo = _logMan.GetSawmill("unobserved");
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            try
            {
                uo.Error(args.Exception!.ToString());
            }
            catch (ObjectDisposedException)
            {
                // Avoid eating the exception if it's during shutdown and the sawmill is already gone.
                System.Console.Error.WriteLine($"UnobservedTaskException but sawmill is disposed! {args.Exception}");
            }
#if EXCEPTION_TOLERANCE
                args.SetObserved(); // don't crash
#endif
        };
    }

    // This list is copied from Content.Server.Entry.IgnoredComponents
    // Would be preferable to move this to a data file if needed
    // TODO FIX
    private static void AddServerComponentIgnores(IComponentFactory factory)
    {
        var list = new[]
        {
            "ConstructionGhost",
            "IconSmooth",
            "InteractionOutline",
            "Marker",
            "GuidebookControlsTest",
            "GuideHelp",
            "Clickable",
            "Icon",
            "CableVisualizer",
            "SolutionItemStatus",
            "UIFragment",
            "PdaBorderColor",
            "InventorySlots",
            "LightFade",
            "HolidayRsiSwap",
            "OptionsVisualizer",
            "AnomalyScannerScreen",
            "MultipartMachineGhost"
        };
        factory.RegisterIgnore(list);
    }

    // Below list copied from client EntryPoint.Init
    // TODO FIX
    private static void AddClientPrototypeIgnores(IPrototypeManager protoMan)
    {
        protoMan.RegisterIgnore("utilityQuery");
        protoMan.RegisterIgnore("utilityCurvePreset");
        protoMan.RegisterIgnore("accent");
        protoMan.RegisterIgnore("gasReaction");
        protoMan.RegisterIgnore("seed"); // Seeds prototypes are server-only.
        protoMan.RegisterIgnore("objective");
        protoMan.RegisterIgnore("holiday");
        protoMan.RegisterIgnore("htnCompound");
        protoMan.RegisterIgnore("htnPrimitive");
        protoMan.RegisterIgnore("gameMap");
        protoMan.RegisterIgnore("gameMapPool");
        protoMan.RegisterIgnore("lobbyBackground");
        protoMan.RegisterIgnore("gamePreset");
        protoMan.RegisterIgnore("noiseChannel");
        protoMan.RegisterIgnore("playerConnectionWhitelist");
        protoMan.RegisterIgnore("spaceBiome");
        protoMan.RegisterIgnore("worldgenConfig");
        protoMan.RegisterIgnore("gameRule");
        protoMan.RegisterIgnore("worldSpell");
        protoMan.RegisterIgnore("entitySpell");
        protoMan.RegisterIgnore("instantSpell");
        protoMan.RegisterIgnore("roundAnnouncement");
        protoMan.RegisterIgnore("wireLayout");
        protoMan.RegisterIgnore("alertLevels");
        protoMan.RegisterIgnore("nukeopsRole");
        protoMan.RegisterIgnore("ghostRoleRaffleDecider");
    }
}
