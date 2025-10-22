using redfish.Utility;
using System;
using System.Collections.Generic;

namespace redfish.Models
{
    /// <summary>
    /// Represents a single swap configuration profile
    /// </summary>
    public class SwapProfile
    {
        public string Name { get; set; } = "Swap 1";
        public List<Keycode> Keybind { get; set; } = new List<Keycode>();
        public int SwapDuration { get; set; } = 2000;
        public int SwapDelay { get; set; } = 25;
        public int UntickDelay { get; set; } = 500;
        public int EndingLoadout { get; set; } = 1;
        public bool Port3074 { get; set; } = true;
        public bool[] LoadoutEnabled { get; set; } = new bool[12];
        public bool Packet3074DL { get; set; } = false;
        public bool AutoDisableBuffering { get; set; } = false;
        public bool CloseInventory { get; set; } = false;

        public SwapProfile()
        {
            // Default constructor
        }

        public SwapProfile Clone()
        {
            return new SwapProfile
            {
                Name = this.Name,
                Keybind = new List<Keycode>(this.Keybind),
                SwapDuration = this.SwapDuration,
                SwapDelay = this.SwapDelay,
                UntickDelay = this.UntickDelay,
                EndingLoadout = this.EndingLoadout,
                Port3074 = this.Port3074,
                LoadoutEnabled = (bool[])this.LoadoutEnabled.Clone(),
                Packet3074DL = this.Packet3074DL,
                AutoDisableBuffering = this.AutoDisableBuffering,
                CloseInventory = this.CloseInventory
            };
        }
    }
}