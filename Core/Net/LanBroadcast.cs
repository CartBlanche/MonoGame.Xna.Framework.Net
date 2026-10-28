using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Works out where to send a LAN broadcast.
    /// 
    /// A datagram sent to 255.255.255.255 leaves through only one network adapter, whichever
    /// the operating system prefers. On a machine with several (Wi-Fi plus VPN, WSL, Hyper-V or
    /// Docker adapters) that is often not the one the other players are on, so the
    /// advertisement never reaches them. Sending to each adapter's own broadcast address
    /// (for example 192.168.1.255) reaches every network the machine is on.
    /// </summary>
    internal static class LanBroadcast
    {
        /// <summary>
        /// The broadcast address of the subnet that an address and subnet mask describe
        /// </summary>
        public static IPAddress DirectedBroadcast(IPAddress address, IPAddress mask)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (mask == null)
                throw new ArgumentNullException(nameof(mask));

            byte[] a = address.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();
            if (a.Length != 4 || m.Length != 4)
                throw new ArgumentException("Only IPv4 addresses have a broadcast address.");

            byte[] result = new byte[4];
            for (int i = 0; i < 4; i++)
                result[i] = (byte)(a[i] | ~m[i]);
            return new IPAddress(result);
        }

        /// <summary>
        /// The broadcast address of every IPv4 network this machine is connected to.
        /// Adapters that are down, loopback, or point to point (a /32 has no broadcast) are skipped.
        /// </summary>
        public static IReadOnlyList<IPAddress> GetBroadcastAddresses()
        {
            List<IPAddress> result = new List<IPAddress>();

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                            continue;

                        IPAddress broadcast = DirectedBroadcast(unicast.Address, unicast.IPv4Mask);
                        if (!result.Contains(broadcast) &&
                            !broadcast.Equals(IPAddress.Broadcast) &&
                            !broadcast.Equals(unicast.Address))
                            result.Add(broadcast);
                    }
                }
            }
            catch (Exception)
            {
                // some platforms do not allow listing adapters; the global broadcast still goes out
            }

            return result;
        }
    }
}
