using System;
using System.Diagnostics;

namespace AIMusicGenerator
{
    internal sealed class GpuLease : IDisposable
    {
        private readonly Process process;

        private GpuLease(Process process)
        {
            this.process = process;
        }

        public static GpuLease Acquire(string source, int waitSeconds, out string error)
        {
            error = "";
            Process process = new Process();
            try
            {
                process.StartInfo = Paths.BuildGpuHoldStartInfo(source, waitSeconds);
                process.EnableRaisingEvents = false;
                process.Start();
                string line = process.StandardOutput.ReadLine();
                if (line != null && line.StartsWith("[locked]"))
                    return new GpuLease(process);
                error = line == null ? "no response" : line.Trim();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception)
            {
            }
            process.Dispose();
            return null;
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("quit");
                    process.StandardInput.Flush();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
            }
            catch (Exception)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch (Exception)
                {
                }
            }
            process.Dispose();
        }
    }
}
