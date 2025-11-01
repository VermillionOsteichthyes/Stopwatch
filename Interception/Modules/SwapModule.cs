using redfish.Models;
using redfish.Utility;
using redfish.Interception;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Navigation;

namespace redfish.Interception.Modules
{
    public class SwapModule : PacketModuleBase
    {
        public override string DisplayName => "Swap";

        // State switches (like PveModule)
        public static bool IsSwapping = false;

        // NEW: Profile-based system
        public static List<SwapProfile> Profiles { get; set; } = new List<SwapProfile>();

        // Keep these for backwards compatibility during migration
        private static int activeProfileIndex = 0;

        private static readonly Dictionary<(int, int), Func<Dictionary<int, (int X, int Y)>>> ResolutionCoordinateMap = new()
        {
            { (1920, 1080), GetCoordinates1920x1080 },
            { (2560, 1440), GetCoordinates2560x1440 },
            { (3840, 2160), GetCoordinates3840x2160 },
            { (2560, 1080), GetCoordinates2560x1080 },
            { (2048, 1280), GetCoordinates2048x1280 },
            { (1920, 1440), GetCoordinates1920x1440 },
            { (1920, 1200), GetCoordinates1920x1200 },
            { (1680, 1050), GetCoordinates1680x1050 },
            { (1600, 1200), GetCoordinates1600x1200 },
            { (1600, 1024), GetCoordinates1600x1024 },
            { (1600, 900), GetCoordinates1600x900 },
            { (1440, 1080), GetCoordinates1440x1080 },
            { (1440, 900), GetCoordinates1440x900 },
            { (1366, 768), GetCoordinates1366x768 },
            { (1360, 768), GetCoordinates1360x768 },
            { (1280, 1440), GetCoordinates1280x1440 },
            { (1280, 1024), GetCoordinates1280x1024 },
            { (1280, 960), GetCoordinates1280x960 },
            { (1280, 800), GetCoordinates1280x800 },
            { (1280, 768), GetCoordinates1280x768 },
            { (1280, 720), GetCoordinates1280x720 }
        };

        public SwapModule() : base("Swap", false)
        {
            Description = "Automated loadout swapping with port control";
            Debug.WriteLine($"{Name}: SwapModule constructor called");

            LoadConfiguration();
            KeyListener.KeysPressed += SwapHandler;

            Debug.WriteLine($"{Name}: SwapModule initialized with {Profiles.Count} profile(s)");
        }

        public void LoadConfiguration()
        {
            var swapConfig = Config.GetNamed("Swap");

            // Try to load profiles array first (new format)
            if (swapConfig.Settings.TryGetValue("Profiles", out var profilesObj))
            {
                Profiles = LoadProfilesFromConfig(profilesObj);
                Debug.WriteLine($"{Name}: Loaded {Profiles.Count} profiles from config");
            }
            else
            {
                // Migrate old single-swap format to profile format
                Debug.WriteLine($"{Name}: No profiles found, migrating old format");
                var legacyProfile = new SwapProfile
                {
                    Name = "Swap 1",
                    Keybind = LoadKeybindFromConfig(swapConfig.Settings, "SwapKeybind"),
                    SwapDuration = GetIntFromConfig(swapConfig.Settings, "SwapDuration", 2000),
                    SwapDelay = GetIntFromConfig(swapConfig.Settings, "SwapDelay", 25),
                    UntickDelay = GetIntFromConfig(swapConfig.Settings, "UntickDelay", 500),
                    EndingLoadout = GetIntFromConfig(swapConfig.Settings, "EndingLoadout", 1),
                    Port3074 = GetBoolFromConfig(swapConfig.Settings, "Port3074", true),
                    LoadoutEnabled = LoadLoadoutsFromConfig(swapConfig.Settings, "LoadoutEnabled"),
                    Packet3074DL = GetBoolFromConfig(swapConfig.Settings, "Packet3074DL", false),
                    AutoDisableBuffering = GetBoolFromConfig(swapConfig.Settings, "AutoDisableBuffering", false),
                    CloseInventory = GetBoolFromConfig(swapConfig.Settings, "CloseInventory", false)
                };

                // Create default profiles 2-8
                var profile2 = new SwapProfile { Name = "Swap 2" };
                var profile3 = new SwapProfile { Name = "Swap 3" };
                var profile4 = new SwapProfile { Name = "Swap 4" };
                var profile5 = new SwapProfile { Name = "Swap 5" };

                Profiles = new List<SwapProfile> { legacyProfile, profile2 };

                // Save migrated profiles
                SaveProfiles();
            }

            // Ensure we have at least x profiles where x is the max number of profiles supported
            while (Profiles.Count < 5)
            {
                Profiles.Add(new SwapProfile { Name = $"Swap {Profiles.Count + 1}" });
            }
        }

