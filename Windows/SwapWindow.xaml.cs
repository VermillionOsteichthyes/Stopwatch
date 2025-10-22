using redfish.Controls;
using redfish.Interception;
using redfish.Interception.Modules;
using redfish.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace redfish
{
    public partial class SwapWindow : Window
    {
        private bool loaded = false;
        private int currentProfileIndex = 0;

        public SwapWindow()
        {
            Visibility = Visibility.Collapsed;
            InitializeComponent();
            MouseDown += SwapWindow_MouseDown;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => LoadSettings());
        }

        private void LoadSettings()
        {
            VersionString.Content = $"Made by VermillionOsteichthyes";
            var left = MainWindow.Instance.Checker.TimeLeft;
            if (left < TimeSpan.FromDays(400))
            {
                var month = left.Days / 30;
                if (month > 0)
                {
                    TimeString.Content = $"Expires in {month} month{(month > 1 ? "s" : "")}";
                }
                else if (left.Days > 0)
                {
                    TimeString.Content = $"Expires in {left.Days} day{(left.Days > 1 ? "s" : "")}";
                }
                else
                {
                    TimeString.Content = $"Expires in {left.Hours} hour{(left.Hours > 1 ? "s" : "")}";
                }
            }

            //User_Nickname.Content = MainWindow.Instance.Checker.Name;

            UpdateProfileButtonStates();
            LoadProfile(0);

            Visibility = Visibility.Visible;
            loaded = true;
        }

        private void UpdateProfileButtonStates()
        {
            var profileButtons = new[] { 
                Profile1Button, Profile2Button, Profile3Button, Profile4Button, 
                Profile5Button 
            };

            for (int i = 0; i < profileButtons.Length; i++)
            {
                if (profileButtons[i] != null)
                {
                    profileButtons[i].ButtonBorder.BorderThickness = new Thickness(0);
                    profileButtons[i].ButtonBorder.BorderBrush = Brushes.Transparent;
                    profileButtons[i].ButtonBorder.Effect = null;
                    profileButtons[i].ButtonBorder.Opacity = 0.7;
                }
            }

            if (currentProfileIndex >= 0 && currentProfileIndex < profileButtons.Length && profileButtons[currentProfileIndex] != null)
            {
                var activeButton = profileButtons[currentProfileIndex];
                activeButton.ButtonBorder.BorderThickness = new Thickness(1.75);
                activeButton.ButtonBorder.BorderBrush = Brushes.White;
                activeButton.ButtonBorder.Effect = new DropShadowEffect()
                {
                    ShadowDepth = 0,
                    Color = Colors.White,
                    BlurRadius = 8
                };
                activeButton.ButtonBorder.Opacity = 1.0;
            }
        }

        private void Profile1Button_Click(object sender, RoutedEventArgs e) => SwitchToProfile(0);
        private void Profile2Button_Click(object sender, RoutedEventArgs e) => SwitchToProfile(1);
        private void Profile3Button_Click(object sender, RoutedEventArgs e) => SwitchToProfile(2);
        private void Profile4Button_Click(object sender, RoutedEventArgs e) => SwitchToProfile(3);
        private void Profile5Button_Click(object sender, RoutedEventArgs e) => SwitchToProfile(4);

        // is this needed? on button click stuff should already be saving
        private void SwitchToProfile(int profileIndex)
        {
            if (currentProfileIndex != profileIndex && profileIndex >= 0 && profileIndex < SwapModule.Profiles.Count)
            {
                SaveCurrentProfile();
                LoadProfile(profileIndex);
                UpdateProfileButtonStates();
            }
        }

        private void LoadProfile(int profileIndex)
        {
            if (profileIndex < 0 || profileIndex >= SwapModule.Profiles.Count)
                return;

            currentProfileIndex = profileIndex;
            var profile = SwapModule.Profiles[profileIndex];

            Debug.WriteLine($"SwapWindow: Loading profile '{profile.Name}'");

            Duration.Text = profile.SwapDuration.ToString();
            Delay.Text = profile.SwapDelay.ToString();
            UntickDelay.Text = profile.UntickDelay.ToString();
            EndingLoadout.Text = profile.EndingLoadout.ToString();

            PortSwitch.SetState(profile.Port3074);
            Packet3074DL.SetState(profile.Packet3074DL);
            AutoDisableBuffering.SetState(profile.AutoDisableBuffering);
            CloseInventory.SetState(profile.CloseInventory);

            var checkboxes = new[] { Loadout1, Loadout2, Loadout3, Loadout4, Loadout5, Loadout6,
                               Loadout7, Loadout8, Loadout9, Loadout10, Loadout11, Loadout12 };

            for (int i = 0; i < 12; i++)
            {
                checkboxes[i].SetState(profile.LoadoutEnabled[i]);
            }

            SwapBindButton.Text = profile.Keybind.Any()
                ? String.Join(" + ", profile.Keybind.Select(x => x.ToString().Replace("VK_", "")))
                : "No keybind";

            Debug.WriteLine($"SwapWindow: Loaded profile '{profile.Name}' - {profile.Keybind.Count} keys, {profile.LoadoutEnabled.Count(x => x)} loadouts enabled");
        }


        private void SaveCurrentProfile()
        {
            if (!loaded || currentProfileIndex < 0 || currentProfileIndex >= SwapModule.Profiles.Count)
                return;

            var profile = SwapModule.Profiles[currentProfileIndex];

            Debug.WriteLine($"SwapWindow: Saving profile '{profile.Name}'");

            SwapModule.SaveProfiles();
        }

        #region Window Control Events

        private void ExitButtonClick(object sender, RoutedEventArgs e)
        {
            //SaveSettings();
            this.Close();
        }

        private void SwapWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Handle any cleanup needed when clicking outside controls
        }

        #endregion

        #region Keybind Events

        // MODIFIED: Keybind button now works with current profile
        private Dictionary<Controls.Button, List<Keycode>> listening = new();
        private DateTime lastUpdated = DateTime.MinValue;
        private SemaphoreSlim keybindSemaphore = new(1);

        private void KeybindButtonClick(object sender, RoutedEventArgs e)
        {
            if (DateTime.Now - lastUpdated > TimeSpan.FromSeconds(0.15) && keybindSemaphore.CurrentCount > 0)
            {
                var button = sender as Controls.Button;
                keybindSemaphore.Wait();
                bool listen = !listening.ContainsKey(button);

                if (listen)
                {
                    if (button == SwapBindButton)
                    {
                        // Reference the current profile's keybind
                        listening.Add(button, SwapModule.Profiles[currentProfileIndex].Keybind);
                    }

                    if (listening.Count == 1)
                    {
                        InterceptionManager.Modules.ForEach(x => x.UnhookKeybind());
                        KeyListener.KeysPressed += ListeningNewKeybind;
                    }
                    button.ButtonBorder.BorderThickness = new Thickness(1.75);
                    button.ButtonBorder.BorderBrush = Brushes.White;
                    button.ButtonBorder.Effect = new DropShadowEffect()
                    {
                        ShadowDepth = 0,
                        Color = Colors.White,
                        BlurRadius = 8
                    };
                }
                else
                {
                    listening.Remove(button);
                    if (listening.Count == 0)
                    {
                        InterceptionManager.Modules.ForEach(x => x.HookKeybind());
                        KeyListener.KeysPressed -= ListeningNewKeybind;
                    }
                    button.ButtonBorder.BorderThickness = new Thickness(0);
                    button.ButtonBorder.BorderBrush = Brushes.Transparent;
                    button.ButtonBorder.Effect = null;

                    SwapModule.SaveProfiles();
                }

                keybindSemaphore.Release();
                lastUpdated = DateTime.Now;
            }

            void ListeningNewKeybind(LinkedList<Keycode> keycodes)
            {
                if (keycodes.Count == 1 && keycodes.First.Value == Keycode.VK_LMB)
                    return;

                foreach (var b in listening.Values)
                    b.Clear();

                if (keycodes.Count == 1 && keycodes.First.Value == Keycode.VK_ESC)
                {
                    Dispatcher.Invoke(DispatcherPriority.Background, () =>
                    {
                        foreach (var b in listening.Keys)
                            b.Text = "No keybind";
                    });
                    return;
                }

                foreach (var b in listening.Values)
                    b.AddRange(keycodes);

                Dispatcher.Invoke(DispatcherPriority.Background, () =>
                {
                    try
                    {
                        foreach (var b in listening)
                            b.Key.Text = String.Join(" + ", b.Value.Select(x => x.ToString().Replace("VK_", "")));
                    }
                    catch { }
                });
            }
        }

        #endregion

        #region TextBox Events

        private void Duration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            if (ValidateNumericInput(Duration.Text.Trim(), 1))
            {
                SwapModule.Profiles[currentProfileIndex].SwapDuration = int.Parse(Duration.Text.Trim());
                SwapModule.SaveProfiles();
            }
        }

        private void Delay_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            if (ValidateNumericInput(Delay.Text.Trim(), 1))
            {
                SwapModule.Profiles[currentProfileIndex].SwapDelay = int.Parse(Delay.Text.Trim());
                SwapModule.SaveProfiles();
            }
        }

        private void EndingLoadout_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            if (ValidateNumericInput(EndingLoadout.Text.Trim(), 1, 12))
            {
                SwapModule.Profiles[currentProfileIndex].EndingLoadout = int.Parse(EndingLoadout.Text.Trim());
                SwapModule.SaveProfiles();
            }
        }

        private void UntickDelay_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!loaded) return;
            if (ValidateNumericInput(UntickDelay.Text.Trim(), 1))
            {
                SwapModule.Profiles[currentProfileIndex].UntickDelay = int.Parse(UntickDelay.Text.Trim());
                SwapModule.SaveProfiles();
            }
        }

        private void PortSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            SwapModule.Profiles[currentProfileIndex].Port3074 = PortSwitch.IsOptionA;
            SwapModule.SaveProfiles();
        }

        private void UpdateLoadoutArray()
        {
            if (!loaded) return;
            var checkboxes = new[] { Loadout1, Loadout2, Loadout3, Loadout4, Loadout5, Loadout6,
                               Loadout7, Loadout8, Loadout9, Loadout10, Loadout11, Loadout12 };

            for (int i = 0; i < 12; i++)
            {
                SwapModule.Profiles[currentProfileIndex].LoadoutEnabled[i] = checkboxes[i].Checked;
            }
            SwapModule.SaveProfiles();
        }

        private void Loadout1_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout2_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout3_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout4_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout5_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout6_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout7_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout8_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout9_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout10_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout11_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();
        private void Loadout12_Click(object sender, RoutedEventArgs e) => UpdateLoadoutArray();

        #endregion

        #region Other Checkbox Events

        private void Packet3074DL_Click(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            SwapModule.Profiles[currentProfileIndex].Packet3074DL = Packet3074DL.Checked;
            SwapModule.SaveProfiles();
        }

        private void AutoDisableBuffering_Click(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            SwapModule.Profiles[currentProfileIndex].AutoDisableBuffering = AutoDisableBuffering.Checked;
            SwapModule.SaveProfiles();
        }

        private void CloseInventory_Click(object sender, RoutedEventArgs e)
        {
            if (!loaded) return;
            SwapModule.Profiles[currentProfileIndex].CloseInventory = CloseInventory.Checked;
            SwapModule.SaveProfiles();
        }

        #endregion

        #region Helper Methods

        // Keep existing helper methods...
        private bool ValidateNumericInput(string input, int min = 0, int max = int.MaxValue)
        {
            if (int.TryParse(input, out int value))
            {
                return value >= min && value <= max;
            }
            return false;
        }

        private bool GetBoolFromConfig(Dictionary<string, object> settings, string key, bool defaultValue)
        {
            if (settings.TryGetValue(key, out var value))
            {
                if (value is bool boolValue)
                    return boolValue;
                if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.True)
                    return true;
                if (value is System.Text.Json.JsonElement jsonElement2 && jsonElement2.ValueKind == System.Text.Json.JsonValueKind.False)
                    return false;
            }
            return defaultValue;
        }
        #endregion
    }
}