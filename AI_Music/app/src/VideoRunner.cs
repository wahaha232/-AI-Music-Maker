using System;
using System.Diagnostics;
using System.IO;

namespace AIMusicGenerator
{
    internal static class VideoRunner
    {
        public static int Run(string audioPath, string imagePath, Action<string> log)
        {
            if (!File.Exists(Paths.VideoTool))
            {
                log("=== 找不到影片轉換腳本: " + Paths.VideoTool + " ===");
                return 2;
            }
            if (!File.Exists(Paths.PythonExe))
            {
                log("=== 找不到 Python: " + Paths.PythonExe + " ===");
                return 2;
            }
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = Paths.BuildVideoStartInfo(audioPath, imagePath);
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) log(e.Data);
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) log("[video] " + e.Data);
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                log("=== 影片轉換啟動失敗: " + ex.Message + " ===");
                return 1;
            }
        }
    }
}