        public static void SaveProfiles()
        {
            var swapConfig = Config.GetNamed("Swap");
            swapConfig.Settings["Profiles"] = Profiles;
            Config.Save();
            Debug.WriteLine($"SwapModule: Saved {Profiles.Count} profiles");
        }


        private void SwapHandler(LinkedList<Keycode> keycodes)
        {
            if (!KeybindChecks()) return;

            // Check each profile's keybind
            for (int i = 0; i < Profiles.Count; i++)
            {
                var profile = Profiles[i];
                if (profile.Keybind.Any() &&
                    keycodes.Count >= profile.Keybind.Count &&
                    profile.Keybind.All(x => keycodes.Contains(x)))
                {
                    Debug.WriteLine($"{Name}: Profile '{profile.Name}' keybind matched! Executing swap...");
                    ExecuteSwap(i);
                    return; // Only execute first matching profile
                }
            }
        }

        public void ExecuteSwap(int profileIndex = 0)
        {
            if (IsSwapping) return;

            if (profileIndex < 0 || profileIndex >= Profiles.Count)
            {
                Debug.WriteLine($"{Name}: Invalid profile index {profileIndex}");
                return;
            }

            var p = Process.GetProcessesByName("destiny2");
            if (p.Length == 0)
            {
                Debug.WriteLine($"{Name}: Destiny 2 not found");
                return;
            }

            var profile = Profiles[profileIndex];
            Debug.WriteLine($"{Name}: Executing profile '{profile.Name}'");

            ToggleSwapState(true);

            Task.Run(async () =>
            {
                try
                {
                    await ExecuteSwapSequence(profile);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex, $"{Name}: Error during swap execution");
                }
                finally
                {
                    ToggleSwapState(false);
                }
            });
        }


        private void ToggleSwapState(bool swapping)
        {
            IsSwapping = swapping;
            if (swapping)
            {
                IsActivated = true;
                StartTime = DateTime.Now;
            }
            else
            {
                IsActivated = false;
                StartTime = DateTime.Now;
            }

            // Update UI (like PveModule does)
            MainWindow.Instance?.Dispatcher.BeginInvoke(() =>
            {
                (swapping ? EnableSound : DisableSound).Play();
            });
        }

        public override void Toggle()
        {
            ExecuteSwap();
        }

        private async Task ExecuteSwapSequence(SwapProfile profile)
        {
            Debug.WriteLine($"{Name}: Starting swap sequence for '{profile.Name}'");

            var enabledLoadouts = profile.LoadoutEnabled
                .Select((enabled, index) => new { Index = index + 1, Enabled = enabled })
                .Where(x => x.Enabled)
                .Select(x => x.Index)
                .ToList();

            if (!enabledLoadouts.Any())
            {
                Debug.WriteLine($"{Name}: No loadouts selected in profile '{profile.Name}'");
                return;
            }

            var originalBufferState = await GetCurrentBufferState();

            try
            {
                if (profile.AutoDisableBuffering && originalBufferState)
                {
                    await SetBufferState(false);
                }

                await ActivatePortModules(profile);
                await OpenInventory();
                await PerformLoadoutClicking(enabledLoadouts, profile.SwapDuration, profile.SwapDelay);

                if (profile.EndingLoadout >= 1 && profile.EndingLoadout <= 12)
                {
                    await ClickEndingLoadout(profile.EndingLoadout);
                }

                if (profile.CloseInventory)
                {
                    await CloseInventoryUI();
                }

                await Task.Delay(profile.UntickDelay);
                await DeactivatePortModules(profile);
            }
            finally
            {
                if (profile.AutoDisableBuffering)
                {
                    await SetBufferState(originalBufferState);
                }
            }

            Debug.WriteLine($"{Name}: Swap sequence completed for '{profile.Name}'");
        }

