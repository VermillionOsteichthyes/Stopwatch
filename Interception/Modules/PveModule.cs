﻿using DotNetBungieAPI.Models.Destiny.Components;

using redfish.Interception.PacketProviders;
using redfish.Models;
using redfish.Utility;
using redfish.Windows;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;

using WindivertDotnet;

namespace redfish.Interception.Modules
{
    public class PveModule : PacketModuleBase
    {
        public override string DisplayName => "3074";
        private PacketProviderBase xbox;
        public PveModule() : base("PVE", true, InterceptionManager.GetProvider("Xbox"))
        {
            Icon = System.Windows.Application.Current.FindResource("ArcIcon") as Geometry;
            Description = "Block game world updates [3074]";

            xbox = InterceptionManager.GetProvider("Xbox");

            KeyListener.KeysPressed += OutboundHandler;
            KeyListener.KeysPressed += SlowInboundHandler;
            KeyListener.KeysPressed += SlowOutboundHandler;

            Buffer = Config.GetNamed(Name).GetSettings<bool>("Buffer");
            AutoResync = Config.GetNamed(Name).GetSettings<bool>("AutoResync");

            OutboundKeybind.AddRange(Config.GetNamed(Name).GetSettings<List<Keycode>>("OutboundKeybind"));
            SlowInboundKeybind.AddRange(Config.GetNamed(Name).GetSettings<List<Keycode>>("SlowInboundKeybind"));
            SlowOutboundKeybind.AddRange(Config.GetNamed(Name).GetSettings<List<Keycode>>("SlowOutboundKeybind"));

            // Initialize BufferKeybind from config
            BufferKeybind.AddRange(Config.GetNamed(Name).GetSettings<List<Keycode>>("BufferKeybind"));
            AutoResyncKeybind.AddRange(Config.GetNamed(Name).GetSettings<List<Keycode>>("AutoResyncKeybind"));
        }

        public override void StartListening()
        {
            base.StartListening();
            if (Inbound || Outbound || SlowInbound)
            {
                StartTime = DateTime.Now;
                IsActivated = true;
            }
        }

        public override void StopListening()
        {
            IsActivated = false;
            base.StopListening();
        }

        
        // HANDLERS
        private void OutboundHandler(LinkedList<Keycode> keycodes)
        {
            if (!KeybindChecks()) return;

            if (!OutboundKeybind.Any() || keycodes.Count < OutboundKeybind.Count) return;

            if (OutboundKeybind.All(x => keycodes.Contains(x)))
            {
                ToggleSwitch(ref Outbound);
            }
        }
        private void SlowInboundHandler(LinkedList<Keycode> keycodes)
        {
            if (!KeybindChecks()) return;

            if (!SlowInboundKeybind.Any() || keycodes.Count < SlowInboundKeybind.Count) return;

            if (SlowInboundKeybind.All(x => keycodes.Contains(x)))
            {
                ToggleSwitch(ref SlowInbound);
            }
        }
        private void SlowOutboundHandler(LinkedList<Keycode> keycodes)
        {
            if (!KeybindChecks()) return;

            if (!SlowOutboundKeybind.Any() || keycodes.Count < SlowOutboundKeybind.Count) return;

            if (SlowOutboundKeybind.All(x => keycodes.Contains(x)))
            {
                ToggleSwitch(ref SlowOutbound);
            }
        }



        // SWITCHES
        public static bool Inbound = false;
        public static bool Outbound = false;
        public static bool SlowInbound = false;
        public static bool SlowOutbound = false;
        public static bool Buffer;
        public static bool AutoResync;

        public static List<Keycode> SlowInboundKeybind = new List<Keycode>();
        public static List<Keycode> SlowOutboundKeybind = new List<Keycode>();
        public static List<Keycode> OutboundKeybind = new List<Keycode>();

        // ADD THIS: BufferKeybind property
        public static List<Keycode> BufferKeybind = new List<Keycode>();
        public static List<Keycode> AutoResyncKeybind = new List<Keycode>();
        public void ToggleSwitch(ref bool target, bool? enable = null)
        {
            var currentIn = Inbound;
            var currentOut = Outbound;
            var currentSlowIn = SlowInbound;
            var currentSlowOut = SlowOutbound;

            var old = target;
            target = enable ?? !target;
            if (old != target)
            {
                if (target && !IsActivated)
                {
                    IsActivated = true;
                    StartTime = DateTime.Now;
                }
                else if (!Outbound && !Inbound && !SlowInbound && !SlowOutbound && IsActivated)
                {
                    IsActivated = false;
                    StartTime = DateTime.Now;
                    xbox.ClearDelayQueue(null, Buffer, Buffer ? 0 : 0); // 1000 / 20 / 3
                }

                if (!currentIn && Inbound) SlowInbound = false;
                if (!currentOut && Outbound) SlowOutbound = false;
                if (!currentSlowIn && SlowInbound) Inbound = false;
                if (!currentSlowOut && SlowOutbound) Outbound = false;

                var save = target;
                MainWindow.Instance.Dispatcher.BeginInvoke(() =>
                {
                    MainWindow.Instance.PveInCB.SetState(Inbound);
                    MainWindow.Instance.PveSlowInCB.SetState(SlowInbound);
                    MainWindow.Instance.PveOutCB.SetState(Outbound);
                    MainWindow.Instance.PveSlowOutCB.SetState(SlowOutbound);
                    (save ? EnableSound : DisableSound).Play();
                });
            }
        }

        public override void Toggle()
        {
            ToggleSwitch(ref Inbound);
        }

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;

            if (!IsActivated) return true;

            if (p.IsPveQuestionableReconnectRequest()) return false;

            if (p.Inbound)
            {
                if (Inbound)
                {
                    if (Buffer) xbox.DelayPacket(p, TimeSpan.FromSeconds(35));
                    return false;
                }
                if (SlowInbound)
                {
                    xbox.DelayPacket(p, TimeSpan.FromSeconds(1), true, true);
                    return false;
                }
            }
            else 
            {
                if (Outbound)
                {
                    if (Buffer) xbox.DelayPacket(p, TimeSpan.FromSeconds(35));
                    return false;
                }
                if (SlowOutbound)
                {
                    xbox.DelayPacket(p, TimeSpan.FromSeconds(1), true, true);
                    return false;
                }
            }

            return true;
        }
    }
}