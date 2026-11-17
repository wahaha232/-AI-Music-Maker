using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AIMusicGenerator
{
    internal static class SdCoverClient
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly object StartGate = new object();
        private static Process serverProcess;

        static SdCoverClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (Exception) { }
        }

        public static bool EnsureApi(string url, string installPath, Action<string> log)
        {
            lock (StartGate)
            {
            if (IsReady(url)) return true;
            string python = Path.Combine(installPath, "venv", "Scripts", "python.exe");
            string launch = Path.Combine(installPath, "launch.py");
            if (!File.Exists(python) || !File.Exists(launch))
            {
                if (log != null) log("找不到 Stable Diffusion WebUI: " + installPath);
                return false;
            }
            try
            {
                if (log != null) log("啟動 Stable Diffusion WebUI API...");
                int port = 7860;
                Uri parsed;
                if (Uri.TryCreate(url, UriKind.Absolute, out parsed) && !parsed.IsDefaultPort)
                    port = parsed.Port;
                ProcessStartInfo psi = new ProcessStartInfo(python);
                psi.Arguments = "\"" + launch + "\" --api --nowebui --lowvram --disable-extra-extensions --port " + port;
                psi.WorkingDirectory = installPath;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                serverProcess = new Process();
                serverProcess.StartInfo = psi;
                serverProcess.OutputDataReceived += delegate { };
                serverProcess.ErrorDataReceived += delegate { };
                serverProcess.Start();
                serverProcess.BeginOutputReadLine();
                serverProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                if (log != null) log("SD 啟動失敗: " + ex.Message);
                return false;
            }
            for (int i = 0; i < 80; i++)
            {
                Thread.Sleep(3000);
                if (IsReady(url))
                {
                    if (log != null) log("SD API 就緒");
                    return true;
                }
            }
            if (log != null) log("SD API 啟動逾時");
            return false;
            }
        }

        public static bool IsReady(string url)
        {
            try
            {
                Get(url.TrimEnd('/') + "/sdapi/v1/options", 4000);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static List<string> GetModels(string url)
        {
            string json = Get(url.TrimEnd('/') + "/sdapi/v1/sd-models", 30000);
            List<Dictionary<string, object>> list =
                Serializer.Deserialize<List<Dictionary<string, object>>>(json);
            List<string> result = new List<string>();
            if (list != null)
            {
                foreach (Dictionary<string, object> item in list)
                {
                    object title;
                    if (item.TryGetValue("title", out title) && title != null)
                        result.Add(Convert.ToString(title));
                }
            }
            return result;
        }

        public static void SelectModel(string url, string title)
        {
            string body = Serializer.Serialize(new Dictionary<string, object>
            {
                { "sd_model_checkpoint", title }
            });
            PostJson(url.TrimEnd('/') + "/sdapi/v1/options", body, 300000);
        }

        public static void GenerateCover(string url, string prompt, string negative,
            int width, int height, int steps, string outputPath)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["prompt"] = prompt;
            payload["negative_prompt"] = negative;
            payload["steps"] = steps;
            payload["width"] = width;
            payload["height"] = height;
            payload["cfg_scale"] = 7;
            payload["sampler_name"] = "Euler a";
            payload["seed"] = -1;
            string json = PostJson(url.TrimEnd('/') + "/sdapi/v1/txt2img",
                Serializer.Serialize(payload), 900000);
            Dictionary<string, object> data = Serializer.Deserialize<Dictionary<string, object>>(json);
            object imagesObject;
            if (data == null || !data.TryGetValue("images", out imagesObject))
                throw new InvalidOperationException("SD 回應沒有 images");
            IEnumerable images = imagesObject as IEnumerable;
            if (images == null) throw new InvalidOperationException("SD 回應 images 格式錯誤");
            string base64 = null;
            foreach (object image in images)
            {
                base64 = Convert.ToString(image);
                break;
            }
            if (string.IsNullOrEmpty(base64)) throw new InvalidOperationException("SD 回應 images 為空");
            int comma = base64.IndexOf(',');
            if (comma >= 0 && base64.Substring(0, comma).Contains("base64"))
                base64 = base64.Substring(comma + 1);
            byte[] bytes = Convert.FromBase64String(base64);
            File.WriteAllBytes(outputPath, bytes);
        }

        public static void UnloadCheckpoint(string url)
        {
            try
            {
                PostJson(url.TrimEnd('/') + "/sdapi/v1/unload-checkpoint", "{}", 30000);
            }
            catch (Exception)
            {
            }
        }

        public static void Stop()
        {
            Process process = serverProcess;
            serverProcess = null;
            if (process == null) return;
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
            }
            catch (Exception) { }
            try { process.Dispose(); } catch (Exception) { }
        }

        private static string Get(string url, int timeout)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string PostJson(string url, string body, int timeout)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
