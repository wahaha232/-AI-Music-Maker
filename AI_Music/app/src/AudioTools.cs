using System;
using System.IO;
using NAudio.Wave;
using TagLib;

namespace AIMusicGenerator
{
    internal static class AudioTools
    {
        public static WaveStream OpenReader(string path)
        {
            try
            {
                return new AudioFileReader(path);
            }
            catch (Exception)
            {
                return new MediaFoundationReader(path);
            }
        }

        public static float[] BuildPeaks(string path, int buckets, out double seconds)
        {
            using (WaveStream reader = OpenReader(path))
            {
                ISampleProvider provider = reader.ToSampleProvider();
                int channels = Math.Max(1, provider.WaveFormat.Channels);
                long totalFrames = Math.Max(1L,
                    (long)Math.Round(reader.TotalTime.TotalSeconds * provider.WaveFormat.SampleRate));
                float[] peaks = new float[buckets];
                float[] buffer = new float[16384];
                long frame = 0;
                int read;
                while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    int frames = read / channels;
                    for (int i = 0; i < frames; i++)
                    {
                        int index = (int)((frame + i) * buckets / totalFrames);
                        if (index >= buckets) index = buckets - 1;
                        for (int c = 0; c < channels; c++)
                        {
                            float value = Math.Abs(buffer[i * channels + c]);
                            if (value > peaks[index]) peaks[index] = value;
                        }
                    }
                    frame += frames;
                }
                seconds = reader.TotalTime.TotalSeconds;
                return peaks;
            }
        }

        public static void WriteTags(string path, string prompt, long seed, string bpm, string key, string format, string coverPath)
        {
            using (TagLib.File file = TagLib.File.Create(path))
            {
                string title = (prompt ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
                if (title.Length > 200) title = title.Substring(0, 200);
                file.Tag.Title = title;
                file.Tag.Performers = new string[] { "AI Music Maker" };
                file.Tag.Comment = "seed=" + seed + "; bpm=" + bpm + "; key=" + key + "; format=" + format;
                bool hasCover = false;
                if (!string.IsNullOrEmpty(coverPath) && System.IO.File.Exists(coverPath))
                {
                    try
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(coverPath);
                        TagLib.Picture picture = new TagLib.Picture();
                        picture.Data = new TagLib.ByteVector(bytes);
                        picture.Type = TagLib.PictureType.FrontCover;
                        picture.MimeType = coverPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            ? "image/png" : "image/jpeg";
                        picture.Description = "Cover";
                        file.Tag.Pictures = new TagLib.IPicture[] { picture };
                        hasCover = true;
                    }
                    catch (Exception)
                    {
                        hasCover = false;
                    }
                }
                try
                {
                    file.Save();
                }
                catch (Exception)
                {
                    if (hasCover)
                    {
                        file.Tag.Pictures = new TagLib.IPicture[0];
                        file.Save();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        public static string ReadComment(string path)
        {
            try
            {
                using (TagLib.File file = TagLib.File.Create(path))
                {
                    string title = file.Tag.Title ?? "";
                    string comment = file.Tag.Comment ?? "";
                    int pictures = file.Tag.Pictures == null ? 0 : file.Tag.Pictures.Length;
                    if (title.Length > 60) title = title.Substring(0, 60);
                    return (title.Length > 0 ? title + "  |  " : "") + comment + "  |  pictures=" + pictures;
                }
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
