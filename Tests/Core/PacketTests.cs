using System;
using System.IO;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>PacketWriter and PacketReader behave as XNA's, which are BinaryWriter and BinaryReader.</summary>
    [TestFixture]
    public class PacketTests
    {
        private static PacketReader ReaderFor(PacketWriter writer)
        {
            var reader = new PacketReader();
            reader.Reset(writer.GetData());
            return reader;
        }

        [Test]
        public void WriteByteArray_WritesTheBytesAsTheyAre()
        {
            var writer = new PacketWriter();
            writer.Write(new byte[] { 1, 2, 3 });

            Assert.That(writer.GetData(), Is.EqualTo(new byte[] { 1, 2, 3 }), "no length prefix, as in XNA");
        }

        [Test]
        public void WriteByteArrayRange_WritesOnlyThatRange()
        {
            var writer = new PacketWriter();
            writer.Write(new byte[] { 1, 2, 3, 4, 5 }, 1, 3);

            Assert.That(writer.GetData(), Is.EqualTo(new byte[] { 2, 3, 4 }));
        }

        [Test]
        public void ReadBytes_ReadsWhatWriteWrote()
        {
            var writer = new PacketWriter();
            writer.Write(new byte[] { 9, 8, 7 });
            writer.Write(42);

            var reader = ReaderFor(writer);
            Assert.That(reader.ReadBytes(3), Is.EqualTo(new byte[] { 9, 8, 7 }));
            Assert.That(reader.ReadInt32(), Is.EqualTo(42));
        }

        [Test]
        public void LengthPrefixedBytes_RoundTrip_AndAnOverlongPrefixIsRejected()
        {
            var writer = new PacketWriter();
            writer.WriteLengthPrefixed(new byte[] { 5, 6 });
            Assert.That(ReaderFor(writer).ReadLengthPrefixedBytes(), Is.EqualTo(new byte[] { 5, 6 }));

            var lying = new PacketWriter();
            lying.Write(1000);          // claims 1000 bytes follow
            lying.Write((byte)1);
            Assert.Throws<EndOfStreamException>(() => ReaderFor(lying).ReadLengthPrefixedBytes());
        }

        [Test]
        public void CharsAndDecimals_RoundTrip()
        {
            var writer = new PacketWriter();
            writer.Write('x');
            writer.Write(new[] { 'a', 'b', 'c' });
            writer.Write(123.456m);

            var reader = ReaderFor(writer);
            Assert.That(reader.ReadChar(), Is.EqualTo('x'));
            Assert.That(reader.ReadChars(3), Is.EqualTo(new[] { 'a', 'b', 'c' }));
            Assert.That(reader.ReadDecimal(), Is.EqualTo(123.456m));
        }

        [Test]
        public void Seek_RewritesEarlierBytes()
        {
            var writer = new PacketWriter();
            writer.Write(0);
            writer.Write("payload");
            writer.Seek(0, SeekOrigin.Begin);
            writer.Write(7);

            var reader = ReaderFor(writer);
            Assert.That(reader.ReadInt32(), Is.EqualTo(7));
            Assert.That(reader.ReadString(), Is.EqualTo("payload"));
        }
    }
}
