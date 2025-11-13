using Hardcodet.Wpf.TaskbarNotification;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.Logging;

using vermillion.Controls;
using vermillion.Database;
using vermillion.Interception;
using vermillion.Interception.Modules;
using vermillion.Models;
using vermillion.Utility;
using vermillion.Windows;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;



using WindivertDotnet;

using Application = System.Windows.Application;

namespace vermillion
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }
        public IdentityChecker Checker { get; private set; }
        public DateTime AppStart = DateTime.Now;
        TimeSpan animationTime = TimeSpan.FromSeconds(0.5);

        public ObservableCollection<EnabledModuleTimer> DisplayModules { get; set; } = new ObservableCollection<EnabledModuleTimer>();
        KeyListener inputListener { get; set; }

        public string CurrentModuleName => Config.Instance.CurrentModule;
        private PacketModuleBase CurrentModule => InterceptionManager.GetModule(CurrentModuleName);
        private List<Keycode> Keybind => Config.GetNamed(CurrentModuleName).Keybind;

        private ServiceWindow _serviceWindow;
        private AhkManagerWindow _ahkManagerWindow;
        private SwapWindow _swapWindow;

        public OverlayWindow overlay { get; set; }

        public void KeyLogger(LinkedList<Keycode> keycodes) => Debug.WriteLine(String.Join(" + ", keycodes.Select(x => x.ToString().Replace("VK_", ""))));

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        public MainWindow(IdentityChecker auth)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += FirstChanceExceptionHandler;
            AppDomain.CurrentDomain.ProcessExit += ProcessExitHandler;

            Debug.WriteLine(App.ExeDirectory);

            Checker = auth;
            // DEBUG
            //Task.Run(() => ExtraLogger.Login());

            Instance = this;
            DataContext = this;
            Title = Checker.Name;
            InitializeComponent();
            Debug.WriteLine("App started");

            inputListener = new KeyListener();
            if (Config.Instance.Settings.DB_KeyPresses)
                KeyListener.KeysPressed += KeyLogger;

            KeyListener.KeysPressed += AltTabTracker;
            KeyListener.KeysPressed += GlobalKeyHook_KeyPressed; // Add global key hook
            InterceptionManager.Init();
            AhkManager.Init();

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                StartupProgressBar.Instance.Close();

                float getBrightness(System.Drawing.Color c)
                {
                    return (c.R * 0.299f + c.G * 0.587f + c.B * 0.114f) / 256f;
                }

                CurrentTime.Content = DateTime.Now.ToString("hh:mm:ss");

                if (!Config.Instance.Settings.Window_DisplayClock)
                    CurrentTime.Visibility = Visibility.Collapsed;
                if (!Config.Instance.Settings.Window_DisplaySpeed)
                    Speed.Visibility = Visibility.Collapsed;
                //if (Checker.Type < IdentityChecker.AccessType.Debug)
                //    OpenLogs.Visibility = Visibility.Collapsed;
                if (Config.Instance.Settings.Overlay_StartOnLaunch)
                {
                    OverlayClick(null, null);
                    Overlay.Border_MouseEnter(this, null);
                    Overlay.Border_MouseLeave(this, null);
                }


                var accent = System.Windows.Application.Current.Resources["AccentColor"] as SolidColorBrush;

                System.Drawing.Color color = System.Drawing.Color.FromArgb(accent.Color.R, accent.Color.G, accent.Color.B);
                float hue = color.GetHue();
                float saturation = color.GetSaturation();
                float lightness = getBrightness(color) - 0.425f;
                var dimmedAccent = new SolidColorBrush(ExtensionMethods.ColorFromHSL(hue, saturation, lightness));

                int i = 0;
                int j = 0;
                foreach (var m in InterceptionManager.Modules)
                {
                    if (m.Name == "Swap")
                        continue;

                    var button = new WindowControlButton()
                    {
                        //GlowColor = m.Color,
                        FillColor = dimmedAccent,
                        GlowColor = accent,
                        PathData = m.Icon,
                        Name = m.Name.Replace(" ", "_"),
                        stayActive = m.IsEnabled,
                        Text = m.DisplayName // <-- Set display name for button
                    };

                    button.Click += NewModuleClicked;
                    RegisterName(button.Name, button);
                    ModuleSelection.Children.Add(button);
                    Grid.SetRow(button, j);
                    Grid.SetColumn(button, i);

                    i++;
                    if (i == 4)
                    {
                        i = 0;
                        j++;
                    }
                }

                VolumeSlider.Value = Config.Instance.Volume;
                UpdateSelectedModule();

                var t = new DispatcherTimer();
                t.Tick += WindowTick;
                t.Interval = TimeSpan.FromSeconds(0.5);
                t.Start();

                var l = new DispatcherTimer();
                l.Tick += (s, a) => Task.Run(async () =>
                {
                    //   Checker.AuthApp.check();
                    var a = Checker.Calc;
                });
                l.Interval = TimeSpan.FromSeconds(120);
                l.Start();
            });
        }

        private void GlobalKeyHook_KeyPressed(LinkedList<Keycode> keycodes)
        {
            // Don't fire keybinds if game isn't focused
            if (!OverlayWindow.CheckGameFocus() && Config.Instance.Settings.AltTabSupressKeybinds) return;

            // Check if PVE buffering keybind is pressed
            if (PveModule.BufferKeybind.Any() &&
                PveModule.BufferKeybind.All(bufferKey => keycodes.Contains(bufferKey)))
            {
                Debug.WriteLine("PVE Buffer keybind matched!");
                Dispatcher.Invoke(() => TogglePveBuffer());
                return;
            }

            //Check if PVE AutoResync keybind is pressed
            if (PveModule.AutoResyncKeybind.Any() &&
                PveModule.AutoResyncKeybind.All(resyncKey => keycodes.Contains(resyncKey)))
            {
                Debug.WriteLine("PVE AutoResync keybind matched!");
                Dispatcher.Invoke(() => TogglePveResync());
                return;
            }

            // UPDATED: Check all swap profile keybinds
            for (int i = 0; i < SwapModule.Profiles.Count; i++)
            {
                var profile = SwapModule.Profiles[i];
                if (profile.Keybind.Any() &&
                    keycodes.Count >= profile.Keybind.Count &&
                    profile.Keybind.All(swapKey => keycodes.Contains(swapKey)))
                {
                    int profileIndex = i; // Capture for closure
                    Debug.WriteLine($"Swap profile '{profile.Name}' keybind matched!");
                    Dispatcher.Invoke(() =>
                    {
                        var swapModule = InterceptionManager.GetModule("Swap") as SwapModule;
                        if (swapModule != null)
                        {
                            Debug.WriteLine($"Executing swap profile '{profile.Name}' from global key hook");
                            swapModule.ExecuteSwap(profileIndex);
                        }
                    });
                    return; // Only execute first matching profile
                }
            }

            // Add other global keybind checks here
        }

        private void TogglePveBuffer()
        {
            if (CurrentModule is PveModule)
            {
                PveModule.Buffer = !PveModule.Buffer;
                PveBufferCB.SetState(PveModule.Buffer);

                // Save the setting to config
                Config.GetNamed("PVE").Settings["Buffer"] = PveModule.Buffer;
                Config.Save();

                PveBufferCBClick(PveBufferCB, new RoutedEventArgs());
            }
        }

        private void TogglePveResync()
        {
            if (CurrentModule is PveModule)
            {
                PveModule.AutoResync = !PveModule.AutoResync;
                PveResyncCB.SetState(PveModule.AutoResync);

                // Save the setting to config
                Config.GetNamed("PVE").Settings["AutoResync"] = PveModule.AutoResync;
                Config.Save();
                PveResyncCBClick(PveResyncCB, new RoutedEventArgs());
            }
        }

        private void AltTabTracker(LinkedList<Keycode> keycodes)
        {
            if (keycodes.Contains(Keycode.VK_LWIN) || (keycodes.Contains(Keycode.VK_LALT) && keycodes.Contains(Keycode.VK_TAB)))
            {
                OverlayWindow.CheckGameFocus(true);
            }
        }

        private void ProcessExitHandler(object? sender, EventArgs e)
        {
            try
            {
                AhkManager.ReloadAhksFromDirectory();
                Config.Instance.LastOpenAhks.Clear();
                Config.Instance.LastOpenAhks.AddRange(AhkManager.Ahks.Where(x => x.Value > 0).Select(x => x.Key));
                Config.Save();
                if (Config.Instance.Settings.AHK_AutoClose)
                {
                    foreach (var ahk in Config.Instance.LastOpenAhks)
                        AhkManager.TryStopAhk(ahk);
                }
                //   Checker.AuthApp.logout();
            }
            catch
            {
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            //ExtraLogger.Error(e.ExceptionObject as Exception, "Unhandled");
        }

        private void FirstChanceExceptionHandler(object sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is TaskCanceledException tc)
            {
                Debug.WriteLine($"Task cancelled");
                return;
            }
            Debug.WriteLine($"{e.Exception.GetType().Name}: {e.Exception.StackTrace}");
        }

        private void WindowTick(object? sender, EventArgs e)
        {
            // MODULES
            try
            {
                var modules = InterceptionManager.Modules.Where(x => x.Togglable);
                var pairs = modules.Select(x => (module: x, display: DisplayModules.FirstOrDefault(y => y.ModuleName == x.Name))).ToArray();

                foreach (var p in pairs)
                {
                    var display = p.display;
                    if (p.module.IsActivated && display is null)
                    {   // New active found
                        var d = new EnabledModuleTimer(p.module.DisplayName) { Visibility = Visibility.Collapsed };
                        DisplayModules.Add(d);
                        Dispatcher.BeginInvoke(() => d.ElementAppear());
                    }

                    if (display is not null)
                    {
                        display.UpdateTimer();

                        if (!p.module.IsActivated && DateTime.Now - p.module.StartTime > TimeSpan.FromSeconds(Config.Instance.Settings.Window_TimerDecaySeconds))
                        {
                            Dispatcher.BeginInvoke(async () =>
                            {
                                display.ElementDisappear();
                                await Task.Delay(animationTime);
                                DisplayModules.Remove(display);
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Poll modules: {ex}");
            }

            // OTHER FUNCS
            try
            {
                CurrentTime.Content = DateTime.Now.ToString("HH:mm:ss");
                OverlayWindow.CheckGameFocus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Main poll: {ex}");
            }
        }

        private void UpdateSelectedModule()
        {
            var targetModule = CurrentModuleName;

            if (CurrentModule is null)
            {
                Config.Instance.CurrentModule = targetModule = InterceptionManager.Modules.First().Name;
            }

            SelectedModuleLabel.Content = CurrentModule.DisplayName;
            SelectedModuleButton.PathData = CurrentModule.Icon;
            SelectedModuleButton.GlowColor = CurrentModule.Color;
            SelectedModuleButton.RefreshAppearance(null, null);

            Description.Text = CurrentModule.Description;

            KeybindButton.Text = Keybind.Any()
                ? String.Join(" + ", Keybind.Select(x => x.ToString().Replace("VK_", "")))
                : "No keybind";
            ModuleCheckbox.SetState(Config.GetNamed(targetModule).Enabled);

            if (CurrentModule is PveModule)
            {
                PveInCB.SetState(PveModule.Inbound);
                PveOutCB.SetState(PveModule.Outbound);
                PveSlowInCB.SetState(PveModule.SlowInbound);
                PveSlowOutCB.SetState(PveModule.SlowOutbound);

                PveResyncCB.SetState(PveModule.AutoResync);
                PveBufferCB.SetState(PveModule.Buffer);

                PveOutbound.Text = PveModule.OutboundKeybind.Any()
                    ? String.Join(" + ", PveModule.OutboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
                PveSlowInbound.Text = PveModule.SlowInboundKeybind.Any()
                    ? String.Join(" + ", PveModule.SlowInboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                PveSlowOutbound.Text = PveModule.SlowOutboundKeybind.Any()
                    ? String.Join(" + ", PveModule.SlowOutboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
                PveResync.Text = PveModule.AutoResyncKeybind.Any()
                    ? String.Join(" + ", PveModule.AutoResyncKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
                PveBuffer.Text = PveModule.BufferKeybind.Any()
                    ? String.Join(" + ", PveModule.BufferKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                PVE_Panel.Visibility = PveInCB.Visibility = Visibility.Visible;
            }
            else
            {
                kbd.Content = "Keybind";
                ActivationGrid.ToolTip = null;
                PVE_Panel.Visibility = PveInCB.Visibility = Visibility.Collapsed;
            }



            if (CurrentModule is PvpModule)
            {
                PvpInCB.SetState(PvpModule.Inbound);
                PvpOutCB.SetState(PvpModule.Outbound);
                PvpResyncCB.SetState(PvpModule.AutoResync);
                PvpBufferCB.SetState(PvpModule.Buffer);

                PvpOutbound.Text = PvpModule.OutboundKeybind.Any()
                    ? String.Join(" + ", PvpModule.OutboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                PVP_Panel.Visibility = PvpInCB.Visibility = Visibility.Visible;
            }
            else
            {
                kbd.Content = "Keybind";
                ActivationGrid.ToolTip = null;
                PVP_Panel.Visibility = PvpInCB.Visibility = Visibility.Collapsed;
            }



            // мега костыль, как и вся функция в принципе, переделать бы через страницы
            if (CurrentModule is PvpModule)
            {
                kbd.Content = "DL";
                ActivationGrid.ToolTip = "Block info sent by players";
            }
            if (CurrentModule is PveModule)
            {
                kbd.Content = "DL";
                ActivationGrid.ToolTip = "Block info sent by server";
            }



            if (CurrentModule is MultishotModule ms)
            {
                MS_MaxTimeSlider.Value = MultishotModule.MaxTime;
                MS_DETECT.SetState(MultishotModule.WaitShot);
                MS_PVP.SetState(MultishotModule.PlayersMode);
                MS_INBOUND.SetState(MultishotModule.Inbound);
                MS_OUTBOUND.SetState(MultishotModule.Outbound);
                MS_TOGGLABLE.SetState(ms.Togglable);

                MS_PVPKeybind.Text = MultishotModule.PlayersKeybind.Any()
                    ? String.Join(" + ", MultishotModule.PlayersKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                MULTISHOT_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                MULTISHOT_Panel.Visibility = Visibility.Collapsed;
            }



            if (CurrentModule is ApiModule)
            {
                API_Disable.SetState(ApiModule.Disable);
                API_Buffer.SetState(ApiModule.Buffer);
                API_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                API_Panel.Visibility = Visibility.Collapsed;
            }

            if (CurrentModule is InstanceModule)
            {
                InstBufferCB.SetState(InstanceModule.Buffer);
                InstSlowCB.SetState(InstanceModule.RateLimitingEnabled);
                InstRateLimitTB.Text = InstanceModule.TargetBytesPerSecond.ToString();
                INSTANCE_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                INSTANCE_Panel.Visibility = Visibility.Collapsed;
            }
        }

        // Keybind logic
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
                    if (button == KeybindButton)
                        listening.Add(button, Keybind);
                    else if (button == PveOutbound)
                        listening.Add(button, PveModule.OutboundKeybind);
                    else if (button == PveSlowInbound)
                        listening.Add(button, PveModule.SlowInboundKeybind);
                    else if (button == MS_PVPKeybind)
                        listening.Add(button, MultishotModule.PlayersKeybind);
                    else if (button == PvpOutbound)
                        listening.Add(button, PvpModule.OutboundKeybind);
                    else if (button == PveSlowOutbound)
                        listening.Add(button, PveModule.SlowOutboundKeybind);
                    else if (button == PveBuffer)
                        listening.Add(button, PveModule.BufferKeybind);
                    else if (button == PveResync)
                        listening.Add(button, PveModule.AutoResyncKeybind);

                    //button.Background = new SolidColorBrush(Color.FromArgb(0x88, 0xD9, 0xCC, 0xD9));
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
                    //button.Background = Application.Current.Resources["InactiveColor"] as SolidColorBrush;
                    if (listening.Count == 0)
                    {
                        InterceptionManager.Modules.ForEach(x => x.HookKeybind());
                        KeyListener.KeysPressed -= ListeningNewKeybind;
                    }
                    button.ButtonBorder.BorderThickness = new Thickness(0);
                    button.ButtonBorder.BorderBrush = Brushes.Transparent;
                    button.ButtonBorder.Effect = null;

                    Config.Save();
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

        private void InstRateLimitTB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CurrentModule is InstanceModule)
            {
                if (long.TryParse(InstRateLimitTB.Text.Trim(), out long val) && val > 0)
                {
                    InstanceModule.TargetBytesPerSecond = val;
                    Config.GetNamed("З0k").Settings["TargetBytesPerSecond"] = val;
                    Config.Save();

                    Debug.WriteLine($"Rate limit set to: {val} bytes/second");
                }
            }
        }
    }
}