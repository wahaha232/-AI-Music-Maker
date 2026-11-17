using System;
using System.Diagnostics;

namespace AIMusicGenerator
{
    internal static class ProcessSupervisor
    {
        public static bool StopTree(Process process, int waitMilliseconds)
        {
            if (process == null) return true;
            try
            {
                if (!process.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill",
                        "/PID " + process.Id + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                if (!process.WaitForExit(Math.Max(0, waitMilliseconds)))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                    catch (Exception)
                    {
                    }
                }
                return process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
