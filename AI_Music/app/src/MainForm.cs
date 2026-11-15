using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace AIMusicGenerator
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox txtCaption = new TextBox();
        private readonly TextBox txtLyrics = new TextBox();
        private readonly TextBox txtOutDir = new TextBox();
        private readonly TextBox txtSeed = new TextBox();
        private readonly TextBox txtLog = new TextBox();
        private readonly NumericUpDown numBpm = new NumericUpDown();
        private readonly NumericUpDown numDuration = new NumericUpDown();
        private readonly NumericUpDown numSteps = new NumericUpDown();
        private readonly NumericUpDown numGuidance = new NumericUpDown();
        private readonly ComboBox comboKey = new ComboBox();
        private readonly ComboBox comboSignature = new ComboBox();
        private readonly ComboBox comboLanguage = new ComboBox();
        private readonly ComboBox comboFormat = new ComboBox();
        private readonly ComboBox comboOffload = new ComboBox();
        private readonly CheckBox chkAutoBpm = new CheckBox();
        private readonly CheckBox chkInstrumental = new CheckBox();
        private readonly CheckBox chkThinking = new CheckBox();
        private readonly Button btnGenerate = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnOpen = new Button();
        private readonly Button btnBrowse = new Button();
        private readonly ProgressBar bar = new ProgressBar();
        private readonly Label lblStatus = new Label();
        private readonly WinTimer finishTimer = new WinTimer();
        private readonly WinTimer logTimer = new WinTimer();
        private readonly WinTimer elapsedTimer = new WinTimer();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly Queue<string> logQueue = new Queue<string>();
        private readonly object logGate = new object();
        private Process daemon;
        private bool daemonReady;
        private bool jobRunning;
        private bool cancelRequested;
        private bool restartRequested;
        private bool closing;
        private bool lastLogWasProgress;
        private string daemonOffloadMode = "auto";
        private Dictionary<string, object> pendingConfig;
        private string lastOutputFile = "";
        private string baseStatus = "";
        private bool exitOk;
        private int exitCode = -1;
        private DateTime jobStarted;
        private int maxDurationWithLm = 480;
        private WinTimer uiTestTimer;
        private int uiTestState;
        private int uiTestTicks;
        internal int UiTestResult = -1;
        private const string DefaultCaption =
            "Warm lo-fi chill instrumental with mellow Rhodes piano, soft hip-hop drums, " +
            "warm bass and vinyl crackle. Instrumental only, no vocals.";

        public MainForm()
        {
            Text = "AI Music Generator - ACE-Step 1.5";
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(960, 776);
            MinimumSize = new Size(820, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            LoadSettings();
            finishTimer.Interval = 700;
            finishTimer.Tick += OnFinishTimer;
            logTimer.Interval = 100;
            logTimer.Tick += OnLogTimer;
            logTimer.Start();
            elapsedTimer.Interval = 1000;
            elapsedTimer.Tick += OnElapsedTimer;
            CleanupTempConfigs();
            LoadLimitsAsync();
        }

        private void BuildUi()
        {
            Label lblCaption = MakeLabel("音樂描述 (Caption)", 12, 12);
            txtCaption.SetBounds(12, 34, 924, 86);
            txtCaption.Multiline = true;
            txtCaption.ScrollBars = ScrollBars.Vertical;
            txtCaption.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label lblLyrics = MakeLabel("歌詞 (純音樂可留 [Instrumental])", 12, 128);
            txtLyrics.SetBounds(12, 150, 924, 52);
            txtLyrics.Multiline = true;
            txtLyrics.ScrollBars = ScrollBars.Vertical;
            txtLyrics.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label lblBpm = MakeLabel("BPM", 12, 214);
            numBpm.SetBounds(56, 210, 70, 26);
            numBpm.Minimum = 20;
            numBpm.Maximum = 300;
            numBpm.Value = 78;
            chkAutoBpm.SetBounds(130, 212, 58, 24);
            chkAutoBpm.Text = "自動";
            chkAutoBpm.Checked = true;
            chkAutoBpm.CheckedChanged += OnAutoBpmChanged;

            Label lblDuration = MakeLabel("長度(秒)", 196, 214);
            numDuration.SetBounds(266, 210, 76, 26);
            numDuration.Minimum = 5;
            numDuration.Maximum = 600;
            numDuration.Value = 60;

            Label lblSteps = MakeLabel("步數", 352, 214);
            numSteps.SetBounds(400, 210, 60, 26);
            numSteps.Minimum = 1;
            numSteps.Maximum = 100;
            numSteps.Value = 8;

            Label lblSeed = MakeLabel("種子 (空白=隨機)", 472, 214);
            txtSeed.SetBounds(600, 210, 96, 26);

            chkInstrumental.SetBounds(704, 212, 96, 24);
            chkInstrumental.Text = "純音樂";
            chkInstrumental.Checked = true;

            chkThinking.SetBounds(804, 212, 150, 24);
            chkThinking.Text = "LM 思考 (較慢)";
            chkThinking.Checked = true;
            chkThinking.CheckedChanged += OnThinkingChanged;

            Label lblKey = MakeLabel("調性", 12, 250);
            comboKey.SetBounds(56, 246, 120, 26);
            comboKey.DropDownStyle = ComboBoxStyle.DropDownList;
            comboKey.Items.AddRange(new object[]
            {
                "自動", "C Major", "G Major", "D Major", "A Major", "E Major",
                "F Major", "A Minor", "E Minor", "B Minor", "D Minor"
            });
            comboKey.SelectedIndex = 0;

            Label lblSignature = MakeLabel("拍號", 192, 250);
            comboSignature.SetBounds(240, 246, 60, 26);
            comboSignature.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSignature.Items.AddRange(new object[] { "自動", "4", "3", "6" });
            comboSignature.SelectedIndex = 0;

            Label lblGuidance = MakeLabel("Guidance", 316, 250);
            numGuidance.SetBounds(402, 246, 80, 26);
            numGuidance.DecimalPlaces = 2;
            numGuidance.Increment = 0.1m;
            numGuidance.Minimum = 0.1m;
            numGuidance.Maximum = 20m;
            numGuidance.Value = 1.0m;

            Label lblLanguage = MakeLabel("語言", 510, 250);
            comboLanguage.SetBounds(558, 246, 110, 26);
            comboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            comboLanguage.Items.AddRange(new object[]
            {
                "unknown", "en", "zh", "ja", "ko", "es", "fr", "de"
            });
            comboLanguage.SelectedIndex = 0;

            Label lblFormat = MakeLabel("輸出格式", 12, 286);
            comboFormat.SetBounds(86, 282, 90, 26);
            comboFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFormat.Items.AddRange(new object[] { "wav", "flac", "mp3" });
            comboFormat.SelectedIndex = 0;

            Label lblOffload = MakeLabel("Offload", 192, 286);
            comboOffload.SetBounds(266, 282, 110, 26);
            comboOffload.DropDownStyle = ComboBoxStyle.DropDownList;
            comboOffload.Items.AddRange(new object[] { "auto", "save_vram" });
            comboOffload.SelectedIndex = 0;

            Label lblOffloadHint = MakeLabel("(於下次模型載入生效)", 388, 286);
            lblOffloadHint.ForeColor = Color.DimGray;

            Label lblOut = MakeLabel("輸出資料夾", 12, 318);
            txtOutDir.SetBounds(12, 340, 810, 26);
            txtOutDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnBrowse.SetBounds(832, 338, 104, 30);
            btnBrowse.Text = "瀏覽...";
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Click += OnBrowse;

            btnGenerate.SetBounds(12, 374, 150, 40);
            btnGenerate.Text = "生成音樂";
            btnGenerate.Font = new Font(Font, FontStyle.Bold);
            btnGenerate.Click += OnGenerate;

            btnCancel.SetBounds(170, 374, 100, 40);
            btnCancel.Text = "取消";
            btnCancel.Enabled = false;
            btnCancel.Click += OnCancel;

            btnOpen.SetBounds(278, 374, 130, 40);
            btnOpen.Text = "開啟資料夾";
            btnOpen.Enabled = false;
            btnOpen.Click += OnOpenFolder;

            lblStatus.SetBounds(420, 384, 516, 22);
            lblStatus.Text = "待命 - 首次載入模型約需 1-5 分鐘，載入後保持常駐";
            lblStatus.AutoEllipsis = true;
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            bar.SetBounds(12, 424, 924, 14);
            bar.Style = ProgressBarStyle.Blocks;
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label lblLog = MakeLabel("執行日誌", 12, 446);
            txtLog.SetBounds(12, 468, 924, 296);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.WordWrap = true;
            txtLog.BackColor = Color.FromArgb(24, 24, 24);
            txtLog.ForeColor = Color.Gainsboro;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Controls.AddRange(new Control[]
            {
                lblCaption, txtCaption, lblLyrics, txtLyrics,
                lblBpm, numBpm, chkAutoBpm, lblDuration, numDuration,
                lblSteps, numSteps, lblSeed, txtSeed, chkInstrumental, chkThinking,
                lblKey, comboKey, lblSignature, comboSignature, lblGuidance, numGuidance,
                lblLanguage, comboLanguage, lblFormat, comboFormat,
                lblOffload, comboOffload, lblOffloadHint,
                lblOut, txtOutDir, btnBrowse,
                btnGenerate, btnCancel, btnOpen, lblStatus, bar, lblLog, txtLog
            });

            FormClosing += OnFormClosing;
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }

        private void OnAutoBpmChanged(object sender, EventArgs e)
        {
            numBpm.Enabled = !jobRunning && !chkAutoBpm.Checked;
        }

        private void OnThinkingChanged(object sender, EventArgs e)
        {
            ApplyDurationLimit();
        }

        private void ApplyDurationLimit()
        {
            decimal limit = chkThinking.Checked ? maxDurationWithLm : 600;
            numDuration.Maximum = limit;
            if (numDuration.Value > limit) numDuration.Value = limit;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "選擇輸出資料夾";
                string current = txtOutDir.Text.Trim();
                if (Directory.Exists(current)) dialog.SelectedPath = current;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtOutDir.Text = dialog.SelectedPath;
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            string dir = txtOutDir.Text.Trim();
            if (lastOutputFile.Length > 0 && File.Exists(lastOutputFile))
            {
                Process.Start("explorer.exe", "/select," + Paths.Quote(lastOutputFile));
                return;
            }
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", Paths.Quote(dir));
        }

        private void OnGenerate(object sender, EventArgs e)
        {
            if (jobRunning) return;

            string caption = txtCaption.Text.Trim();
            if (caption.Length == 0)
            {
                MessageBox.Show(this, "請輸入音樂描述。", "缺少描述", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int seed = -1;
            string seedText = txtSeed.Text.Trim();
            if (seedText.Length > 0)
            {
                if (!int.TryParse(seedText, out seed))
                {
                    MessageBox.Show(this, "種子必須是整數，或留空表示隨機。", "種子格式錯誤",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (seed < 0)
                {
                    MessageBox.Show(this, "種子不可為負數；請留空表示隨機。", "種子格式錯誤",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (!File.Exists(Paths.PythonExe) || !File.Exists(Paths.Backend))
            {
                MessageBox.Show(this, "找不到 Python 環境或生成腳本：\n" + Paths.Workspace,
                    "環境不完整", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!Directory.Exists(txtOutDir.Text.Trim()))
            {
                try { Directory.CreateDirectory(txtOutDir.Text.Trim()); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "無法建立輸出資料夾：" + ex.Message,
                        "路徑錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            SaveSettings();
            finishTimer.Stop();
            lastOutputFile = "";
            txtLog.Clear();
            lastLogWasProgress = false;
            exitOk = false;
            exitCode = -1;
            cancelRequested = false;
            restartRequested = false;

            Dictionary<string, object> config = new Dictionary<string, object>();
            config["caption"] = caption;
            config["lyrics"] = txtLyrics.Text.Trim().Length == 0 ? "[Instrumental]" : txtLyrics.Text.Trim();
            config["instrumental"] = chkInstrumental.Checked;
            config["thinking"] = chkThinking.Checked;
            config["bpm"] = chkAutoBpm.Checked ? null : (object)(int)numBpm.Value;
            config["duration"] = (double)numDuration.Value;
            config["inference_steps"] = (int)numSteps.Value;
            config["guidance_scale"] = (double)numGuidance.Value;
            config["keyscale"] = comboKey.SelectedIndex <= 0 ? "" : Convert.ToString(comboKey.SelectedItem);
            config["timesignature"] = comboSignature.SelectedIndex <= 0 ? "" : Convert.ToString(comboSignature.SelectedItem);
            config["vocal_language"] = Convert.ToString(comboLanguage.SelectedItem);
            config["audio_format"] = Convert.ToString(comboFormat.SelectedItem);
            config["offload_mode"] = Convert.ToString(comboOffload.SelectedItem);
            config["seed"] = seed;
            config["output_dir"] = txtOutDir.Text.Trim();
            config["filename_prefix"] = "ace";

            jobRunning = true;
            jobStarted = DateTime.Now;
            elapsedTimer.Start();
            SetBusy(true);
            bar.Style = ProgressBarStyle.Marquee;
            AppendLog("=== 開始生成 ===");
            AppendLog("描述: " + caption);

            string offloadMode = Convert.ToString(config["offload_mode"]);
            if (daemon == null)
            {
                pendingConfig = config;
                StartDaemon(offloadMode);
            }
            else if (daemonReady)
            {
                if (daemonOffloadMode != offloadMode)
                {
                    pendingConfig = config;
                    restartRequested = true;
                    SetStatus("切換 offload 模式，重新載入模型...");
                    KillDaemon();
                }
                else
                {
                    SendJob(config);
                }
            }
            else
            {
                pendingConfig = config;
                SetStatus("模型載入中，完成後自動開始生成...");
            }
        }

        private void StartDaemon(string offloadMode)
        {
            try
            {
                daemonOffloadMode = offloadMode == "save_vram" ? "save_vram" : "auto";
                ProcessStartInfo psi = Paths.BuildDaemonStartInfo(daemonOffloadMode);
                daemon = new Process();
                daemon.StartInfo = psi;
                daemon.EnableRaisingEvents = true;
                daemon.OutputDataReceived += OnOutput;
                daemon.ErrorDataReceived += OnError;
                daemon.Exited += OnDaemonExited;
                daemon.Start();
                daemon.BeginOutputReadLine();
                daemon.BeginErrorReadLine();
                AppendLog("=== 啟動模型常駐程序（載入後保持，offload=" + daemonOffloadMode + "）===");
                SetStatus("載入模型中...");
            }
            catch (Exception ex)
            {
                daemon = null;
                jobRunning = false;
                pendingConfig = null;
                elapsedTimer.Stop();
                SetBusy(false);
                bar.Style = ProgressBarStyle.Blocks;
                MessageBox.Show(this, "無法啟動生成程序：" + ex.Message, "啟動失敗",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendJob(Dictionary<string, object> config)
        {
            try
            {
                if (daemon == null || !daemonReady) return;
                daemon.StandardInput.WriteLine(AsciiJson(serializer.Serialize(config)));
                daemon.StandardInput.Flush();
                jobStarted = DateTime.Now;
                bar.Style = ProgressBarStyle.Marquee;
                AppendLog("=== 已送出生成指令（模型沿用中）===");
                SetStatus("生成中...");
            }
            catch (Exception ex)
            {
                AppendLog("送出指令失敗: " + ex.Message);
                SetStatus("送出指令失敗");
                jobRunning = false;
                elapsedTimer.Stop();
                SetBusy(false);
                bar.Style = ProgressBarStyle.Blocks;
            }
        }

        private void OnCancel(object sender, EventArgs e)
        {
            if (daemon == null) return;
            cancelRequested = true;
            restartRequested = false;
            SetStatus("正在取消...");
            AppendLog("=== 已送出取消指令（模型將卸載，下次需重新載入）===");
            KillDaemon();
        }

        private void KillDaemon()
        {
            Process running = daemon;
            if (running == null) return;
            try
            {
                if (!running.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill",
                        "/PID " + running.Id + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception ex)
            {
                AppendLog("取消失敗: " + ex.Message);
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            PostLine(e.Data, false);
        }

        private void OnError(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            PostLine(e.Data, true);
        }

        private void PostLine(string line, bool isError)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action<string, bool>(HandleLine), line, isError);
            }
            catch (InvalidOperationException)
            {
                // form is closing
            }
        }

        private void HandleLine(string line, bool isError)
        {
            string clean = Regex.Replace(line, "\\x1B\\[[0-9;]*m", "");
            if (isError) clean = clean.Replace("\t", "    ");
            EnqueueLog(clean);

            if (clean.StartsWith("[stage]"))
            {
                SetStatus(clean.Substring(7).Trim());
            }
            else if (clean.StartsWith("[progress]"))
            {
                HandleProgress(clean.Substring(10).Trim());
            }
            else if (clean.StartsWith("[error]"))
            {
                SetStatus("錯誤: " + clean.Substring(7).Trim());
            }
            else if (clean.StartsWith("[done]"))
            {
                lastOutputFile = clean.Substring(6).Trim();
            }
            else if (clean.StartsWith("[ready]"))
            {
                daemonReady = true;
                AppendLog("=== 模型就緒 ===");
                if (pendingConfig != null)
                {
                    Dictionary<string, object> queued = pendingConfig;
                    pendingConfig = null;
                    SendJob(queued);
                }
                else
                {
                    SetStatus("模型就緒");
                }
            }
            else if (clean.StartsWith("[idle]"))
            {
                int code = 1;
                int marker = clean.IndexOf("code=", StringComparison.Ordinal);
                if (marker >= 0) int.TryParse(clean.Substring(marker + 5).Trim(), out code);
                jobRunning = false;
                exitCode = code;
                exitOk = code == 0;
                bar.Style = ProgressBarStyle.Blocks;
                bar.Value = 0;
                elapsedTimer.Stop();
                SetBusy(false);
                SetStatus("收尾中...");
                finishTimer.Start();
            }
        }

        private void HandleProgress(string payload)
        {
            int space = payload.IndexOf(' ');
            string number = space < 0 ? payload : payload.Substring(0, space);
            string desc = space < 0 ? "" : payload.Substring(space + 1).Trim();
            int percent;
            if (!int.TryParse(number, out percent)) return;
            percent = Math.Max(0, Math.Min(100, percent));
            bar.Style = ProgressBarStyle.Blocks;
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Value = percent;
            SetStatus((desc.Length > 0 ? desc + " " : "") + percent + "%");
        }

        private void OnDaemonExited(object sender, EventArgs e)
        {
            if (closing || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(HandleDaemonExit));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void HandleDaemonExit()
        {
            Process finished = daemon;
            daemon = null;
            daemonReady = false;
            finishTimer.Stop();

            if (restartRequested)
            {
                restartRequested = false;
                if (finished != null)
                {
                    try { finished.WaitForExit(); finished.WaitForExit(); }
                    catch (Exception) { }
                    finished.Dispose();
                }
                string mode = "auto";
                if (pendingConfig != null && pendingConfig.ContainsKey("offload_mode"))
                    mode = Convert.ToString(pendingConfig["offload_mode"]);
                SetBusy(true);
                StartDaemon(mode);
                return;
            }

            bool wasRunning = jobRunning;
            pendingConfig = null;
            jobRunning = false;
            elapsedTimer.Stop();
            int code = -1;
            if (finished != null)
            {
                try
                {
                    finished.WaitForExit();
                    finished.WaitForExit();
                    code = finished.ExitCode;
                }
                catch (Exception)
                {
                }
                finished.Dispose();
            }

            bar.Style = ProgressBarStyle.Blocks;
            bar.Value = 0;
            SetBusy(false);

            if (cancelRequested)
            {
                SetStatus("已取消（模型已卸載）");
                AppendLog("=== 已取消 ===");
                cancelRequested = false;
            }
            else if (wasRunning)
            {
                SetStatus("模型程序意外結束 (exit " + code + ")");
                AppendLog("=== 模型程序意外結束 (exit " + code + ") ===");
            }
            else
            {
                SetStatus("模型程序已結束");
                AppendLog("=== 模型程序已結束 ===");
            }
        }

        private void OnFinishTimer(object sender, EventArgs e)
        {
            finishTimer.Stop();
            if (cancelRequested)
            {
                SetStatus("已取消");
                cancelRequested = false;
                return;
            }
            if (exitOk && lastOutputFile.Length > 0)
            {
                SetStatus("完成: " + Path.GetFileName(lastOutputFile));
                AppendLog("=== 完成: " + lastOutputFile + " ===");
            }
            else if (exitOk)
            {
                SetStatus("完成（請到輸出資料夾查看）");
                AppendLog("=== 完成 ===");
            }
            else
            {
                SetStatus("失敗 (exit " + exitCode + ") - 詳見日誌");
                AppendLog("=== 生成失敗 (exit " + exitCode + ") ===");
            }
        }

        private void OnElapsedTimer(object sender, EventArgs e)
        {
            if (!jobRunning) return;
            UpdateStatusLabel();
        }

        private void SetStatus(string text)
        {
            baseStatus = text;
            UpdateStatusLabel();
        }

        private void UpdateStatusLabel()
        {
            string suffix = "";
            if (jobRunning)
            {
                TimeSpan elapsed = DateTime.Now - jobStarted;
                suffix = "  [已耗時 " +
                         ((int)elapsed.TotalMinutes).ToString("00") + ":" +
                         elapsed.Seconds.ToString("00") + "]";
            }
            lblStatus.Text = baseStatus + suffix;
        }

        private void SetBusy(bool busy)
        {
            btnGenerate.Enabled = !busy;
            btnCancel.Enabled = busy;
            btnBrowse.Enabled = !busy;
            btnOpen.Enabled = !busy && (lastOutputFile.Length > 0 || Directory.Exists(txtOutDir.Text.Trim()));
            txtCaption.Enabled = !busy;
            txtLyrics.Enabled = !busy;
            txtOutDir.Enabled = !busy;
            txtSeed.Enabled = !busy;
            numBpm.Enabled = !busy && !chkAutoBpm.Checked;
            numDuration.Enabled = !busy;
            numSteps.Enabled = !busy;
            numGuidance.Enabled = !busy;
            chkAutoBpm.Enabled = !busy;
            comboKey.Enabled = !busy;
            comboSignature.Enabled = !busy;
            comboLanguage.Enabled = !busy;
            comboFormat.Enabled = !busy;
            comboOffload.Enabled = !busy;
            chkInstrumental.Enabled = !busy;
            chkThinking.Enabled = !busy;
        }

        private void EnqueueLog(string text)
        {
            bool progress = text.IndexOf('\r') >= 0;
            if (progress)
            {
                string[] parts = text.Split('\r');
                string last = "";
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (parts[i].Trim().Length > 0)
                    {
                        last = parts[i];
                        break;
                    }
                }
                if (last.Length == 0) return;
                text = last;
            }
            lock (logGate)
            {
                if (logQueue.Count > 4000)
                {
                    logQueue.Clear();
                    logQueue.Enqueue("[...日誌過多，已丟棄...]");
                }
                logQueue.Enqueue((progress ? "\u0001" : "") + text);
            }
        }

        private void AppendLog(string text)
        {
            EnqueueLog(text);
        }

        private void OnLogTimer(object sender, EventArgs e)
        {
            List<string> items = new List<string>();
            lock (logGate)
            {
                while (logQueue.Count > 0 && items.Count < 200)
                    items.Add(logQueue.Dequeue());
            }
            if (items.Count == 0) return;
            txtLog.SuspendLayout();
            foreach (string item in items)
            {
                bool progress = item.Length > 0 && item[0] == '\u0001';
                AppendLogLine(progress ? item.Substring(1) : item, progress);
            }
            txtLog.ResumeLayout();
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void AppendLogLine(string text, bool progress)
        {
            if (txtLog.TextLength > 500000)
            {
                string current = txtLog.Text;
                int start = Math.Max(0, current.Length - 250000);
                int newline = current.IndexOf('\n', start);
                if (newline >= 0) start = newline + 1;
                txtLog.Text = "[...舊日誌已截斷...]" + Environment.NewLine + current.Substring(start);
                lastLogWasProgress = false;
            }
            if (progress && lastLogWasProgress)
            {
                string current = txtLog.Text;
                int end = current.Length;
                while (end > 0 && (current[end - 1] == '\n' || current[end - 1] == '\r')) end--;
                int cut = current.LastIndexOf('\n', Math.Max(0, end - 1));
                txtLog.Text = cut >= 0 ? current.Substring(0, cut + 1) : "";
            }
            txtLog.AppendText(text + Environment.NewLine);
            lastLogWasProgress = progress;
        }

        private void LoadLimitsAsync()
        {
            Thread worker = new Thread(delegate()
            {
                try
                {
                    ProcessStartInfo psi = Paths.BuildLimitsStartInfo();
                    using (Process process = new Process())
                    {
                        process.StartInfo = psi;
                        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
                        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data == null || !e.Data.StartsWith("[limits]")) return;
                            string[] tokens = e.Data.Substring(8).Trim().Split(' ');
                            foreach (string token in tokens)
                            {
                                if (!token.StartsWith("max_duration_with_lm=")) continue;
                                int value;
                                if (int.TryParse(token.Substring("max_duration_with_lm=".Length), out value) && value > 0)
                                {
                                    int captured = value;
                                    try
                                    {
                                        BeginInvoke(new Action(delegate()
                                        {
                                            maxDurationWithLm = captured;
                                            ApplyDurationLimit();
                                        }));
                                    }
                                    catch (InvalidOperationException)
                                    {
                                    }
                                }
                            }
                        };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                        process.WaitForExit();
                    }
                }
                catch (Exception)
                {
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void CleanupTempConfigs()
        {
            try
            {
                if (!Directory.Exists(Paths.TempDir)) return;
                DateTime cutoff = DateTime.Now.AddDays(-7);
                foreach (FileInfo file in new DirectoryInfo(Paths.TempDir).GetFiles("config_*.json"))
                {
                    if (file.LastWriteTime < cutoff)
                    {
                        try { file.Delete(); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void MigrateLegacySettings()
        {
            try
            {
                if (File.Exists(Paths.SettingsFile)) return;
                if (!File.Exists(Paths.LegacySettingsFile)) return;
                Directory.CreateDirectory(Paths.SettingsDir);
                File.Move(Paths.LegacySettingsFile, Paths.SettingsFile);
            }
            catch (Exception)
            {
            }
        }

        private void LoadSettings()
        {
            MigrateLegacySettings();
            Dictionary<string, object> s = new Dictionary<string, object>();
            try
            {
                if (File.Exists(Paths.SettingsFile))
                    s = serializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(Paths.SettingsFile, Encoding.UTF8));
            }
            catch (Exception)
            {
                s = new Dictionary<string, object>();
            }
            txtCaption.Text = GetString(s, "caption", DefaultCaption);
            txtLyrics.Text = GetString(s, "lyrics", "[Instrumental]");
            txtOutDir.Text = GetString(s, "output_dir", Paths.Generated);
            txtSeed.Text = GetString(s, "seed", "");
            chkInstrumental.Checked = GetBool(s, "instrumental", true);
            chkThinking.Checked = GetBool(s, "thinking", true);
            chkAutoBpm.Checked = GetBool(s, "auto_bpm", true);
            SelectCombo(comboKey, GetString(s, "keyscale", ""));
            SelectCombo(comboSignature, GetString(s, "timesignature", ""));
            SelectCombo(comboLanguage, GetString(s, "vocal_language", "unknown"));
            SelectCombo(comboFormat, GetString(s, "audio_format", "wav"));
            SelectCombo(comboOffload, GetString(s, "offload_mode", "auto"));
            numBpm.Value = Clamp(GetDecimal(s, "bpm", 78m), numBpm.Minimum, numBpm.Maximum);
            numDuration.Value = Clamp(GetDecimal(s, "duration", 60m), numDuration.Minimum, numDuration.Maximum);
            numSteps.Value = Clamp(GetDecimal(s, "inference_steps", 8m), numSteps.Minimum, numSteps.Maximum);
            numGuidance.Value = Clamp(GetDecimal(s, "guidance_scale", 1.0m),
                numGuidance.Minimum, numGuidance.Maximum);
            ApplyDurationLimit();
            numBpm.Enabled = !chkAutoBpm.Checked;
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Paths.SettingsDir);
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["caption"] = txtCaption.Text;
                s["lyrics"] = txtLyrics.Text;
                s["output_dir"] = txtOutDir.Text;
                s["seed"] = txtSeed.Text;
                s["bpm"] = (int)numBpm.Value;
                s["auto_bpm"] = chkAutoBpm.Checked;
                s["duration"] = (double)numDuration.Value;
                s["inference_steps"] = (int)numSteps.Value;
                s["guidance_scale"] = (double)numGuidance.Value;
                s["keyscale"] = comboKey.SelectedIndex <= 0 ? "" : Convert.ToString(comboKey.SelectedItem);
                s["timesignature"] = comboSignature.SelectedIndex <= 0 ? "" : Convert.ToString(comboSignature.SelectedItem);
                s["vocal_language"] = Convert.ToString(comboLanguage.SelectedItem);
                s["audio_format"] = Convert.ToString(comboFormat.SelectedItem);
                s["offload_mode"] = Convert.ToString(comboOffload.SelectedItem);
                s["instrumental"] = chkInstrumental.Checked;
                s["thinking"] = chkThinking.Checked;
                File.WriteAllText(Paths.SettingsFile, serializer.Serialize(s), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // settings are optional
            }
        }

        private static void SelectCombo(ComboBox combo, string value)
        {
            int index = combo.FindStringExact(value);
            if (index < 0 && string.IsNullOrEmpty(value)) index = 0;
            combo.SelectedIndex = index >= 0 ? index : 0;
        }

        private static string AsciiJson(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch <= 0x7F) builder.Append(ch);
                else builder.Append("\\u").Append(((int)ch).ToString("x4"));
            }
            return builder.ToString();
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string GetString(Dictionary<string, object> s, string key, string fallback)
        {
            object value;
            if (s.TryGetValue(key, out value) && value != null) return Convert.ToString(value);
            return fallback;
        }

        private static bool GetBool(Dictionary<string, object> s, string key, bool fallback)
        {
            object value;
            if (s.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToBoolean(value); }
                catch (Exception) { }
            }
            return fallback;
        }

        private static decimal GetDecimal(Dictionary<string, object> s, string key, decimal fallback)
        {
            object value;
            if (s.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToDecimal(value); }
                catch (Exception) { }
            }
            return fallback;
        }


        internal void StartUiTest()
        {
            Shown += delegate
            {
                WindowState = FormWindowState.Minimized;
                uiTestTimer = new WinTimer();
                uiTestTimer.Interval = 1000;
                uiTestTimer.Tick += OnUiTestTick;
                uiTestTimer.Start();
            };
        }

        private void OnUiTestTick(object sender, EventArgs e)
        {
            uiTestTicks++;
            try
            {
                if (uiTestState == 0)
                {
                    uiTestState = 1;
                    uiTestTicks = 0;
                    AppendLog("=== UI TEST: generate ===");
                    OnGenerate(this, EventArgs.Empty);
                }
                else if (uiTestState == 1)
                {
                    if (lblStatus.Text.Contains("完成:"))
                    {
                        uiTestState = 2;
                        uiTestTicks = 0;
                        AppendLog("=== UI TEST: first generation complete ===");
                        OnGenerate(this, EventArgs.Empty);
                    }
                    else if (uiTestTicks > 900 || lblStatus.Text.Contains("失敗"))
                    {
                        FinishUiTest(2);
                    }
                }
                else if (uiTestState == 2)
                {
                    if (uiTestTicks >= 5)
                    {
                        uiTestState = 3;
                        uiTestTicks = 0;
                        AppendLog("=== UI TEST: cancel ===");
                        OnCancel(this, EventArgs.Empty);
                    }
                }
                else if (uiTestState == 3)
                {
                    if (lblStatus.Text.Contains("已取消"))
                    {
                        FinishUiTest(0);
                    }
                    else if (uiTestTicks > 60)
                    {
                        FinishUiTest(3);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("UI TEST error: " + ex.Message);
                FinishUiTest(4);
            }
        }

        private void FinishUiTest(int code)
        {
            UiTestResult = code;
            try
            {
                uiTestTimer.Stop();
                Directory.CreateDirectory(Paths.TempDir);
                string body = "result=" + code + Environment.NewLine +
                              "status=" + lblStatus.Text + Environment.NewLine +
                              "log=" + txtLog.Text;
                File.WriteAllText(Path.Combine(Paths.TempDir, "ui_test_result.txt"),
                    body, new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
            Close();
        }
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            closing = true;
            finishTimer.Stop();
            logTimer.Stop();
            elapsedTimer.Stop();
            SaveSettings();
            Process running = daemon;
            daemon = null;
            if (running != null)
            {
                try
                {
                    if (!running.HasExited)
                    {
                        try
                        {
                            running.StandardInput.WriteLine("quit");
                            running.StandardInput.Flush();
                        }
                        catch (Exception)
                        {
                        }
                        Process.Start(new ProcessStartInfo("taskkill",
                            "/PID " + running.Id + " /T /F")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
