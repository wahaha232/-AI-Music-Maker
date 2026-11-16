using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AIMusicGenerator
{
    internal static class AutoRunner
    {
        public static int Run(string configPath)
        {
            string logDir = Path.Combine(Path.GetTempPath(), "AIMusicGenerator");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "auto_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            int exitCode = -1;
            object gate = new object();
            using (StreamWriter writer = new StreamWriter(logPath, false, new UTF8Encoding(false)))
            {
                writer.AutoFlush = true;
                using (Process process = new Process())
                {
                    process.StartInfo = Paths.BuildStartInfo(Path.GetFullPath(configPath));
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (gate) { writer.WriteLine(StripAnsi(e.Data)); }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (gate) { writer.WriteLine("[stderr] " + StripAnsi(e.Data)); }
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                writer.WriteLine("[auto] exit=" + exitCode);
                writer.WriteLine("[auto] log=" + logPath);
            }
            return exitCode;
        }

        private static string StripAnsi(string text)
        {
            return Regex.Replace(text, "\\x1B\\[[0-9;]*m", "");
        }
    }
}