        private async Task<bool> GetCurrentBufferState()
        {
            // Check current buffer state from PveModule
            return PveModule.Buffer;
        }

        private async Task<Dictionary<string, bool>> GetCurrentModuleStates()
        {
            return new Dictionary<string, bool>
            {
                ["PVE"] = InterceptionManager.GetModule("PVE")?.IsActivated ?? false,
                ["PVP"] = InterceptionManager.GetModule("PVP")?.IsActivated ?? false,
            };
        }

        private async Task SetBufferState(bool enabled)
        {
            var pveModule = InterceptionManager.GetModule("PVE") as PveModule;
            if (pveModule != null)
            {
                PveModule.Buffer = enabled;
                //PvpModule.Buffer = enabled;
                MainWindow.Instance.Dispatcher.Invoke(() =>
                {
                    MainWindow.Instance.PveBufferCB.SetState(enabled);
                    //MainWindow.Instance.PvpBufferCB.SetState(enabled);  
                });
                Debug.WriteLine($"{Name}: Set buffer state to {enabled}");
            }
        }

        private async Task ActivatePortModules(SwapProfile profile)
        {
            Debug.WriteLine($"{Name}: Activating port modules - Port: {(profile.Port3074 ? "3074" : "27k")}, 3074DL: {profile.Packet3074DL}");

            if (profile.Port3074)
            {
                var pveModule = InterceptionManager.GetModule("PVE") as PveModule;
                if (pveModule != null && !PveModule.Outbound)
                {
                    pveModule.ToggleSwitch(ref PveModule.Outbound, true);
                    MainWindow.Instance.Dispatcher.Invoke(() => MainWindow.Instance.PveOutCB.SetState(true));
                    Debug.WriteLine($"{Name}: Activated PVE outbound blocking");
                }
            }
            else
            {
                var pvpModule = InterceptionManager.GetModule("PVP") as PvpModule;
                if (pvpModule != null && !PvpModule.Outbound)
                {
                    pvpModule.ToggleSwitch(ref PvpModule.Outbound, true);
                    MainWindow.Instance.Dispatcher.Invoke(() => MainWindow.Instance.PvpOutCB.SetState(true));
                    Debug.WriteLine($"{Name}: Activated PVP outbound blocking");
                }
            }

            if (profile.Packet3074DL)
            {
                var pveModule = InterceptionManager.GetModule("PVE") as PveModule;
                if (pveModule != null && !PveModule.Inbound)
                {
                    pveModule.ToggleSwitch(ref PveModule.Inbound, true);
                    MainWindow.Instance.Dispatcher.Invoke(() => MainWindow.Instance.PveInCB.SetState(true));
                    Debug.WriteLine($"{Name}: Activated PVE inbound blocking");
                }
            }
        }


        private async Task DeactivatePortModules(SwapProfile profile)
        {
            Debug.WriteLine($"{Name}: Deactivating port modules");

            if (profile.Port3074)
            {
                var pveModule = InterceptionManager.GetModule("PVE") as PveModule;
                if (pveModule != null && PveModule.Outbound)
                {
                    pveModule.ToggleSwitch(ref PveModule.Outbound, false);
                    MainWindow.Instance.Dispatcher.Invoke(() => MainWindow.Instance.PveOutCB.SetState(false));
                    Debug.WriteLine($"{Name}: Deactivated PVE outbound");
                }
            }
            else
            {
                var pvpModule = InterceptionManager.GetModule("PVP") as PvpModule;
                if (pvpModule != null && PvpModule.Outbound)
                {
                    pvpModule.ToggleSwitch(ref PvpModule.Outbound, false);
                    MainWindow.Instance.Dispatcher.Invoke(() => MainWindow.Instance.PvpOutCB.SetState(false));
                    Debug.WriteLine($"{Name}: Deactivated PVP outbound");
                }
            }

            if (profile.Packet3074DL)
            {
                var pveModule = InterceptionManager.GetModule("PVE") as PveModule;
                if (pveModule != null && PveModule.Inbound)
                {
                    pveModule.ToggleSwitch(ref PveModule.Inbound, false);
                    MainWindow.Instance.Dispatcher.Invoke(() => MainWindow.Instance.PveInCB.SetState(false));
                    Debug.WriteLine($"{Name}: Deactivated PVE inbound");
                }
            }
        }

