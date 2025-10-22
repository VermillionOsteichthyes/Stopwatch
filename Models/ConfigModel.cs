using redfish.Utility;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace redfish.Models
{
    public class ConfigModel
    {
        public string CurrentModule { get; set; }
        public double Volume { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public Settings Settings { get; set; } = new Settings();
        public Dictionary<string, ModuleSettingsBase> Modules { get; set; } = new Dictionary<string, ModuleSettingsBase>();
        public List<string> LastOpenAhks { get; set; } = new List<string>();
    }

    public class Settings
    {
        public string? Tracker_BungieName { get; set; } = null;
        public bool Tracker_CountRaids { get; set; } = true;
        public bool Tracker_CountDungeons { get; set; } = true;
        public bool AltTabSupressKeybinds { get; set; } = true;

        public bool Overlay_StartOnLaunch { get; set; } = true;
        public bool Overlay_ShowTime { get; set; } = false;
        public bool Overlay_ShowTimer { get; set; } = true;
        public bool Overlay_DisableOnInactivity { get; set; } = true;
        public bool Overlay_DisplayOnlyTogglable { get; set; } = true;
        public int Overlay_LeftOffset { get; set; } = 0;
        public int Overlay_BottomOffset { get; set; } = 0;

        public bool Window_Snow { get; set; } = false;
        public bool Window_DisplayClock { get; set; } = true;
        public bool Window_DisplaySpeed { get; set; } = true;
        public int Window_TimerDecaySeconds { get; set; } = 0;

        public bool DB_SavePackets { get; set; } = false;
        public bool DB_KeyPresses { get; set; } = false;

        public bool AHK_AutoClose { get; set; } = true;
        public bool AHK_AutoOpen { get; set; } = true;
    }

    public class ModuleSettingsBase
    {
        public bool Enabled = false;
        public List<Keycode> Keybind = new List<Keycode>();
        public Dictionary<string, object> Settings = new Dictionary<string, object>();

        public T GetSettings<T>(string name)
        {
            if (!Settings.ContainsKey(name))
            {
                Settings[name] = default(T);
                if (name == "Inbound" || name == "SelfDisable" || name == "Buffer" || name == "AutoResync" || name == "RateLimitingEnabled")
                    Settings[name] = true;
                else if (name.Contains("Keybind"))
                    Settings[name] = new List<Keycode>();
                else if (name == "TimeLimit")
                    Settings[name] = 1.8d;
                else if (name == "TargetBitsPerSecond")
                    Settings[name] = 1000000L; // Default as long

                Config.Save();
            }

            if (Settings[name] is JsonElement e)
            {
                if (name.Contains("Keybind"))
                {
                    return JsonSerializer.Deserialize<T>(e);
                }

                if (typeof(T).Equals(typeof(bool)))
                {
                    return (T)Convert.ChangeType(e.GetBoolean(), typeof(T));
                }

                if (typeof(T).Equals(typeof(double)))
                {
                    return (T)Convert.ChangeType(e.GetDouble(), typeof(T));
                }

                if (typeof(T).Equals(typeof(long)))
                {
                    return (T)Convert.ChangeType(e.GetInt64(), typeof(T));
                }

                if (typeof(T).Equals(typeof(int)))
                {
                    return (T)Convert.ChangeType(e.GetInt32(), typeof(T));
                }
            }

            // Handle numeric conversions for non-JsonElement values
            if (typeof(T) == typeof(long) && Settings[name] is int intValue)
            {
                return (T)Convert.ChangeType((long)intValue, typeof(T));
            }

            return (T)Convert.ChangeType(Settings[name], typeof(T));
        }
    }
}
