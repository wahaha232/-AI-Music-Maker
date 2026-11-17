using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AIMusicGenerator
{
    internal static class Paths
    {
        private static string cachedWorkspace;

        public static string Workspace
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedWorkspace)) return cachedWorkspace;
                string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    if (File.Exists(Path.Combine(dir, "AI_Music.venv", "Scripts", "python.exe")))
                    {
                        cachedWorkspace = dir;
                        return cachedWorkspace;
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
                cachedWorkspace = @"G:\AIMUSIC";
                return cachedWorkspace;
            }
        }

        public static string PythonExe
        {
            get { return Path.Combine(Workspace, "AI_Music.venv", "Scripts", "python.exe"); }
        }

        public static string Backend
        {
            get { return Path.Combine(Workspace, "AI_Music", "app", "generate_backend.py"); }
        }

        public static string VideoTool
        {
            get { return Path.Combine(Workspace, "AI_Music", "scripts", "music_to_video.py"); }
        }

        public static string AudioEditTool
        {
            get { return Path.Combine(Workspace, "AI_Music", "scripts", "audio_edit.py"); }
        }

        public static string FfmpegExe
        {
            get { return Path.Combine(Workspace, "AI_Music", "tools", "ffmpeg", "bin", "ffmpeg.exe"); }
        }

        public static string Generated
        {
            get { return Path.Combine(Workspace, "AI_Music", "generated"); }
        }

        public static string SettingsDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AIMusicGenerator");
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(SettingsDir, "settings.json"); }
        }

        public static string LegacySettingsFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"); }
        }

        public static string TempDir
        {
            get { return Path.Combine(Path.GetTempPath(), "AIMusicGenerator"); }
        }

        public static string CoverFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cover.png"); }
        }

        public static string Quote(string value)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            int backslashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (ch == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(ch);
            }
            if (backslashes > 0) builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        public static ProcessStartInfo BuildStartInfo(string configPath)
        {
            return CreatePythonStartInfo(
                "-u " + Quote(Backend) + " --config " + Quote(configPath), false);
        }

        public static ProcessStartInfo BuildVideoStartInfo(string audioPath, string imagePath)
        {
            string arguments = "-u " + Quote(VideoTool) + " " + Quote(audioPath);
            if (!string.IsNullOrEmpty(imagePath)) arguments += " --image " + Quote(imagePath);
            return CreatePythonStartInfo(arguments, false);
        }

        public static ProcessStartInfo BuildAudioFadeStartInfo(string inputPath,
            double fadeIn, double fadeOut, string outputPath)
        {
            string arguments = "-u " + Quote(AudioEditTool) + " fade " + Quote(inputPath) +
                " --fade-in " + fadeIn.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                " --fade-out " + fadeOut.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                " --out " + Quote(outputPath) + " --overwrite";
            return CreatePythonStartInfo(arguments, false);
        }

        public static ProcessStartInfo BuildAudioMergeStartInfo(IEnumerable<string> inputs,
            string outputPath)
        {
            StringBuilder arguments = new StringBuilder();
            arguments.Append("-u ").Append(Quote(AudioEditTool)).Append(" merge");
            foreach (string input in inputs)
                arguments.Append(" ").Append(Quote(input));
            arguments.Append(" --out ").Append(Quote(outputPath)).Append(" --overwrite");
            return CreatePythonStartInfo(arguments.ToString(), false);
        }

        public static ProcessStartInfo BuildAudioNormalizeStartInfo(IEnumerable<string> inputs,
            string outputDir)
        {
            StringBuilder arguments = new StringBuilder();
            arguments.Append("-u ").Append(Quote(AudioEditTool)).Append(" normalize");
            foreach (string input in inputs)
                arguments.Append(" ").Append(Quote(input));
            arguments.Append(" --outdir ").Append(Quote(outputDir)).Append(" --overwrite");
            return CreatePythonStartInfo(arguments.ToString(), false);
        }

        public static ProcessStartInfo BuildDaemonStartInfo(string offloadMode)
        {
            return CreatePythonStartInfo(
                "-u " + Quote(Backend) + " --daemon --offload-mode " + Quote(offloadMode), true);
        }

        public static ProcessStartInfo BuildLimitsStartInfo()
        {
            return CreatePythonStartInfo("-u " + Quote(Backend) + " --limits", false);
        }

        private static ProcessStartInfo CreatePythonStartInfo(string arguments, bool redirectInput)
        {
            ProcessStartInfo psi = new ProcessStartInfo(PythonExe);
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = redirectInput;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.WorkingDirectory = Workspace;
            return psi;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static bool SelfTestQuoting(out string details)
        {
            string[] cases = { @"D:\", @"D:\a b\", "D:\\a\"b" };
            details = "";
            foreach (string value in cases)
            {
                string commandLine = "\"prog\" " + Quote(value);
                int argc;
                IntPtr argv = CommandLineToArgvW(commandLine, out argc);
                if (argv == IntPtr.Zero)
                {
                    details = "CommandLineToArgvW failed for " + value;
                    return false;
                }
                string parsed = argc >= 2 ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, IntPtr.Size)) : null;
                LocalFree(argv);
                if (argc != 2 || parsed != value)
                {
                    details = "quote round-trip failed: input=<" + value + "> argc=" + argc +
                              " parsed=<" + parsed + ">";
                    return false;
                }
            }
            details = "quote round-trip OK (" + cases.Length + " cases)";
            return true;
        }
    }
}