        // Helper method to load profiles from config
        private List<SwapProfile> LoadProfilesFromConfig(object profilesObj)
        {
            var profiles = new List<SwapProfile>();

            if (profilesObj is List<SwapProfile> directList)
            {
                return new List<SwapProfile>(directList);
            }

            // Handle JSON deserialization
            if (profilesObj is System.Text.Json.JsonElement jsonElement &&
                jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                try
                {
                    foreach (var item in jsonElement.EnumerateArray())
                    {
                        var profile = new SwapProfile
                        {
                            Name = item.TryGetProperty("Name", out var name) ? name.GetString() : "Swap",
                            SwapDuration = item.TryGetProperty("SwapDuration", out var dur) ? dur.GetInt32() : 2000,
                            SwapDelay = item.TryGetProperty("SwapDelay", out var delay) ? delay.GetInt32() : 25,
                            UntickDelay = item.TryGetProperty("UntickDelay", out var untick) ? untick.GetInt32() : 500,
                            EndingLoadout = item.TryGetProperty("EndingLoadout", out var ending) ? ending.GetInt32() : 1,
                            Port3074 = item.TryGetProperty("Port3074", out var port) && port.GetBoolean(),
                            Packet3074DL = item.TryGetProperty("Packet3074DL", out var packet) && packet.GetBoolean(),
                            AutoDisableBuffering = item.TryGetProperty("AutoDisableBuffering", out var auto) && auto.GetBoolean(),
                            CloseInventory = item.TryGetProperty("CloseInventory", out var close) && close.GetBoolean(),
                        };

                        // Load keybind
                        if (item.TryGetProperty("Keybind", out var keybindProp) &&
                            keybindProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var key in keybindProp.EnumerateArray())
                            {
                                if (key.TryGetInt32(out int keyValue) && Enum.IsDefined(typeof(Keycode), keyValue))
                                {
                                    profile.Keybind.Add((Keycode)keyValue);
                                }
                            }
                        }

                        // Load loadouts
                        if (item.TryGetProperty("LoadoutEnabled", out var loadoutsProp) &&
                            loadoutsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var loadouts = loadoutsProp.EnumerateArray().ToArray();
                            if (loadouts.Length == 12)
                            {
                                for (int i = 0; i < 12; i++)
                                {
                                    profile.LoadoutEnabled[i] = loadouts[i].GetBoolean();
                                }
                            }
                        }

                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SwapModule: Error loading profiles from JSON: {ex.Message}");
                }
            }

            return profiles;
        }

        private List<Keycode> LoadKeybindFromConfig(Dictionary<string, object> settings, string key)
        {
            var keybind = new List<Keycode>();

            if (settings.TryGetValue(key, out var keybindObj))
            {
                if (keybindObj is List<Keycode> directList)
                {
                    keybind = new List<Keycode>(directList);
                }
                else if (keybindObj is System.Text.Json.JsonElement jsonKeybind &&
                         jsonKeybind.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in jsonKeybind.EnumerateArray())
                    {
                        if (item.TryGetInt32(out int keyValue) && Enum.IsDefined(typeof(Keycode), keyValue))
                        {
                            keybind.Add((Keycode)keyValue);
                        }
                    }
                }
            }

