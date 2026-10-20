using System;
using System.IO;
using System.Text;
using MagazineImpact;

internal static class Tests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static byte[] MakeWav(short[] pcm, int channels, bool extraChunk)
    {
        using (var memory = new MemoryStream())
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(44100);
            writer.Write(44100 * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            if (extraChunk)
            {
                writer.Write(Encoding.ASCII.GetBytes("JUNK")); writer.Write(3);
                writer.Write(new byte[] { 1, 2, 3, 0 });
            }
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length * 2);
            foreach (short sample in pcm) writer.Write(sample);
            writer.Flush();
            memory.Position = 4; writer.Write((int)memory.Length - 8); writer.Flush();
            return memory.ToArray();
        }
    }

    private static void Reject(byte[] bytes, string message)
    {
        try { float[] s; int c, r; Wav.ReadPcm16(bytes, out s, out c, out r); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }

    public static void Main(string[] args)
    {
        Check(ImpactPolicy.ShouldPlay(4, .45f, 0, float.NegativeInfinity, .1f), "First landing must play.");
        Check(!ImpactPolicy.ShouldPlay(.1f, .45f, 4, 0, .1f), "Resting jitter must be silent.");
        Check(!ImpactPolicy.ShouldPlay(4, .45f, 1.02f, 1, .1f), "Duplicate contact must be suppressed.");
        Check(ImpactPolicy.ShouldPlay(1, .45f, 1.2f, 1, .1f), "A later bounce must play.");
        Check(!ImpactPolicy.ShouldPlay(float.NaN, .45f, 1, 0, .1f), "Invalid physics value must be rejected.");
        Check(!ImpactPolicy.ShouldPlay(float.PositiveInfinity, .45f, 1, 0, .1f), "Infinite physics value must be rejected.");
        Check(Math.Abs(ImpactPolicy.Gain(.45f, .45f, 3.5f) - .2f) < .00001, "Quiet accepted impact gain.");
        Check(ImpactPolicy.Gain(10, .45f, 3.5f) == 1, "Loud impact must be clamped.");
        Check(ImpactPolicy.Gain(1, .45f, 3.5f) < ImpactPolicy.Gain(2, .45f, 3.5f), "Harder impact should be louder.");
        Check(!float.IsNaN(ImpactPolicy.Gain(2, 2, .1f)), "Inverted config thresholds must not divide by zero.");
        float[] samples; int channels, rate;
        Wav.ReadPcm16(MakeWav(new short[] { short.MinValue, 0, short.MaxValue }, 1, true), out samples, out channels, out rate);
        Check(channels == 1 && rate == 44100 && samples.Length == 3, "Mono WAV and padded unknown chunks.");
        Check(samples[0] == -1 && samples[1] == 0 && samples[2] > .999f, "PCM signed conversion.");
        Wav.ReadPcm16(MakeWav(new short[] { 1, 2, 3, 4 }, 2, false), out samples, out channels, out rate);
        Check(channels == 2 && samples.Length == 4, "Stereo interleaving.");
        Reject(new byte[4], "Truncated header.");
        Reject(MakeWav(new short[] { 1, 2, 3 }, 2, false), "Partial stereo frame.");
        byte[] broken = MakeWav(new short[] { 1 }, 1, false);
        broken[4] = 255; Reject(broken, "RIFF length beyond file.");
        byte[] floatWav = MakeWav(new short[] { 1 }, 1, false);
        floatWav[20] = 3; Reject(floatWav, "Unsupported float WAV.");
        Wav.ReadPcm16(File.ReadAllBytes(args[0]), out samples, out channels, out rate);
        Check(samples.Length > 0 && channels == 1, "Bundled impact clip is readable.");
        Console.WriteLine("PASS: " + checks + " checks.");
    }
}
