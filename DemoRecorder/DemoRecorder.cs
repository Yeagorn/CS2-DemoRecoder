using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace DemoRecorder;
    
public class DemoRecorder : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName { get; } = "DemoRecorder";
    public override string ModuleVersion { get; } = "1.0.1";
    public override string ModuleAuthor { get; } = "SAPSAN";
    public required PluginConfig Config { get; set; }

    public List<CCSPlayerController> connectedPlayers = new();

    private static string g_sDemosName = new (DateTime.Now.ToString("dd_MM_yyyy_HH_mm") + "-" + Server.MapName + ".dem"),
                          g_sDemosDir = Server.GameDirectory;

    private static readonly string g_BinaryPath = g_sDemosDir + "/bin/" + (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linuxsteamrt64/libengine2.so" : "win64/engine2.dll"),
                         g_Signature = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) 
                            ? @"\x55\x48\x89\xE5\x41\x57\x41\x56\x41\x55\x49\x89\xF5\x41\x54\x4C\x8D\x67\x08"
                            : @"\x40\x55\x56\x41\x57\x48\x8D\x6C\x24\x00\x48\x81\xEC\x00\x00\x00\x00\x80\xB9\x00\x00\x00\x00\x00";

    public bool g_bChangeMap, bOldState, g_bState;

    public MemoryFunctionVoid<IntPtr, IntPtr> RecordEnd = new(g_Signature, g_BinaryPath);

    private HookResult RecordEndHookResult(DynamicHook hook)
    {
        Task.Delay(1000).ContinueWith((task) =>
        {
            UploadDemo(g_sDemosDir + g_sDemosName, g_bState);
        });
        return HookResult.Continue;
    }

    public void OnConfigParsed(PluginConfig config)
    {
        config = ConfigManager.Load<PluginConfig>(ModuleName);
        Config = config;

        g_sDemosDir = Server.GameDirectory + "/csgo/addons/counterstrikesharp/data/" + Config.DemosDir;
        CreateDemoDir();
    }

    public override void Load(bool hotReload)
    {
        Directory.SetCurrentDirectory(Server.GameDirectory);

        CreateDemoDir();

        RecordEnd.Hook(RecordEndHookResult, HookMode.Post);

        RegisterEventHandler<EventCsIntermission>(OnEventCsIntermissionPost);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        RegisterListener<Listeners.OnMapEnd>(OnMapEndHandler);
        AddCommandListener("changelevel", CommandListener_Changelevel, HookMode.Pre);
        AddCommandListener("host_workshop_map", CommandListener_Workshop, HookMode.Pre);
        AddCommandListener("ds_workshop_changelevel", CommandListener_WorkshopLevel, HookMode.Pre);
        UploadAllDemos();
    }

    public override void Unload (bool hotReload)
    {
        RecordEnd.Unhook(RecordEndHookResult, HookMode.Post);
    }

    [GameEventHandler(mode: HookMode.Post)]
    private HookResult OnEventCsIntermissionPost(EventCsIntermission @event, GameEventInfo info)
    {
        g_bState = true;
        RecordDemo(false);
        g_bChangeMap = true;
        return HookResult.Continue;
    }

    [RequiresPermissions("@css/root")]
    [ConsoleCommand("css_dr_reload")]
    public void OnReloadCommand(CCSPlayerController? controller, CommandInfo info)
    {
        OnConfigParsed(Config);
        Logger.LogInformation(">> Config reloaded!");
        controller?.PrintToChat($" {ChatColors.Red}[Demo Recorder] {ChatColors.Default}Config reloaded {ChatColors.Green}success{ChatColors.Default}!");
    }

    [GameEventHandler]
    public HookResult OnPlayerConnectedFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!player.IsBot)
        {
            connectedPlayers.Add(player);

            if (GetActivePlayerCount() >= Config.MinOnline)
            {
                g_bState = false;
                RecordDemo(true);
            }
            return HookResult.Continue;
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        connectedPlayers.Remove(player);

        if (GetActivePlayerCount() < Config.MinOnline)
        {
            g_bState = true;
            RecordDemo(false);
            
        }
        return HookResult.Continue;
    }

    private void OnMapStartHandler(string mapName)
    {
        g_bChangeMap = false;
        g_sDemosName = new string(DateTime.Now.ToString("dd_MM_yyyy_HH_mm") + "-" + mapName + ".dem");
    }

    private void CreateDemoDir()
    {
        if (!Directory.Exists(g_sDemosDir))
        {
            Logger.LogInformation(">> Create folder for demos: {Folder}.", g_sDemosDir);

            Directory.CreateDirectory(g_sDemosDir);
        }
    }

    private void OnMapEndHandler()
    {
        if (!g_bChangeMap)
        {
            g_bState = false;
            RecordDemo(false);
            g_bChangeMap = true;
        }
    }

    private void RecordDemo(bool bState)
    {
        if (g_bChangeMap)
        {
            return;
        }

        if (bState && !bOldState)
        {
            bOldState = true;

            Server.ExecuteCommand($"tv_record \"addons/counterstrikesharp/data/{Config.DemosDir}{g_sDemosName}\"");

            Logger.LogInformation(">> Recording start ({Name}).", g_sDemosName);
        }
        else if (!bState && bOldState)
        {
            bOldState = false;
            Server.ExecuteCommand($"tv_stoprecord");
            Logger.LogInformation(">> Recording stop ({Name}).", g_sDemosName);
            
        }
    }

    async void UploadDemo(string path, bool bUploadOld = false)
    {
        if (!File.Exists(path)) return;

        try
        {
            string Status = await UploadFile(path);
            
            if(!String.IsNullOrEmpty(Status))
            {
                Logger.LogInformation(">> Demo upload complete: {File}", path.Split('/').Last());
                File.Delete(path);
            }  
            else Logger.LogInformation(">> Demo upload faill: {File}", path.Split('/').Last());

        }
        catch (Exception ex)
        {
            Logger.LogInformation(">> UploadDemo Exception: {ex}", ex);
        }

        if(bUploadOld)
        {
            UploadAllDemos();
        }
    }

     private HookResult CommandListener_WorkshopLevel(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (AdminManager.PlayerHasPermissions(player, "@css/generic")) {
            if (bOldState)
            {
                // Stop the demo recording
                RecordDemo(false);

                // Get the next map's name, remove any quotes, trim spaces, and convert to lowercase
                var _nextMap = commandInfo.ArgString.Trim().Replace("\"", "").ToLower();

                // Validate if the map name is not empty or invalid
                if (string.IsNullOrWhiteSpace(_nextMap))
                {
                    Logger.LogWarning(">> CommandListener_Changelevel: Invalid map name provided.");
                    return HookResult.Continue; // Allow the command to continue since the map name is invalid
                }

                // Schedule the map change after a 2-second delay
                AddTimer(2.0f, () =>
                {
                    Server.ExecuteCommand($"ds_workshop_changelevel {_nextMap}");
                });

                // Stop further processing of the changelevel command
                return HookResult.Stop;
            }

            // Continue with the normal execution of the changelevel command if conditions are not met
            return HookResult.Continue;
        } else {
            return HookResult.Stop; 
        }
    }

    private HookResult CommandListener_Workshop(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (AdminManager.PlayerHasPermissions(player, "@css/generic")) {
            if (bOldState)
            {
                // Stop the demo recording
                RecordDemo(false);

                // Get the next map's name, remove any quotes, trim spaces, and convert to lowercase
                var _nextMap = commandInfo.ArgString.Trim().Replace("\"", "").ToLower();

                // Validate if the map name is not empty or invalid
                if (string.IsNullOrWhiteSpace(_nextMap))
                {
                    Logger.LogWarning(">> CommandListener_Changelevel: Invalid map name provided.");
                    return HookResult.Continue; // Allow the command to continue since the map name is invalid
                }

                // Schedule the map change after a 2-second delay
                AddTimer(2.0f, () =>
                {
                    Server.ExecuteCommand($"host_workshop_map {_nextMap}");
                });

                // Stop further processing of the changelevel command
                return HookResult.Stop;
            }

            // Continue with the normal execution of the changelevel command if conditions are not met
            return HookResult.Continue;
        } else {
            return HookResult.Stop; 
        }
    }

    private HookResult CommandListener_Changelevel(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (AdminManager.PlayerHasPermissions(player, "@css/generic")) {
            if (bOldState)
            {
                // Stop the demo recording
                RecordDemo(false);

                // Get the next map's name, remove any quotes, trim spaces, and convert to lowercase
                var _nextMap = commandInfo.ArgString.Trim().Replace("\"", "").ToLower();

                // Validate if the map name is not empty or invalid
                if (string.IsNullOrWhiteSpace(_nextMap))
                {
                    Logger.LogWarning(">> CommandListener_Changelevel: Invalid map name provided.");
                    return HookResult.Continue; // Allow the command to continue since the map name is invalid
                }

                // Schedule the map change after a 2-second delay
                AddTimer(2.0f, () =>
                {
                    Server.ExecuteCommand($"changelevel {_nextMap}");
                });

                // Stop further processing of the changelevel command
                return HookResult.Stop;
            }

            // Continue with the normal execution of the changelevel command if conditions are not met
            return HookResult.Continue;
        } else {
            return HookResult.Stop; 
        }
    }

    public async Task<string> UploadFile(string path)
    {
        using var client = new HttpClient();
        {
            using var content = new StreamContent(File.OpenRead(path));
            {
                content.Headers.Remove("Content-Type");
                content.Headers.Add("Content-Type", "application/octet-stream");

                using var req = new HttpRequestMessage(HttpMethod.Put, Config.UploadUrl);
                {
                    req.Headers.Add("Auth", Config.Token);
                    req.Headers.Add("Demo-Name", path.Split('/').Last());
                    req.Headers.Add("Demo-ServerId", Config.ServerId.ToString());
                    req.Headers.Add("Demo-Time", File.GetCreationTime(path).ToString());

                    req.Content = content;

                    using HttpResponseMessage resp = await client.SendAsync(req);
                    {
                        resp.EnsureSuccessStatusCode();
                        return await resp.Content.ReadAsStringAsync();
                    }
                }
            }
        }
    }

    void UploadAllDemos()
    {
        foreach (string file in Directory.GetFiles(g_sDemosDir))
        {
            Logger.LogInformation(">> Try upload old demo: {File}", file);
            UploadDemo(file);
        }
    }

    private int GetActivePlayerCount()
    {
        return connectedPlayers.Count;
    }
}
