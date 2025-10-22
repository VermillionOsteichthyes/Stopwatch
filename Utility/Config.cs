using redfish.Models;
using redfish.Utility;
using redfish.Interception;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using redfish.Interception.Modules;
using System.Xml.Linq;
using System.Reflection;
using System.Diagnostics;

namespace redfish
{
    public static class Config
    {
        static string ConfigPath = "VermillionOsteichthyes.cfg";
        public static ConfigModel Instance { get; set; }

        static Config()
        {
            ConfigPath = Path.Combine(App.ExeDirectory, ConfigPath);
            Instance = File.Exists(ConfigPath)
                ? File.ReadAllText(ConfigPath).Deserialize<ConfigModel>()
                : Instance ?? new ConfigModel() { Volume = 30 };
        }
        
        public static ModuleSettingsBase GetNamed(string name)
        {
            if (Instance == null)
                Load();

            if (Instance.Modules.TryGetValue(name, out var module))
                return module;

            Instance.Modules[name] = new ModuleSettingsBase();
            return Instance.Modules[name];
        }
        public static void Load()
        {
            Instance = File.Exists(ConfigPath)
                ? File.ReadAllText(ConfigPath).Deserialize<ConfigModel>()
                : Instance ?? new ConfigModel() { Volume = 30 };

            App.snow = Instance.Settings.Window_Snow;
            Debug.WriteLine("Config loaded");
        }

        // In Config.cs - Update the Save() method

        public static void Save()
        {
            if (Instance == null || InterceptionManager.Modules.Count < 8)
                return;

            try
            {
                GetNamed("PVE").Settings["Buffer"] = PveModule.Buffer;
                GetNamed("PVE").Settings["AutoResync"] = PveModule.AutoResync;
                GetNamed("PVE").Settings["OutboundKeybind"] = PveModule.OutboundKeybind;
                GetNamed("PVE").Settings["SlowInboundKeybind"] = PveModule.SlowInboundKeybind;
                GetNamed("PVE").Settings["SlowOutboundKeybind"] = PveModule.SlowOutboundKeybind;
                GetNamed("PVE").Settings["BufferKeybind"] = PveModule.BufferKeybind;
                GetNamed("PVE").Settings["AutoResyncKeybind"] = PveModule.AutoResyncKeybind;

                GetNamed("PVP").Settings["OutboundKeybind"] = PvpModule.OutboundKeybind;
                GetNamed("PVP").Settings["Buffer"] = PvpModule.Buffer;
                GetNamed("PVP").Settings["AutoResync"] = PvpModule.AutoResync;

                GetNamed("API Block").Settings["SelfDisable"] = ApiModule.Disable;
                GetNamed("API Block").Settings["Buffer"] = ApiModule.Buffer;

                GetNamed("З0k").Settings["Buffer"] = InstanceModule.Buffer;
                GetNamed("З0k").Settings["RateLimitingEnabled"] = InstanceModule.RateLimitingEnabled;
                GetNamed("З0k").Settings["TargetBitsPerSecond"] = InstanceModule.TargetBytesPerSecond;

                GetNamed("Multishot").Settings["Inbound"] = MultishotModule.Inbound;
                GetNamed("Multishot").Settings["Outbound"] = MultishotModule.Outbound;
                GetNamed("Multishot").Settings["TimeLimit"] = MultishotModule.MaxTime;
                GetNamed("Multishot").Settings["PlayersMode"] = MultishotModule.PlayersMode;
                GetNamed("Multishot").Settings["ShotDetection"] = MultishotModule.WaitShot;
                GetNamed("Multishot").Settings["Togglable"] = InterceptionManager.GetModule("Multishot").Togglable;
                GetNamed("Multishot").Settings["PlayersKeybind"] = MultishotModule.PlayersKeybind;

                // UPDATED: Save profiles instead of individual settings
                GetNamed("Swap").Settings["Profiles"] = SwapModule.Profiles;

                // OPTIONAL: Keep legacy format for backwards compatibility (will be ignored on load)
                // This ensures older versions can still read something meaningful
                if (SwapModule.Profiles.Any())
                {
                    var firstProfile = SwapModule.Profiles[0];
                    GetNamed("Swap").Settings["SwapKeybind"] = firstProfile.Keybind;
                    GetNamed("Swap").Settings["SwapDuration"] = firstProfile.SwapDuration;
                    GetNamed("Swap").Settings["SwapDelay"] = firstProfile.SwapDelay;
                    GetNamed("Swap").Settings["UntickDelay"] = firstProfile.UntickDelay;
                    GetNamed("Swap").Settings["EndingLoadout"] = firstProfile.EndingLoadout;
                    GetNamed("Swap").Settings["Port3074"] = firstProfile.Port3074;
                    GetNamed("Swap").Settings["LoadoutEnabled"] = firstProfile.LoadoutEnabled;
                    GetNamed("Swap").Settings["Packet3074DL"] = firstProfile.Packet3074DL;
                    GetNamed("Swap").Settings["AutoDisableBuffering"] = firstProfile.AutoDisableBuffering;
                    GetNamed("Swap").Settings["CloseInventory"] = firstProfile.CloseInventory;
                }

                File.WriteAllText(ConfigPath, Instance.Serialize(true));
                Debug.WriteLine($"Config saved");
            }
            catch (Exception e)
            {
                //Debug.WriteLine(e, additionalInfo: "Config save");
            }
        }
    }
}
