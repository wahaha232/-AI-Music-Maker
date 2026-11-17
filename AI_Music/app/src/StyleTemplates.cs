using System.Collections.Generic;

namespace AIMusicGenerator
{
    internal sealed class StyleTemplate
    {
        public string Name;
        public string Caption;
        public int Bpm;
        public string Key;
        public string Signature;
        public bool Instrumental;

        public StyleTemplate(string name, string caption, int bpm, string key,
            string signature, bool instrumental)
        {
            Name = name;
            Caption = caption;
            Bpm = bpm;
            Key = key;
            Signature = signature;
            Instrumental = instrumental;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class StyleTemplates
    {
        public static readonly List<StyleTemplate> All = new List<StyleTemplate>
        {
            new StyleTemplate(
                "Lo-fi 深夜雨景",
                "Instrumental lo-fi hip hop, warm Rhodes piano, soft swung drums, " +
                "round bass, vinyl crackle, subtle rain ambience, jazzy soul chords, " +
                "mellow and nostalgic late-night atmosphere, no vocals, no sudden dynamics",
                72, "C Minor", "4", true),
            new StyleTemplate(
                "Chillhop 咖啡廳",
                "Instrumental chillhop, mellow electric piano, dusty boom-bap drums, " +
                "warm upright bass, light guitar licks, soft vinyl texture, " +
                "relaxed cafe study atmosphere, no vocals, smooth and unobtrusive",
                82, "F Major", "4", true),
            new StyleTemplate(
                "Ambient 漂浮",
                "Instrumental ambient music, warm evolving pads, gentle piano fragments, " +
                "slow harmonic movement, spacious reverb, soft low drone, " +
                "calm floating atmosphere, no percussion, no vocals, very gentle",
                60, "D Major", "4", true),
            new StyleTemplate(
                "Cinematic 電影感",
                "Instrumental cinematic music, emotional piano, warm strings, " +
                "subtle hybrid percussion, gradual build and release, wide reverb, " +
                "hopeful and reflective film score atmosphere, no vocals",
                70, "A Minor", "4", true),
            new StyleTemplate(
                "Acoustic 木吉他",
                "Instrumental acoustic folk, fingerpicked steel-string guitar, " +
                "soft upright bass, light shaker and brushed drums, intimate room sound, " +
                "warm natural atmosphere, no vocals, gentle and organic",
                88, "G Major", "4", true),
            new StyleTemplate(
                "Jazz 深夜鋼琴",
                "Instrumental jazz trio, expressive grand piano, walking upright bass, " +
                "brushed jazz drums, lush extended chords, intimate late-night club feel, " +
                "no vocals, elegant and relaxed",
                96, "A# Major", "4", true),
            new StyleTemplate(
                "Synthwave 復古",
                "Instrumental synthwave, warm analog synth pads, arpeggiated leads, " +
                "punchy retro drums, deep bass, subtle tape saturation, " +
                "nostalgic night-drive atmosphere, no vocals",
                104, "E Minor", "4", true),
            new StyleTemplate(
                "EDM 柔和電子",
                "Instrumental melodic electronic, warm plucks, soft supersaw chords, " +
                "steady four-on-the-floor groove, deep round bass, airy pads, " +
                "uplifting but restrained energy, no vocals",
                118, "A Minor", "4", true),
            new StyleTemplate(
                "Rock 溫暖抒情",
                "Instrumental warm rock ballad, clean electric guitar, soft bass, " +
                "acoustic drum kit, subtle organ, restrained dynamics, " +
                "emotional and hopeful atmosphere, no vocals",
                92, "D Major", "4", true),
            new StyleTemplate(
                "Hip-Hop 慵懶",
                "Instrumental mellow hip hop, lazy swung drums, warm sample-like keys, " +
                "deep sub bass, vinyl dust, sparse melodic loop, " +
                "relaxed confident late-night mood, no vocals",
                86, "F Minor", "4", true)
        };
    }
}
