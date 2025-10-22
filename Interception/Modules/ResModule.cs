﻿using redfish.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Diagnostics;

namespace redfish.Interception.Modules
{
    public class ResModule : PacketModuleBase
    {
        PacketProviderBase provider;
        public ResModule() : base("Revive", false, InterceptionManager.GetProvider("Players"))
        {
            Description =
@"Skips revive confirmation
Doesn't take revive token in some activities
Bind with interaction key
Disables itself";
            provider = InterceptionManager.GetProvider("Players");
        }

        public override void Toggle()
        {
            IsActivated = !IsActivated;

            if (IsActivated)
            {
                StartTime = DateTime.Now;
                disable = null;
                blacklist.Clear();
                highest = DateTime.MinValue;
                cts = new CancellationTokenSource();
            }
            else
            {
                cts.Cancel();
                blacklist.Clear();
            }
        }

        Task disable;
        CancellationTokenSource cts;
        TimeSpan delay = TimeSpan.FromSeconds(2.5);
        DateTime highest = DateTime.MinValue;
        HashSet<string> blacklist = new ();
        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;

            if (!IsActivated) return true;

            var r = p.RemoteAddress;
            var black = blacklist.Contains(r);
            var any = blacklist.Any();

            // long time no revive
            if (DateTime.Now - StartTime > TimeSpan.FromSeconds(10) && !any)
            {
                ForceDisable();
                Debug.WriteLine($"{Name}: Disabled after timeout");
                return true;
            }

            if (p.Outbound) return true;// return !black;

            if (p.Length >= 1215 && p.Length <= 1225)
            {
                if (DateTime.Now > highest) highest = DateTime.Now;

                if (!black)
                {
                    blacklist.Add(r);
                    provider.Delay.RemoveAll(x => x.RemoteAddress == r);
                    Debug.WriteLine($"{Name}: Found at {r}");

                    disable ??= Task.Run(async () =>
                    {
                        Debug.WriteLine($"{Name}: Self disable timer start");

                        while (!cts.IsCancellationRequested)
                        {
                            var del = (highest + delay) - DateTime.Now;
                            if (del > TimeSpan.Zero) // delay passed since last revive
                            {
                                await Task.Delay(del, cts.Token);
                            }
                            else
                            {
                                ForceDisable();
                                Debug.WriteLine($"{Name}: Disabled with successful reconnect");
                            }
                        }
                    });
                }
                else Debug.WriteLine($"{Name}: Repeat at {r}");
                return false;
            }

            if (black || provider.Connections[r].IsReadonlyPlayerConnection()) return false;

            provider.DelayPacket(p, TimeSpan.FromSeconds(1));
            return false;
        }
    }
}