using System;
using System.IO;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--auto")
            {
                Environment.Exit(AutoRunner.Run(args[1]));
                return;
            }
            if (args.Length >= 3 && args[0] == "--audio-edit-test")
            {
                try
                {
                    System.Text.StringBuilder log = new System.Text.StringBuilder();
                    string input = args[1];
                    string outDir = args[2];
                    Directory.CreateDirectory(outDir);
                    string fadeOut = Path.Combine(outDir,
                        Path.GetFileNameWithoutExtension(input) + "_fade_test" + Path.GetExtension(input));
                    int fadeCode = AudioEditRunner.Run(
                        Paths.BuildAudioFadeStartInfo(input, 1.0, 1.0, fadeOut),
                        delegate(string line) { log.AppendLine(line); });
                    int normCode = AudioEditRunner.Run(
                        Paths.BuildAudioNormalizeStartInfo(new string[] { input }, outDir),
                        delegate(string line) { log.AppendLine(line); });
                    int mergeCode = 0;
                    if (args.Length >= 4)
                    {
                        string merged = Path.Combine(outDir, "merged_test.mp3");
                        mergeCode = AudioEditRunner.Run(
                            Paths.BuildAudioMergeStartInfo(new string[] { input, args[3] }, merged),
                            delegate(string line) { log.AppendLine(line); });
                    }
                    log.AppendLine("fade=" + fadeCode + " normalize=" + normCode +
                                   " merge=" + mergeCode);
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "audio_edit_test.txt"), log.ToString());
                    Environment.Exit(fadeCode == 0 && normCode == 0 && mergeCode == 0 ? 0 : 1);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "audio_edit_test.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--video-test")
            {
                try
                {
                    System.Text.StringBuilder log = new System.Text.StringBuilder();
                    string image = args.Length >= 3 ? args[2] : "";
                    int code = VideoRunner.Run(args[1], image, delegate(string line) { log.AppendLine(line); });
                    string output = Path.ChangeExtension(args[1], ".mp4");
                    log.AppendLine("exit=" + code + " output=" + output + " exists=" + File.Exists(output));
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "video_test.txt"), log.ToString());
                    Environment.Exit(code == 0 && File.Exists(output) ? 0 : 1);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "video_test.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--sd-test")
            {
                try
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    System.Text.StringBuilder log = new System.Text.StringBuilder();
                    string audio = args[1];
                    string model = args.Length >= 3 ? args[2] : "";
                    bool ready = SdCoverClient.EnsureApi("http://127.0.0.1:7860",
                        @"G:\stable-diffusion-webui", delegate(string message) { log.AppendLine(message); });
                    if (!ready) throw new InvalidOperationException("SD API unavailable");
                    if (model.Length > 0) SdCoverClient.SelectModel("http://127.0.0.1:7860", model);
                    string cover = Path.ChangeExtension(audio, ".cover.png");
                    SdCoverClient.GenerateCover("http://127.0.0.1:7860",
                        "warm acoustic guitar album cover art, square composition, cinematic warm light, no text, no watermark, high quality",
                        "text, watermark, logo, letters, blurry, low quality, deformed",
                        512, 512, 12, cover);
                    AudioTools.WriteTags(audio, "SD cover test", 1, "92", "C Major", "mp3", cover);
                    string comment = AudioTools.ReadComment(audio);
                    log.AppendLine("cover=" + cover + " bytes=" + (File.Exists(cover) ? new FileInfo(cover).Length : 0));
                    log.AppendLine("tags=" + comment);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "sd_test.txt"), log.ToString());
                    SdCoverClient.Stop();
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "sd_test.txt"), ex.ToString());
                    SdCoverClient.Stop();
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 1 && args[0] == "--ui-test-single")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = new MainForm();
                form.StartSingleUiTest();
                Application.Run(form);
                Environment.Exit(form.UiTestResult);
                return;
            }

            if (args.Length >= 2 && args[0] == "--auto-video-test")
            {
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        string report = form.AutoVideoTest(args[1]);
                        Directory.CreateDirectory(Paths.TempDir);
                        File.WriteAllText(Path.Combine(Paths.TempDir, "auto_video_test.txt"), report);
                        Environment.Exit(report.StartsWith("ok") ? 0 : 1);
                    }
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "auto_video_test.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 1 && args[0] == "--ui-selftest")
            {
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        string report = form.UiSelfTest();
                        Directory.CreateDirectory(Paths.TempDir);
                        File.WriteAllText(Path.Combine(Paths.TempDir, "ui_selftest.txt"), report);
                        Environment.Exit(0);
                    }
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "ui_selftest.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--link-test")
            {
                try
                {
                    LinkInfo info = LinkAnalyzer.Analyze(args[1]);
                    bool zh = args.Length >= 3 && args[2].Equals("zh", StringComparison.OrdinalIgnoreCase);
                    PromptSuggestion suggestion = LinkAnalyzer.Suggest(info, true, zh);
                    Directory.CreateDirectory(Paths.TempDir);
                    string body = "source=" + info.Source + "\r\nartist=" + info.Artist +
                                  "\r\ntitle=" + info.Title + "\r\nalbum=" + info.Album +
                                  "\r\nyear=" + info.Year + "\r\ngenre=" + suggestion.Genre +
                                  "\r\nbpm=" + suggestion.Bpm + "\r\nprompt=" + suggestion.Prompt +
                                  "\r\nnegative=" + suggestion.Negative;
                    File.WriteAllText(Path.Combine(Paths.TempDir, "link_test.txt"), body);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "link_test.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }

            if (args.Length >= 2 && args[0] == "--player-test")
            {
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        string report = form.PlayerTest(args[1]);
                        Directory.CreateDirectory(Paths.TempDir);
                        File.WriteAllText(Path.Combine(Paths.TempDir, "player_test.txt"), report);
                        Environment.Exit(0);
                    }
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "player_test.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--audio-test")
            {
                try
                {
                    double seconds;
                    float[] peaks = AudioTools.BuildPeaks(args[1], 128, out seconds);
                    string copy = Path.Combine(Paths.TempDir, "audio_test_copy" + Path.GetExtension(args[1]));
                    Directory.CreateDirectory(Paths.TempDir);
                    File.Copy(args[1], copy, true);
                    AudioTools.WriteTags(copy, "Audio test prompt", 123456, "78", "C Major", "wav", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cover.png"));
                    string comment = AudioTools.ReadComment(copy);
                    string report = "seconds=" + seconds.ToString("F2") + " peaks=" + peaks.Length +
                                    " peak_max=" + MaxPeak(peaks).ToString("F3") + " comment=" + comment;
                    File.WriteAllText(Path.Combine(Paths.TempDir, "audio_test.txt"), report);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(Paths.TempDir);
                    File.WriteAllText(Path.Combine(Paths.TempDir, "audio_test.txt"), ex.ToString());
                    Environment.Exit(1);
                }
                return;
            }
            if (args.Length >= 1 && args[0] == "--style-report")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = new MainForm();
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                System.IO.Directory.CreateDirectory(Paths.TempDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Paths.TempDir, "style_report.txt"), form.StyleReport());
                form.Close();
                Environment.Exit(0);
                return;
            }
            if (args.Length >= 2 && args[0] == "--ui-screenshot")
            {
                // --ui-screenshot <out.png> [dark|light] [demoOutputDir] [dialog]
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string target = args[1];
                bool dark = args.Length < 3 || !args[2].Equals("light", StringComparison.OrdinalIgnoreCase);
                string demo = args.Length >= 4 ? args[3] : "";
                string dialog = args.Length >= 5 ? args[4] : "";
                int code = UiScreenshot.Run(target, dark, demo, dialog);
                Environment.Exit(code);
                return;
            }
            if (args.Length >= 1 && args[0] == "--ui-test-batch")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = new MainForm();
                form.StartUiTest(true);
                Application.Run(form);
                Environment.Exit(form.UiTestResult);
                return;
            }
            if (args.Length >= 1 && args[0] == "--ui-test")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = new MainForm();
                form.StartUiTest(false);
                Application.Run(form);
                Environment.Exit(form.UiTestResult);
                return;
            }
            if (args.Length >= 1 && args[0] == "--selftest")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int code = 0;
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        form.CreateControl();
                    }
                    string quoteDetails;
                    if (!Paths.SelfTestQuoting(out quoteDetails))
                    {
                        WriteSelftestError(quoteDetails);
                        code = 1;
                    }
                }
                catch (Exception ex)
                {
                    WriteSelftestError(ex.ToString());
                    code = 1;
                }
                Environment.Exit(code);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static float MaxPeak(float[] values)
        {
            float max = 0f;
            foreach (float value in values) if (value > max) max = value;
            return max;
        }

        private static void WriteSelftestError(string message)
        {
            try
            {
                string dir = Paths.TempDir;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "selftest_error.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + message,
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
            Console.Error.WriteLine(message);
        }
    }
}
