using Microsoft.EntityFrameworkCore;

using vermillion.Database;
using vermillion.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;

using WindivertDotnet;

namespace vermillion.Interception.Modules
{
    public class WeaselModule : PacketModuleBase
    {
        PacketProviderBase _7500;
        public WeaselModule() : base("Weasel", false, InterceptionManager.GetProvider("7500"))
        {
            IsActivated = false;
            Icon = System.Windows.Application.Current.FindResource("WeaselIcon") as Geometry ?? Icon;
            Description =
@"Instant weasel error
Simulates a 7500 port disconnect";

            _7500 = PacketProviders.First();
        }

        public override void Toggle()
        {
            IsActivated = true;
            StartTime = DateTime.Now;

            foreach (var addr in _7500.Connections.Keys.ToArray())
            {
                try
                {
                    if (_7500.Connections.TryGetValue(addr, out var q) && q is not null && DateTime.Now - q.LastOrDefault()?.CreatedAt < TimeSpan.FromSeconds(10))
                        Inject(addr);

                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
                
            }

            Task.Run(async () =>
            {
                await Task.Delay(150);
                IsActivated = false;
            });
        }

        unsafe void Inject(string addr)
        {
            var con = _7500.Connections[addr];
            var out_example = con.LastOrDefault(x => !x.Inbound && x.Length != 0);
            var in_example = con.LastOrDefault(x => x.Inbound && x.Length != 0);

            if (out_example is null || in_example is null)
            {
                Debug.WriteLine($"{Name}: Can't kill {addr}");
                return;
            }

            #region rst
            var p1 = out_example.BuildSameDirection();
            p1.ParseResult.TcpHeader->Rst = true;
            p1.Recalc();
            _7500.StorePacket(p1);
            _7500.SendPacket(p1, true);

            var p2 = in_example.BuildSameDirection();
            p2.ParseResult.TcpHeader->Rst = true;
p2.Recalc();
            _7500.StorePacket(p2);
            _7500.SendPacket(p2, true);
            #endregion
        }
    }
}
