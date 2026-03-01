namespace FEBuilderGBA
{
    /// <summary>
    /// Test surface for GDBSocket (internal in Core).
    /// WinForms can see Core internals via InternalsVisibleTo;
    /// Tests can see this class via WinForms InternalsVisibleTo.
    /// </summary>
    internal static class GDBSocketTestHelper
    {
        public static byte[] MakePacket(string order)
        {
            using (var g = new GDBSocket())
            {
                return g.MakePacket(order);
            }
        }

        public static string UnPacket(byte[] packet, int length)
        {
            using (var g = new GDBSocket())
            {
                return g.UnPacket(packet, length);
            }
        }

        public static void RunInlineTest()
        {
            GDBSocket.TEST_GDB_CHECKSUM();
        }

        public static void DisposeUnconnected()
        {
            var g = new GDBSocket();
            g.Dispose();
        }

        public static void DisposeDouble()
        {
            var g = new GDBSocket();
            g.Dispose();
            g.Dispose();
        }
    }
}