            return keybind;
        }

        private bool[] LoadLoadoutsFromConfig(Dictionary<string, object> settings, string key)
        {
            var loadouts = new bool[12];

            if (settings.TryGetValue(key, out var loadoutObj))
            {
                if (loadoutObj is bool[] boolArray && boolArray.Length == 12)
                {
                    return (bool[])boolArray.Clone();
                }
                else if (loadoutObj is System.Text.Json.JsonElement jsonElement &&
                         jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var jsonArray = jsonElement.EnumerateArray().ToArray();
                    if (jsonArray.Length == 12)
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            loadouts[i] = jsonArray[i].GetBoolean();
                        }
                    }
                }
            }

            return loadouts;
        }

        private async Task OpenInventory()
        {
            Debug.WriteLine($"{Name}: Opening inventory...");

            // Send F1 to open inventory
            await SimulateKeyPress((int)Keycode.VK_F1);
            await Task.Delay(550);

            // Navigate to the loadouts screen
            await SimulateKeyPress((int)Keycode.VK_LEFT);
            await Task.Delay(70);
            await SimulateKeyPress((int)Keycode.VK_LEFT);
            await Task.Delay(70);
        }

        private async Task<bool> IsInventoryOpen()
        {
            var screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;

            (int X, int Y) coord1, coord2;
            System.Drawing.Color expectedColor = System.Drawing.Color.FromArgb(224, 224, 224); // E0 = 224 +/- tolerance (16) D0 to F0

            // Center X coordinate for both inv checks
            coord1.X = screenWidth / 2;
            coord2.X = screenWidth / 2;

            if (screenWidth == 2560 && screenHeight == 1440) // 1440p 16:9
            {
                coord1.Y = 1380;
                coord2.Y = 1351;
            }
            else if (screenHeight == 1080) // 1080p, good for 16:9 and ultrawide 64/27
            {
                coord1.Y = 1035;
                coord2.Y = 1014;
            }
            else if (screenWidth == 1920 && screenHeight == 1200) // 8:5 1200p
            {
                coord1.Y = 1155;
                coord2.Y = 1178;
            }
            else if (screenHeight == 1440) // might work for 1440s, not certain
            {
                coord1.Y = 1380;
                coord2.Y = 1351;
            }
            else // Default to 1080p
            {
                coord1.Y = 1035;
                coord2.Y = 1014;
            }

            var pixel1 = GetPixelColor(coord1.X, coord1.Y);
            var pixel2 = GetPixelColor(coord2.X, coord2.Y);

            return IsColorSimilar(pixel1, expectedColor, 16) ||
                IsColorSimilar(pixel2, expectedColor, 16);
        }

        private async Task<bool> IsLoadoutMenuOpen()
        {
            var screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;

            (int X, int Y) coord;
            System.Drawing.Color expectedColor = System.Drawing.Color.FromArgb(224, 224, 224); // E0 = 224 +/- tolerance (16) D0 to F0

            if (screenWidth == 2560 && screenHeight == 1440) // 1440p 16:9
            {
                coord = (102, 139);
            }
            else if (screenWidth == 1920 && screenHeight == 1080) // 1080p, good for 16:9 NOT ultrawide 64/27
            {
                coord = (77, 104);
            }
            // ultra wide 1080
            else if (screenWidth == 2560 && screenHeight == 1080) // ultrawide 1080
            {
                coord = (425, 1049);
            }

            else if (screenWidth == 1920 && screenHeight == 1200) // 8:5 1200p
            {
                coord = (107, 1102);
            }
            else // Default to 1080p
            {
                coord = (77, 104);
            }
            // TODO: Add other resolutions as needed

            var pixel = GetPixelColor(coord.X, coord.Y);

            return IsColorSimilar(pixel, expectedColor, 16);
        }

        private bool IsColorSimilar(System.Drawing.Color c1, System.Drawing.Color c2, int tolerance)
        {
            return Math.Abs(c1.R - c2.R) <= tolerance &&
                   Math.Abs(c1.G - c2.G) <= tolerance &&
                   Math.Abs(c1.B - c2.B) <= tolerance;
        }

        private System.Drawing.Color GetPixelColor(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);
            return System.Drawing.Color.FromArgb(
                (int)(pixel & 0x000000FF),
                (int)(pixel & 0x0000FF00) >> 8,
                (int)(pixel & 0x00FF0000) >> 16);
        }

        private async Task CloseInventoryUI()
        {
            Debug.WriteLine($"{Name}: Closing inventory");
            await SimulateKeyPress((int)Keycode.VK_F1);
        }

        private async Task PerformLoadoutClicking(List<int> enabledLoadouts, int duration, int delay)
        {
            Debug.WriteLine($"{Name}: Starting loadout clicking for {duration}ms with {delay}ms delay");

            var stopwatch = Stopwatch.StartNew();
            int currentLoadoutIndex = 0;
            var coordinatesMap = GetLoadoutCoordinatesMap(); // Get all coordinates once

            while (stopwatch.ElapsedMilliseconds < duration)
            {
                var loadoutNumber = enabledLoadouts[currentLoadoutIndex];
                if (coordinatesMap.TryGetValue(loadoutNumber, out var coords))
                {
                    await SimulateMouseClick(coords.X, coords.Y);
                }

                currentLoadoutIndex = (currentLoadoutIndex + 1) % enabledLoadouts.Count;

                if (stopwatch.ElapsedMilliseconds + delay < duration)
                {
                    await Task.Delay(delay);
                }
            }
        }

        private async Task ClickEndingLoadout(int endingLoadout)
        {
            Debug.WriteLine($"{Name}: Clicking ending loadout {endingLoadout}");

            var coordinates = GetLoadoutCoordinates(endingLoadout);
            if (!coordinates.HasValue)
            {
                Debug.WriteLine($"{Name}: Invalid ending loadout coordinates for loadout {endingLoadout}");
                return;
            }

            // Move mouse to the ending loadout position first
            SetCursorPos(coordinates.Value.X, coordinates.Value.Y);

            // Precise delays matching your AutoHotkey script
            int[] delays = { 200, 50, 50, 10, 10, 10, 10, 10, 1, 1, 1, 1, 1 };

            for (int i = 0; i < delays.Length; i++)
            {
                await Task.Delay(delays[i]);

                // Perform left mouse button click
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

                Debug.WriteLine($"{Name}: Ending loadout click {i + 1}/{delays.Length} with {delays[i]}ms delay");
            }

            Debug.WriteLine($"{Name}: Completed ending loadout clicking sequence");
        }

        // Loadout button coordinates for different resolutions
        private (int X, int Y)? GetLoadoutCoordinates(int loadoutNumber)
        {
            var coordinates = GetLoadoutCoordinatesMap();
            return coordinates.TryGetValue(loadoutNumber, out var coords) ? coords : null;
        }

        private Dictionary<int, (int X, int Y)> GetLoadoutCoordinatesMap()
        {
            // Get screen dimensions
            var screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
            var currentResolution = (screenWidth, screenHeight);

            Debug.WriteLine($"{Name}: Screen resolution: {screenWidth}x{screenHeight}");

            // Look for an exact match in the resolution map
            if (ResolutionCoordinateMap.TryGetValue(currentResolution, out var getCoordinates))
            {
                Debug.WriteLine($"{Name}: Found coordinate map for {screenWidth}x{screenHeight}");
                var coordinates = getCoordinates();
                if (coordinates.Any())
                {
                    return coordinates;
                }
                Debug.WriteLine($"{Name}: Coordinate map for {screenWidth}x{screenHeight} is empty. Using default 1920x1080.");
            }
            else
            {
                // Fallback to a default if no exact match is found
                Debug.WriteLine($"{Name}: No exact coordinate map found for {screenWidth}x{screenHeight}. Using default 1920x1080.");
            }

            return GetCoordinates1920x1080();
        }

        private static Dictionary<int, (int X, int Y)> GetCoordinates2560x1080()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (460, 340) }, { 2, (560, 340) }, { 3, (460, 440) }, { 4, (560, 440) },
                { 5, (460, 530) }, { 6, (560, 530) }, { 7, (460, 630) }, { 8, (560, 630) },
                { 9, (460, 730) }, { 10, (560, 730) }, { 11, (460, 830) }, { 12, (560, 830) },
            };
        }

        private static Dictionary<int, (int X, int Y)> GetCoordinates2560x1440()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (190, 450) }, { 2, (320, 450) }, { 3, (190, 570) }, { 4, (320, 570) },
                { 5, (190, 690) }, { 6, (320, 690) }, { 7, (190, 810) }, { 8, (320, 810) },
                { 9, (190, 950) }, { 10, (320, 950) }, { 11, (190, 1070) }, { 12, (320, 1070) },
            };
        }

        private static Dictionary<int, (int X, int Y)> GetCoordinates1920x1080()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (140, 340) }, { 2, (240, 340) }, { 3, (140, 440) }, { 4, (240, 440) },
                { 5, (140, 530) }, { 6, (240, 530) }, { 7, (140, 630) }, { 8, (240, 630) },
                { 9, (140, 730) }, { 10, (240, 730) }, { 11, (140, 830) }, { 12, (240, 830) },
            };
        }

        private static Dictionary<int, (int X, int Y)> GetCoordinates3840x2160()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (380, 900) }, { 2, (640, 900) }, { 3, (380, 1140) }, { 4, (640, 1140) },
                { 5, (380, 1380) }, { 6, (640, 1380) }, { 7, (380, 1620) }, { 8, (640, 1620) },
                { 9, (380, 1900) }, { 10, (640, 1900) }, { 11, (380, 2140) }, { 12, (640, 2140) },
            };
        }


        // Placeholders for unfinished resolutions
        private static Dictionary<int, (int X, int Y)> GetCoordinates2048x1280() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1920x1440() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1920x1200()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (140, 450) }, { 2, (240, 450) }, { 3, (140, 560) }, { 4, (240, 560) },
                { 5, (140, 650) }, { 6, (240, 650) }, { 7, (140, 750) }, { 8, (240, 750) },
                { 9, (140, 850) }, { 10, (240, 850) }, { 11, (140, 950) }, { 12, (240, 950) },
            };
        }
        private static Dictionary<int, (int X, int Y)> GetCoordinates1680x1050() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1600x1200() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1600x1024() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1600x900()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (116, 283) }, { 2, (216, 283) }, { 3, (116, 383) }, { 4, (216, 383) },
                { 5, (116, 473) }, { 6, (216, 473) }, { 7, (116, 573) }, { 8, (216, 573) },
                { 9, (116, 673) }, { 10, (216, 673) }, { 11, (116, 773) }, { 12, (216, 773) },
            };
        }
        private static Dictionary<int, (int X, int Y)> GetCoordinates1440x1080() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1440x900() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1366x768() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1360x768() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1280x1440() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1280x1024()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (95, 520) }, { 2, (160, 520) }, { 3, (95, 590) }, { 4, (95, 590) },
                { 5, (95, 660) }, { 6, (160, 660) }, { 7, (95, 720) }, { 8, (95, 720) },
                { 9, (95, 785) }, { 10, (160, 785) }, { 11, (95, 823) }, { 12, (95, 823) },
            };
        }
        private static Dictionary<int, (int X, int Y)> GetCoordinates1280x960() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1280x800() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1280x768() => new();
        private static Dictionary<int, (int X, int Y)> GetCoordinates1280x720()
        {
            return new Dictionary<int, (int X, int Y)>
            {
                { 1, (95, 225) }, { 2, (160, 225) }, { 3, (95, 285) }, { 4, (160, 285) },
                { 5, (95, 345) }, { 6, (160, 345) }, { 7, (95, 405) }, { 8, (160, 405) },
                { 9, (95, 475) }, { 10, (160, 475) }, { 11, (95, 535) }, { 12, (160, 535) },
            };
        }

        private async Task SimulateMouseClick(int x, int y)
        {
            SetCursorPos(x, y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        private async Task SimulateKeyPress(int vkCode)
        {
            keybd_event((byte)vkCode, 0, 0, UIntPtr.Zero);
            keybd_event((byte)vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // Helper methods
        private int GetIntFromConfig(Dictionary<string, object> settings, string key, int defaultValue)
        {
            if (settings.TryGetValue(key, out var value))
            {
                if (value is int intValue) return intValue;
                if (int.TryParse(value.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        private bool GetBoolFromConfig(Dictionary<string, object> settings, string key, bool defaultValue)
        {
            if (settings.TryGetValue(key, out var value))
            {
                if (value is bool boolValue) return boolValue;
                if (value is System.Text.Json.JsonElement jsonElement)
                {
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                }
            }
            return defaultValue;
        }

        // Win32 API declarations
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
    }
}