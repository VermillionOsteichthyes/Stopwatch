using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.VisualBasic.ApplicationServices;

using redfish.Database;
using redfish.Utility;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Xml.Linq;

using static System.Windows.Forms.AxHost;

using Application = System.Windows.Application;

namespace redfish
{
    public partial class StartupProgressBar : Window
    {
        public static StartupProgressBar Instance { get; private set; }
        public IdentityChecker Checker;

        public StartupProgressBar()
        {
            AppDomain.CurrentDomain.UnhandledException += new System.UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
            {
                try
                {
                    //Logger.Fatal(e.ExceptionObject.ToString());
                    //ExtraLogger.Error(e.ExceptionObject as Exception, "Unhandled");
                }
                catch { }
                System.Windows.MessageBox.Show(e.ExceptionObject.ToString());

                Close();
                Application.Current.Shutdown();
            }

            InitializeComponent();
            Instance = this;

            Config.Load();

            login.Text = string.IsNullOrWhiteSpace(Config.Instance.Username) ? "Username" : Config.Instance.Username;
            login.GotFocus += (e, a) => login.Text = login.Text == "Username" ? string.Empty : login.Text;
            login.LostFocus += (e, a) => login.Text = string.IsNullOrEmpty(login.Text) ? "Username" : login.Text;

            password.Password = string.IsNullOrWhiteSpace(Config.Instance.Password) ? "" : Config.Instance.Password;
            passwordWatermark.Visibility = string.IsNullOrWhiteSpace(password.Password) ? Visibility.Visible : Visibility.Hidden;
            password.GotFocus += (e, a) => passwordWatermark.Visibility = Visibility.Hidden;
            password.LostFocus += (e, a) => passwordWatermark.Visibility = string.IsNullOrWhiteSpace(password.Password) ? Visibility.Visible : Visibility.Hidden;

            key.GotFocus += (e, a) => keyWatermark.Visibility = Visibility.Hidden;
            key.LostFocus += (e, a) => keyWatermark.Visibility = string.IsNullOrWhiteSpace(key.Password) ? Visibility.Visible : Visibility.Hidden;

            Checker = new IdentityChecker();
            LoginButton.IsEnabled = false;

            Loaded += StartupProgressBar_Loaded;
        }

        private async void StartupProgressBar_Loaded(object sender, RoutedEventArgs e)
        {
            Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Connecting");

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += async (s, e) =>
            {
         

                Instance.Dispatcher.Invoke(() =>
                {
                    if (Instance.login.Text != "Username" && !string.IsNullOrWhiteSpace(Instance.password.Password))
                    {
                        TaskDescription.Content = "Logging in from config";
                        var l = Instance.login.Text.Trim();
                        var p = Instance.password.Password.Trim();
                        BackgroundWorker worker2 = new BackgroundWorker();
                        worker2.DoWork += async (s, e) =>
                        {
                            //     Checker.AuthApp.login(l, p);
                            Instance.Dispatcher.Invoke(() =>
                            {
                               
                                start ??= Start();
                            
                            });
                        };
                        worker2.RunWorkerAsync();
                    }
                    else
                    {
                        LoginButton.IsEnabled = true;
                        TaskDescription.Content = "Login";
                    }
                });
            };

            worker.RunWorkerAsync();
        }

