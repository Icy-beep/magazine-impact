using System;
using System.IO;
using System.Text;

namespace MagazineImpact
{
    internal static class Wav
    {
        internal static void ReadPcm16(byte[] bytes, out float[] samples, out int channels, out int rate)
        {
            channels = rate = 0;
            samples = null;
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (bytes.Length < 12 || Tag(reader) != "RIFF") throw new InvalidDataException("Not a RIFF WAV.");
                uint riffSize = reader.ReadUInt32();
                if (riffSize < 4 || (long)riffSize + 8 > bytes.Length || Tag(reader) != "WAVE")
                    throw new InvalidDataException("Invalid WAV header.");
                long end = (long)riffSize + 8;
                int format = 0, bits = 0, alignment = 0;
                byte[] data = null;
                while (stream.Position + 8 <= end)
                {
                    string tag = Tag(reader);
                    uint size = reader.ReadUInt32();
                    long next = stream.Position + size;
                    if (next > end) throw new InvalidDataException("Truncated WAV chunk.");
                    if (tag == "fmt ")
                    {
                        if (size < 16) throw new InvalidDataException("Invalid format chunk.");
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        rate = reader.ReadInt32();
                        reader.ReadUInt32();
                        alignment = reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                    }
                    else if (tag == "data")
                    {
                        // Keep custom sound files small and bounded even if the header is hostile.
                        if (size > 4 * 1024 * 1024) throw new InvalidDataException("WAV exceeds 4 MiB.");
                        data = reader.ReadBytes((int)size);
                    }
                    stream.Position = next + (size & 1);
                }
                if (format != 1 || bits != 16 || channels < 1 || channels > 2 ||
                    rate < 8000 || rate > 192000 || alignment != channels * 2 ||
                    data == null || data.Length == 0 || data.Length % alignment != 0 ||
                    (double)data.Length / alignment / rate > 5)
                    throw new InvalidDataException("Use PCM WAV, 16-bit, mono/stereo, <= 5 seconds.");
                samples = new float[data.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
            }
        }

        private static string Tag(BinaryReader reader)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(4));
        }
    }
}
