using System;
using System.Diagnostics;
using System.IO;

namespace AIMusicGenerator
{
    internal static class AudioEditRunner
    {
        public static int Run(ProcessStartInfo startInfo, Action<string> log)
        {
            if (!File.Exists(Paths.AudioEditTool))
            {
                log("=== 找不到音訊工具: " + Paths.AudioEditTool + " ===");
                return 2;
            }
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) log(e.Data);
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) log("[audio] " + e.Data);
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
                log("=== 音訊工具啟動失敗: " + ex.Message + " ===");
                return 1;
            }
        }
    }
}