        public static Task start;
        async Task Start()
        {
            BackgroundWorker worker = new BackgroundWorker();

            worker.DoWork += async (s, e) =>
            {

                bool Update()
                {
                    Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Downloading an update");
                    for (int i = 0; i < 5; i++)
                    {
                        //if (Updater.Update())
                        //{
                        //    break;
                        //}

                        Instance.Dispatcher.Invoke(() => TaskDescription.Content = $"Failed to update, trying again #{i + 1}");
                        if (i == 4)
                        {
                            Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Update failed");
                            return false;
                        }
                    }
                    return true;
                }

                try
                {
                    var p = Process.GetProcessesByName("patcher.exe");
                    if (p.Any())
                    {
                        Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Waiting for patcher");
                        p.First().WaitForExit();
                    }

                    Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Cleaning up temp files");
                    using var db = new redfishDbContext();
                    var date = DateTime.Now - TimeSpan.FromHours(24);
                    db.Packets.Where(x => x.CreatedAt < date).ExecuteDelete();
                    db.Log.Where(x => x.CreatedAt < date).ExecuteDelete();
                    db.Database.ExecuteSql(System.Runtime.CompilerServices.FormattableStringFactory.Create("VACUUM;"));

                    if (Directory.Exists("temp"))
                        Directory.Delete("temp", true);
                    if (File.Exists("sw_update.zip"))
                        File.Delete("sw_update.zip");

                    // MIGRATIONS
                    if (File.Exists("activation.mp3") && !File.Exists("activate.mp3"))
                        File.Move("activation.mp3", "activate.mp3", true);

                    var snd = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "Sound"));
                    foreach (var sound in Directory.GetFiles(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "*.mp3"))
                    {
                        File.Move(sound, Path.Combine(snd.FullName, Path.GetFileName(sound)), true);
                    }

                    Checker.CheckSubs();
                    Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Checking for updates");
                    //if (!Updater.IsLatest())
                    //{
                    //    Update();
                    //    Instance.Dispatcher.Invoke(() =>
                    //    {
                    //        Process.GetCurrentProcess().CloseMainWindow();
                    //        Application.Current.Shutdown();
                    //    });
                    //    return;
                    //}

                    Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Checking the executable");
                    //if (!Updater.IsProtected())
                    //{
                    //    Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Patching");

                    //    Updater.RunPatcher(Process.GetCurrentProcess().MainModule.FileName);

                    //    Instance.Dispatcher.Invoke(() =>
                    //    {
                    //        Process.GetCurrentProcess().CloseMainWindow();
                    //        Application.Current.Shutdown();
                    //    });

                    //    return;
                    //}

                    //Instance.Dispatcher.Invoke(() => TaskDescription.Content = $"Welcome {Checker.Name} :)");
                    Instance.Dispatcher.Invoke(() => TaskDescription.Content = "Freaky Mode Engaged");
					await Task.Delay(1250);

                    if (Checker.Type != IdentityChecker.AccessType.Debug)
                    {
                        Config.Instance.Settings.DB_KeyPresses = false;
                        Config.Instance.Settings.DB_SavePackets = false;
                    }

                    Instance.Dispatcher.Invoke(new Action(() =>
                    {
                        try
                        {
                            var main = new MainWindow(Checker)
                            {
                                Left = this.Left,
                                Top = this.Top
                            };
                            main.Show();

                            Instance.Visibility = Visibility.Collapsed;
                        }
                        catch (Exception e)
                        {
                            //ExtraLogger.Error(e);
                            throw;
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Instance.Dispatcher.Invoke(() =>
                    {
                        //ExtraLogger.Error(ex, "Start");
                        Debug.WriteLine($"ProgressBar: {ex}");

                        var a = System.Windows.MessageBox.Show($"Startup errored with {ex.GetType()}\nContact developer if issue persists");
                        Process.GetCurrentProcess().CloseMainWindow();
                        Application.Current.Shutdown();
                    });
                }
            };

            worker.RunWorkerAsync();
        }

        private void ExitButtonClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LoginClick(object sender, RoutedEventArgs e)
        {
            // TODO: validation for non english characters
            var usr = login.Text.Trim();
            var pw = password.Password.Trim();

       

            Config.Instance.Username = usr;
            Config.Instance.Password = pw;

            if (Checker.TimeLeft.TotalSeconds > 0)
            {
                start ??= Start();
            }
       
        }

        private void RegisterClick(object sender, RoutedEventArgs e)
        {
            // TODO: validation for non english characters
            if (KeyPanel.Visibility == Visibility.Collapsed)
            {
                KeyPanel.ElementAppear();
                RedeemButton.ElementDisappear();
                return;
            }

            var usr = login.Text.Trim();
            var pw = password.Password.Trim();
            var k = key.Password.Trim();


                    Config.Instance.Username = usr;
                    Config.Instance.Password = pw;

                    if (Checker.TimeLeft.TotalSeconds > 0)
                    {
                        start ??= Start();
                    }
        

        }

        private void RedeemClick(object sender, RoutedEventArgs e)
        {
            RedeemButton.ElementDisappear();
            KeyPanel.ElementDisappear();

            var usr = login.Text.Trim();
            var pw = password.Password.Trim();

            Config.Instance.Username = usr;
            Config.Instance.Password = pw;

            if (Checker.TimeLeft.TotalSeconds > 0)
            {
                start ??= Start();
            }

        }
    }
}
