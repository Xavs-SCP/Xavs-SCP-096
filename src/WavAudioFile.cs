using System;
using System.IO;

namespace Scp096ChaseMusic
{
    // Minimal RIFF/WAVE reader.
    //
    // Only what the plugin needs: enough header parsing to locate the PCM payload, and random access reads so a
    // single section can be pulled out of the file without ever materialising the whole track. Multi-channel
    // files are downmixed to mono, because that is what the game's voice pipeline transmits.
    public sealed class WavAudioFile : IDisposable
    {
        private const ushort FormatPcm = 1;
        private const ushort FormatIeeeFloat = 3;
        private const ushort FormatExtensible = 0xFFFE;

        private readonly FileStream _stream;
        private readonly long _dataOffset;
        private readonly ushort _formatTag;
        private readonly int _bytesPerSample;
        private readonly int _bytesPerFrame;

        public int SampleRate { get; }
        public int Channels { get; }
        public int BitsPerSample { get; }

        // Number of sample frames (i.e. per channel), not raw samples.
        public int FrameCount { get; }

        public double Duration => (double)FrameCount / SampleRate;

        private WavAudioFile(FileStream stream, long dataOffset, long dataLength, ushort formatTag,
            int sampleRate, int channels, int bitsPerSample)
        {
            _stream = stream;
            _dataOffset = dataOffset;
            _formatTag = formatTag;
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            _bytesPerSample = bitsPerSample / 8;
            _bytesPerFrame = _bytesPerSample * channels;
            FrameCount = (int)(dataLength / _bytesPerFrame);
        }

        public static WavAudioFile TryOpen(string path, out string error)
        {
            error = null;
            FileStream stream = null;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF")
                    {
                        error = "not a RIFF file.";
                        stream.Dispose();
                        return null;
                    }

                    reader.ReadUInt32(); // RIFF chunk size, unreliable in the wild - we trust the chunk headers.

                    if (new string(reader.ReadChars(4)) != "WAVE")
                    {
                        error = "not a WAVE file.";
                        stream.Dispose();
                        return null;
                    }

                    ushort formatTag = 0;
                    int channels = 0;
                    int sampleRate = 0;
                    int bitsPerSample = 0;
                    bool haveFormat = false;

                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunkId = new string(reader.ReadChars(4));
                        uint chunkSize = reader.ReadUInt32();
                        long chunkStart = stream.Position;

                        if (chunkId == "fmt ")
                        {
                            formatTag = reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = reader.ReadInt32();
                            reader.ReadUInt32(); // byte rate
                            reader.ReadUInt16(); // block align
                            bitsPerSample = reader.ReadUInt16();

                            // WAVE_FORMAT_EXTENSIBLE hides the real format tag in the first two bytes of its GUID.
                            if (formatTag == FormatExtensible && chunkSize >= 40)
                            {
                                reader.ReadUInt16();               // cbSize
                                reader.ReadUInt16();               // valid bits per sample
                                reader.ReadUInt32();               // channel mask
                                formatTag = reader.ReadUInt16();   // first field of the SubFormat GUID
                            }

                            haveFormat = true;
                        }
                        else if (chunkId == "data")
                        {
                            if (!haveFormat)
                            {
                                error = "'data' chunk appears before 'fmt '.";
                                stream.Dispose();
                                return null;
                            }

                            long dataLength = Math.Min(chunkSize, stream.Length - chunkStart);

                            if (formatTag != FormatPcm && formatTag != FormatIeeeFloat)
                            {
                                error = "unsupported WAV encoding (format tag " + formatTag +
                                        "). Re-encode as uncompressed PCM or 32-bit float.";
                                stream.Dispose();
                                return null;
                            }

                            if (formatTag == FormatPcm && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32)
                            {
                                error = "unsupported PCM bit depth (" + bitsPerSample + "). Use 16, 24 or 32 bit.";
                                stream.Dispose();
                                return null;
                            }

                            if (formatTag == FormatIeeeFloat && bitsPerSample != 32)
                            {
                                error = "unsupported float bit depth (" + bitsPerSample + "). Use 32 bit.";
                                stream.Dispose();
                                return null;
                            }

                            if (channels < 1)
                            {
                                error = "the file reports " + channels + " channels.";
                                stream.Dispose();
                                return null;
                            }

                            return new WavAudioFile(stream, chunkStart, dataLength, formatTag, sampleRate, channels, bitsPerSample);
                        }

                        // Chunks are word aligned.
                        long next = chunkStart + chunkSize + (chunkSize % 2);
                        if (next <= chunkStart || next > stream.Length)
                            break;

                        stream.Position = next;
                    }

                    error = "no 'data' chunk found.";
                    stream.Dispose();
                    return null;
                }
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                error = ex.Message;
                return null;
            }
        }

        // Reads frameCount frames starting at startFrame, downmixed to mono and normalised to -1..1.
        public float[] ReadMono(int startFrame, int frameCount)
        {
            if (startFrame < 0 || frameCount <= 0 || startFrame + frameCount > FrameCount)
                throw new ArgumentOutOfRangeException(nameof(startFrame), "Requested range lies outside the audio file.");

            long byteCount = (long)frameCount * _bytesPerFrame;
            if (byteCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(frameCount), "Section is too large to load.");

            byte[] raw = new byte[byteCount];

            _stream.Position = _dataOffset + (long)startFrame * _bytesPerFrame;
            int read = 0;
            while (read < raw.Length)
            {
                int chunk = _stream.Read(raw, read, raw.Length - read);
                if (chunk <= 0)
                    throw new EndOfStreamException("Audio file ended early while reading a section.");

                read += chunk;
            }

            float[] samples = new float[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0f;
                int offset = frame * _bytesPerFrame;
                for (int channel = 0; channel < Channels; channel++)
                    sum += ReadSample(raw, offset + channel * _bytesPerSample);

                samples[frame] = sum / Channels;
            }

            return samples;
        }

        private float ReadSample(byte[] buffer, int offset)
        {
            if (_formatTag == FormatIeeeFloat)
                return BitConverter.ToSingle(buffer, offset);

            switch (BitsPerSample)
            {
                case 16:
                    return BitConverter.ToInt16(buffer, offset) / 32768f;
                case 24:
                    int value24 = buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16);
                    return value24 / 8388608f;
                case 32:
                    return BitConverter.ToInt32(buffer, offset) / 2147483648f;
                default:
                    return 0f;
            }
        }

        public void Dispose() => _stream?.Dispose();
    }
}
