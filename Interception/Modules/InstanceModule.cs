using vermillion.Models;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using WindivertDotnet;

namespace vermillion.Interception.Modules
{
    public class InstanceModule : PacketModuleBase
    {
        PacketProviderBase provider;
        public InstanceModule() : base("З0k", true, InterceptionManager.GetProvider("30000"))
        {
            Icon = System.Windows.Application.Current.FindResource("Traveller") as Geometry;
            Description = @"Blocks inbound З0k updates with optional rate limiting";
            provider = PacketProviders.First();
            Buffer = Config.GetNamed(Name).GetSettings<bool>("Buffer");

            // Initialize rate limiting settings
            RateLimitingEnabled = Config.GetNamed(Name).GetSettings<bool>("RateLimitingEnabled");
            TargetBytesPerSecond = Config.GetNamed(Name).GetSettings<long>("TargetBytesPerSecond");
            if (TargetBytesPerSecond == 0) TargetBytesPerSecond = 100; // Default 1 Mbps (125,000 bytes/s)

            // Initialize packet release timer (fires every 50ms for smooth packet release)
            packetReleaseTimer = new Timer(ProcessPacketQueue, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        }

        public override void Toggle()
        {
            IsActivated = !IsActivated;
            if (!IsActivated)
            {
                // Clear the rate limiting queue when deactivated
                while (packetQueue.TryDequeue(out var _)) { }
                lock (rateLimitLock)
                {
                    totalBytesTransferred = 0;
                    lastResetTime = DateTime.Now;
                }

                Task.Run(async () =>
                {
                    foreach (var addr in TcpReordering.Cache.Keys.Where(x => x.Contains(":300")).ToArray())
                    {
                        try
                        {
                            var send = TcpReordering.Cache[addr].Location[FlagType.Remote].Blocked.ToArray();
                            TcpReordering.Cache[addr].Location[FlagType.Remote].Blocked.Clear();

                            if (!send.Any()) continue;

                            foreach (var p in send)
                            {
                                p.CreatedAt = DateTime.Now;
                                p.Delayed = false;
                                p.AckNum = 0; // let storepacket assign highest
                                p.SourceProvider.StorePacket(p);
                                if (Buffer && !p.Flags.HasFlag(TcpFlags.FIN) && !p.Flags.HasFlag(TcpFlags.RST))
                                    await p.SourceProvider.SendPacket(p, true);

                                Debug.WriteLine($"{Name}: Seq dist {TcpReordering.Cache[addr].Location[FlagType.Remote].HighSeq - p.SeqNum}");
                            }

                            Debug.WriteLine($"{Name}: Sent {send.Length} on {addr}");

                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine(e, "at З0k");
                        }
                    }
                });
            }
            else
            {
                // Reset rate limiting counters when activated
                lock (rateLimitLock)
                {
                    totalBytesTransferred = 0;
                    lastResetTime = DateTime.Now;
                }
            }
        }

        private void ProcessPacketQueue(object state)
        {
            if (!RateLimitingEnabled || isProcessingQueue || !IsActivated)
                return;

            isProcessingQueue = true;

            try
            {
                var now = DateTime.Now;

                lock (rateLimitLock)
                {
                    // Reset counters every second
                    var timeSinceReset = (now - lastResetTime).TotalSeconds;
                    if (timeSinceReset >= 1.0)
                    {
                        totalBytesTransferred = 0;
                        lastResetTime = now;
                        Debug.WriteLine($"{Name}: Rate limit window reset. Queue size: {packetQueue.Count}");
                    }

                    // Safety check
                    if (TargetBytesPerSecond <= 0)
                    {
                        TargetBytesPerSecond = 100; // Reset to default 800 bps (100 bytes/s)
                    }

                    // Process packets until we hit the rate limit
                    int packetsProcessed = 0;
                    while (packetQueue.TryPeek(out Packet packet))
                    {
                        var packetBytes = packet.Length;

                        // Check if we have enough bandwidth in the current window to process this packet.
                        if (totalBytesTransferred + packetBytes > TargetBytesPerSecond && totalBytesTransferred > 0)
                        {
                            // We've hit the rate limit for this second, wait for the next window.
                            Debug.WriteLine($"{Name}: Rate limit reached ({totalBytesTransferred}/{TargetBytesPerSecond} bytes). Waiting for reset. Queue: {packetQueue.Count}");
                            break;
                        }

                        // Dequeue and process the packet
                        if (packetQueue.TryDequeue(out packet))
                        {
                            // Add the packet's size to our bandwidth counter for this second.
                            totalBytesTransferred += packetBytes;
                            packetsProcessed++;

                            // Calculate the delay required to simulate the target bandwidth.
                            // delay (seconds) = size (bytes) / speed (bytes/second)
                            double delayInSeconds = (double)packetBytes / TargetBytesPerSecond;
                            var delay = TimeSpan.FromSeconds(delayInSeconds);

                            // Send the packet with the calculated delay.
                            try
                            {
                                packet.CreatedAt = DateTime.Now;
                                packet.Delayed = true; // Mark it as intentionally delayed
                                packet.AckNum = 0;
                                packet.SourceProvider.StorePacket(packet);

                                // Use the provider's built-in delay mechanism.
                                packet.SourceProvider.DelayPacket(packet, delay);

                                Debug.WriteLine($"{Name}: Delaying packet {packet.Length} bytes by {delay.TotalMilliseconds:F2} ms. Total bytes this window: {totalBytesTransferred}");
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e, "Rate limiting packet processing");
                            }
                        }
                    }

                    if (packetsProcessed > 0)
                    {
                        Debug.WriteLine($"{Name}: Processed {packetsProcessed} packets. Queue remaining: {packetQueue.Count}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e, "ProcessPacketQueue error");
            }
            finally
            {
                isProcessingQueue = false;
            }
        }

        public static bool Buffer;
        public static bool RateLimitingEnabled;
        public static long TargetBytesPerSecond;

        // Rate limiting tracking variables
        private DateTime lastResetTime = DateTime.Now;
        private long totalBytesTransferred = 0;
        private readonly object rateLimitLock = new object();

        // Packet queue for smooth rate limiting
        private readonly ConcurrentQueue<Packet> packetQueue = new ConcurrentQueue<Packet>();
        private readonly Timer packetReleaseTimer;
        private volatile bool isProcessingQueue = false;

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;

            if (!IsActivated) return true;

            if (p.Outbound || p.Length == 0) return true;

            // If rate limiting is enabled, queue the packet for controlled release
            if (RateLimitingEnabled)
            {
                // Add packet to queue for rate-limited processing
                packetQueue.Enqueue(p);
                Debug.WriteLine($"{Name}: Queued packet {p.Length} bytes, queue size: {packetQueue.Count}");
                return false; // Block the original packet, it will be processed through the queue
            }

            // Original blocking behavior when rate limiting is disabled
            return false;
        }

        public override void StopListening()
        {
            // Clean up rate limiting resources when stopping
            packetReleaseTimer?.Dispose();

            // Clear any remaining packets in the queue
            while (packetQueue.TryDequeue(out var _)) { }

            base.StopListening();
        }
    }
}