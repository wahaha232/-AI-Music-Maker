using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.Wave;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace AIMusicGenerator
{
    internal sealed partial class MainForm : Form
    {
        private readonly RichTextBox txtCaption = new RichTextBox();
        private readonly RichTextBox txtLyrics = new RichTextBox();
        private readonly TextBox txtOutDir = new TextBox();
        private readonly TextBox txtSeed = new TextBox();
        private readonly TextBox txtLog = new TextBox();
        private readonly NumericUpDown numBpm = new NumericUpDown();
        private readonly NumericUpDown numDuration = new NumericUpDown();
        private readonly NumericUpDown numHours = new NumericUpDown();
        private readonly NumericUpDown numMinutes = new NumericUpDown();
        private readonly NumericUpDown numSeconds = new NumericUpDown();
        private readonly NumericUpDown numSteps = new NumericUpDown();
        private readonly NumericUpDown numGuidance = new NumericUpDown();
        private readonly ComboBox comboKey = new ThemedComboBox();
        private readonly ComboBox comboSignature = new ThemedComboBox();
        private readonly ComboBox comboLanguage = new ThemedComboBox();
        private readonly ComboBox comboFormat = new ThemedComboBox();
        private readonly ComboBox comboOffload = new ThemedComboBox();
        private readonly ComboBox comboTheme = new ThemedComboBox();
        private readonly ComboBox comboStyleTemplate = new ThemedComboBox();
        private readonly ModernButton btnApplyTemplate = new ModernButton();
        private readonly ModernButton btnExpandPrompt = new ModernButton();
        private readonly ToggleSwitch chkAutoBpm = new ToggleSwitch();
        private readonly ToggleSwitch chkInstrumental = new ToggleSwitch();
        private readonly ToggleSwitch chkThinking = new ToggleSwitch();
        private readonly ToggleSwitch chkKeepModel = new ToggleSwitch();
        private readonly ToggleSwitch chkAutoVideo = new ToggleSwitch();
        private readonly Queue<KeyValuePair<string, string>> videoQueue =
            new Queue<KeyValuePair<string, string>>();
        private readonly object videoGate = new object();
        private bool videoRunning;
        private readonly ModernButton btnGenerate = new ModernButton();
        private readonly ModernButton btnCancel = new ModernButton();
        private readonly ModernButton btnOpen = new ModernButton();
        private readonly ModernButton btnPlay = new ModernButton();
        private readonly ModernButton btnLink = new ModernButton();
        private readonly ModernButton btnAdvanced = new ModernButton();
        private readonly ModernButton btnSd = new ModernButton();
        private readonly ModernButton btnTelegram = new ModernButton();
        private readonly ModernButton btnAlbum = new ModernButton();
        private readonly ModernButton btnMediaTools = new ModernButton();
        private Process albumProcess;
        private bool albumStarting;
        private bool albumCancelled;
        private string currentAlbumFolder = "";
        private Process telegramProcess;
        private bool sdEnabled = false;
        private string sdUrl = "http://127.0.0.1:7860";
        private string sdPath = @"G:\stable-diffusion-webui";
        private string sdModel = "";
        private int sdSteps = 20;
        private int sdSize = 512;
        private string negativePrompt = "";
        private decimal fadeInValue = 0m;
        private decimal fadeOutValue = 0m;
        private decimal shiftValue = 1.0m;
        private string mp3BitrateValue = "128k";
        private int mp3SampleRateValue = 48000;
        private readonly ModernButton btnPlayPause = new ModernButton();
        private readonly ModernButton btnStop = new ModernButton();
        private readonly SeekSlider seekBar = new SeekSlider();
        private readonly Label lblTime = new Label();
        private readonly WaveformPanel waveform = new WaveformPanel();
        private readonly WinTimer playbackTimer = new WinTimer();
        private readonly ToggleSwitch chkBatch = new ToggleSwitch();
        private readonly ListBox lstHistory = new ListBox();
        private readonly ModernButton btnBrowse = new ModernButton();
        private readonly ModernButton btnCover = new ModernButton();
        private readonly SlimProgressBar bar = new SlimProgressBar();
        private readonly Label lblStatus = new Label();
        private readonly WinTimer finishTimer = new WinTimer();
        private readonly WinTimer logTimer = new WinTimer();
        private readonly WinTimer elapsedTimer = new WinTimer();
        private readonly WinTimer daemonIdleTimer = new WinTimer();
        private DateTime lastJobActivity = DateTime.Now;
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
        private bool updatingDuration;
        private bool allowLongThinkingThisJob;
        private readonly Queue<Dictionary<string, object>> batchQueue = new Queue<Dictionary<string, object>>();
        private readonly List<string> batchOutputs = new List<string>();
        private string batchPrompt = "";
        private int batchTotal;
        private int batchCompleted;
        private int batchErrors;
        private bool batchActive;
        private bool softCancel;
        private WaveOutEvent audioOutput;
        private WaveStream audioStream;
        private string loadedAudioPath = "";
        private string coverPath = "";
        private bool scrubbing;
        private WinTimer uiTestTimer;
        private int uiTestState;
        private int uiTestTicks;
        internal int UiTestResult = -1;
        private bool uiTestBatch;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;
        private const string DefaultCaption =
            "Warm lo-fi chill instrumental with mellow Rhodes piano, soft hip-hop drums, " +
            "warm bass and vinyl crackle. Instrumental only, no vocals.";

        public MainForm()
        {
            Text = "AI Music Maker - ACE-Step 1.5";
            Font = ThemeManager.UiFont;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Dpi.S(1440, 900);
            MinimumSize = Dpi.S(1100, 660);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            BuildUi();
            TryLoadIcon();
            ApplyTheme();
            LoadSettings();
            RefreshHistory();
            finishTimer.Interval = 700;
            finishTimer.Tick += OnFinishTimer;
            logTimer.Interval = 100;
            logTimer.Tick += OnLogTimer;
            logTimer.Start();
            elapsedTimer.Interval = 1000;
            elapsedTimer.Tick += OnElapsedTimer;
            daemonIdleTimer.Interval = 5 * 60 * 1000;
            daemonIdleTimer.Tick += OnDaemonIdle;
            daemonIdleTimer.Start();
            playbackTimer.Interval = 200;
            playbackTimer.Tick += OnPlaybackTimer;
            CleanupTempConfigs();
            LoadLimitsAsync();
        }

        private void OnAutoBpmChanged(object sender, EventArgs e)
        {
            numBpm.Enabled = !jobRunning && !chkAutoBpm.Checked;
        }

        private void OnThinkingChanged(object sender, EventArgs e)
        {
            ApplyDurationLimit();
        }

        private void OnDurationPartsChanged(object sender, EventArgs e)
        {
            if (updatingDuration) return;
            int hours = (int)numHours.Value;
            int minutes = (int)numMinutes.Value;
            int seconds = (int)numSeconds.Value;
            if (hours >= 2)
            {
                hours = 2;
                minutes = 0;
                seconds = 0;
            }
            int total = hours * 3600 + minutes * 60 + seconds;
            if (total < 10)
            {
                hours = 0;
                minutes = 0;
                seconds = 10;
                total = 10;
            }
            if (total > 7200)
            {
                hours = 2;
                minutes = 0;
                seconds = 0;
                total = 7200;
            }
            updatingDuration = true;
            numHours.Value = hours;
            numMinutes.Value = minutes;
            numSeconds.Value = seconds;
            numDuration.Value = total;
            updatingDuration = false;
        }

        private void SetDurationFromSeconds(int seconds)
        {
            if (seconds < 10) seconds = 10;
            if (seconds > 7200) seconds = 7200;
            int hours = seconds / 3600;
            int minutes = (seconds % 3600) / 60;
            int remainder = seconds % 60;
            int total = hours * 3600 + minutes * 60 + remainder;
            updatingDuration = true;
            numHours.Value = hours;
            numMinutes.Value = minutes;
            numSeconds.Value = remainder;
            numDuration.Value = total;
            updatingDuration = false;
        }

        private void ApplyDurationLimit()
        {
            numDuration.Maximum = 7200;
            if (numDuration.Value > 7200) numDuration.Value = 7200;
        }

        private bool ConfirmLongDuration(double seconds, bool album)
        {
            allowLongThinkingThisJob = false;
            if (!chkThinking.Checked || seconds <= maxDurationWithLm) return true;
            if (uiTestTimer != null) return true;
            string message =
                (album ? "專輯中最長曲目" : "目前時長") + "為 " + (int)seconds + " 秒，" +
                "超過 LM 思考的建議上限 " + maxDurationWithLm + " 秒。\n" +
                "可能因 VRAM 不足而失敗。\n\n" +
                "是：關閉 LM 思考後繼續\n否：照原設定繼續\n取消：不生成";
            DialogResult result = MessageBox.Show(this, message, "時長超過建議上限",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes)
            {
                chkThinking.Checked = false;
                AppendLog("=== 已關閉 LM 思考以降低 VRAM 風險 ===");
            }
            else
            {
                allowLongThinkingThisJob = true;
                AppendLog("=== 使用者選擇保留 LM 思考與超長時長 ===");
            }
            return true;
        }

        private void OnExpandPrompt(object sender, EventArgs e)
        {
            using (PromptEditorDialog dialog = new PromptEditorDialog(txtCaption.Text))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtCaption.Text = dialog.PromptText;
            }
        }

        private void OnApplyStyleTemplate(object sender, EventArgs e)
        {
            StyleTemplate template = comboStyleTemplate.SelectedItem as StyleTemplate;
            if (template == null) return;
            txtCaption.Text = template.Caption;
            chkAutoBpm.Checked = false;
            numBpm.Value = Clamp(template.Bpm, numBpm.Minimum, numBpm.Maximum);
            SelectCombo(comboKey, template.Key);
            SelectCombo(comboSignature, template.Signature);
            chkInstrumental.Checked = template.Instrumental;
            txtLyrics.Text = template.Instrumental ? "[Instrumental]" : txtLyrics.Text;
            AppendLog("=== 已套用風格模板: " + template.Name + " ===");
            SetStatus("已套用模板: " + template.Name);
        }


        private void OnPlay(object sender, EventArgs e)
        {
            PlaySelected();
        }

        private void PlaySelected()
        {
            string path = lastOutputFile;
            if (path.Length == 0 && lstHistory.SelectedItem is HistoryItem)
                path = ((HistoryItem)lstHistory.SelectedItem).Path;
            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show(this, "尚未選擇可播放的音檔。", "播放",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                if (loadedAudioPath != path) LoadAudio(path);
                if (audioOutput != null) audioOutput.Play();
                UpdatePlaybackUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "無法播放：" + ex.Message, "播放失敗",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnPlayPauseClick(object sender, EventArgs e)
        {
            if (audioOutput == null)
            {
                PlaySelected();
                return;
            }
            if (audioOutput.PlaybackState == PlaybackState.Playing)
                audioOutput.Pause();
            else
                audioOutput.Play();
            UpdatePlaybackUi();
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (audioOutput != null) audioOutput.Stop();
            if (audioStream != null) audioStream.CurrentTime = TimeSpan.Zero;
            seekBar.Value = 0;
            waveform.Position = 0;
            waveform.Invalidate();
            lblTime.Text = "00:00 / " + FormatTime(audioStream == null ? TimeSpan.Zero : audioStream.TotalTime);
            UpdatePlaybackUi();
        }

        private void OnSeekScroll(object sender, EventArgs e)
        {
            if (audioStream == null) return;
            TimeSpan position = TimeSpan.FromSeconds(audioStream.TotalTime.TotalSeconds * seekBar.Value / 1000.0);
            lblTime.Text = FormatTime(position) + " / " + FormatTime(audioStream.TotalTime);
            if (!scrubbing) SeekTo(seekBar.Value / 1000.0);
        }

        private void OnWaveformSeek(double ratio)
        {
            SeekTo(ratio);
        }

        private void SeekTo(double ratio)
        {
            if (audioStream == null) return;
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            audioStream.CurrentTime = TimeSpan.FromSeconds(audioStream.TotalTime.TotalSeconds * ratio);
            seekBar.Value = (int)Math.Round(ratio * 1000);
            waveform.Position = ratio;
            waveform.Invalidate();
            lblTime.Text = FormatTime(audioStream.CurrentTime) + " / " + FormatTime(audioStream.TotalTime);
        }

        private void LoadAudio(string path)
        {
            DisposeAudio();
            audioStream = AudioTools.OpenReader(path);
            audioOutput = new WaveOutEvent();
            audioOutput.Init(audioStream);
            audioOutput.PlaybackStopped += OnPlaybackStopped;
            loadedAudioPath = path;
            SetNowPlaying(path);
            seekBar.Value = 0;
            lblTime.Text = "00:00 / " + FormatTime(audioStream.TotalTime);
            waveform.Peaks = null;
            waveform.Duration = audioStream.TotalTime.TotalSeconds;
            waveform.Position = 0;
            waveform.Invalidate();
            LoadWaveform(path);
            playbackTimer.Start();
            UpdatePlaybackUi();
        }

        private void DisposeAudio()
        {
            playbackTimer.Stop();
            WaveOutEvent output = audioOutput;
            audioOutput = null;
            if (output != null)
            {
                try { output.PlaybackStopped -= OnPlaybackStopped; output.Stop(); }
                catch (Exception) { }
                output.Dispose();
            }
            if (audioStream != null)
            {
                audioStream.Dispose();
                audioStream = null;
            }
            loadedAudioPath = "";
        }

        private void OnPlaybackTimer(object sender, EventArgs e)
        {
            if (audioStream == null) return;
            if (!scrubbing)
            {
                TimeSpan total = audioStream.TotalTime;
                TimeSpan position = audioStream.CurrentTime;
                double ratio = total.TotalSeconds > 0 ? position.TotalSeconds / total.TotalSeconds : 0;
                seekBar.Value = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, ratio)) * 1000);
                waveform.Position = ratio;
                waveform.Invalidate();
                lblTime.Text = FormatTime(position) + " / " + FormatTime(total);
            }
            UpdatePlaybackUi();
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate()
                {
                    UpdatePlaybackUi();
                    if (audioStream != null &&
                        audioStream.CurrentTime >= audioStream.TotalTime - TimeSpan.FromMilliseconds(200))
                    {
                        seekBar.Value = 0;
                        waveform.Position = 0;
                        waveform.Invalidate();
                        lblTime.Text = "00:00 / " + FormatTime(audioStream.TotalTime);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void LoadWaveform(string path)
        {
            string target = path;
            Thread worker = new Thread(delegate()
            {
                try
                {
                    double seconds;
                    float[] peaks = AudioTools.BuildPeaks(target, 1024, out seconds);
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            if (loadedAudioPath != target) return;
                            waveform.Peaks = peaks;
                            waveform.Duration = seconds;
                            waveform.Invalidate();
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                catch (Exception)
                {
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static string FormatTime(TimeSpan value)
        {
            int minutes = (int)value.TotalMinutes;
            return minutes.ToString("00") + ":" + value.Seconds.ToString("00");
        }

        private void HandleCoverThenTag(string path)
        {
            if (sdEnabled && sdModel.Length > 0)
            {
                Process oldDaemon = daemon;
                if (oldDaemon != null && !jobRunning)
                {
                    AppendLog("=== 釋放 ACE-Step 模型以產生 SD 封面 ===");
                    KillDaemon();
                    try { oldDaemon.WaitForExit(5000); } catch (Exception) { }
                }
                StartSdCover(path);
                return;
            }
            TryTagOutput(path);
            QueueAutoVideo(path);
        }

        private void QueueAutoVideo(string path)
        {
            if (!chkAutoVideo.Checked || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!File.Exists(Paths.VideoTool))
            {
                AppendLog("=== 找不到影片轉換腳本，已跳過: " + Paths.VideoTool + " ===");
                return;
            }
            string image = ResolveVideoCover(path);
            lock (videoGate)
            {
                videoQueue.Enqueue(new KeyValuePair<string, string>(path, image));
                if (videoRunning) return;
                videoRunning = true;
            }
            Thread worker = new Thread(ProcessVideoQueue);
            worker.IsBackground = true;
            worker.Start();
        }

        private void ProcessVideoQueue()
        {
            while (true)
            {
                KeyValuePair<string, string> job;
                lock (videoGate)
                {
                    if (videoQueue.Count == 0)
                    {
                        videoRunning = false;
                        return;
                    }
                    job = videoQueue.Dequeue();
                }
                string path = job.Key;
                try
                {
                    string output = Path.ChangeExtension(path, ".mp4");
                    if (File.Exists(output) && new FileInfo(output).Length > 1024)
                    {
                        AppendLog("=== 影片已存在，略過: " + output + " ===");
                        continue;
                    }
                    if (File.Exists(output))
                    {
                        try { File.Delete(output); }
                        catch (Exception ex)
                        {
                            AppendLog("=== 無法清除未完成影片: " + ex.Message + " ===");
                            continue;
                        }
                    }
                    AppendLog("=== 轉換影片: " + Path.GetFileName(path) + " ===");
                    PostUi(delegate()
                    {
                        if (jobRunning || baseStatus.StartsWith("批次完成")) return;
                        if (baseStatus.StartsWith("完成:"))
                            SetStatus(baseStatus + "（影片轉換中）");
                        else
                            SetStatus("轉換影片中...");
                    });
                    int code = VideoRunner.Run(path, job.Value, delegate(string line)
                    {
                        AppendLog(line);
                    });
                    if (code == 0 && File.Exists(output))
                    {
                        AppendLog("=== 影片完成: " + output + " ===");
                        PostUi(delegate()
                        {
                            if (jobRunning || baseStatus.StartsWith("批次完成")) return;
                            const string marker = "（影片轉換中）";
                            if (baseStatus.StartsWith("完成:"))
                            {
                                string text = baseStatus.EndsWith(marker)
                                    ? baseStatus.Substring(0, baseStatus.Length - marker.Length)
                                    : baseStatus;
                                SetStatus(text + "（影片完成）");
                            }
                            else
                            {
                                SetStatus("影片完成: " + Path.GetFileName(output));
                            }
                        });
                    }
                    else
                    {
                        AppendLog("=== 影片轉換失敗 (exit " + code + "): " + path + " ===");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("=== 影片轉換錯誤: " + ex.Message + " ===");
                }
            }
        }

        private string ResolveVideoCover(string audioPath)
        {
            string sidecar = Path.ChangeExtension(audioPath, ".cover.png");
            if (File.Exists(sidecar)) return "";
            if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath)) return coverPath;
            return "";
        }

        private void PostUi(Action action)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void StartSdCover(string path)
        {
            AppendLog("=== 開始產生 SD 封面 ===");
            SetStatus("正在產生 SD 封面...");
            string target = path;
            Thread worker = new Thread(delegate()
            {
                string cover = Path.ChangeExtension(target, ".cover.png");
                GpuLease lease = null;
                try
                {
                    string leaseError;
                    lease = GpuLease.Acquire("sd", 30, out leaseError);
                    if (lease == null)
                        throw new InvalidOperationException("GPU 使用中: " + leaseError);
                    if (!SdCoverClient.EnsureApi(sdUrl, sdPath, delegate(string message)
                    {
                        AppendLog(message);
                    }))
                        throw new InvalidOperationException("SD API 無法啟動");
                    if (sdModel.Length > 0)
                    {
                        AppendLog("SD 切換模型: " + sdModel);
                        SdCoverClient.SelectModel(sdUrl, sdModel);
                    }
                    string prompt = BuildCoverPrompt(target);
                    AppendLog("SD 產生封面...");
                    SdCoverClient.GenerateCover(sdUrl, prompt,
                        "text, watermark, logo, letters, blurry, low quality, deformed",
                        sdSize, sdSize, sdSteps, cover);
                    SdCoverClient.UnloadCheckpoint(sdUrl);
                    if (!File.Exists(cover))
                        throw new InvalidOperationException("SD 未回傳圖片");
                    BeginInvoke(new Action(delegate()
                    {
                        TryTagOutput(target, cover);
                        QueueAutoVideo(target);
                        AppendLog("=== SD 封面完成: " + cover + " ===");
                        SetStatus("完成: " + Path.GetFileName(target) + "（SD 封面）");
                        RefreshHistory();
                    }));
                }
                catch (Exception ex)
                {
                    AppendLog("SD 封面失敗，使用預設封面: " + ex.Message);
                    try { SdCoverClient.Stop(); } catch (Exception) { }
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            SetStatus("SD 封面失敗: " + ex.Message);
                        }));
                    }
                    catch (InvalidOperationException) { }
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            TryTagOutput(target);
                            QueueAutoVideo(target);
                        }));
                    }
                    catch (InvalidOperationException) { }
                }
                finally
                {
                    if (lease != null) lease.Dispose();
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private string BuildCoverPrompt(string audioPath)
        {
            string caption = "";
            try
            {
                string sidecar = Path.ChangeExtension(audioPath, ".json");
                if (File.Exists(sidecar))
                {
                    Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(sidecar, Encoding.UTF8));
                    object configValue;
                    Dictionary<string, object> config = null;
                    if (data.TryGetValue("config", out configValue))
                        config = configValue as Dictionary<string, object>;
                    if (config != null) caption = GetString(config, "caption", "");
                }
            }
            catch (Exception) { }
            if (caption.Length > 500) caption = caption.Substring(0, 500);
            return (caption + ", album cover art, square composition, cinematic lighting, "
                    + "warm elegant atmosphere, no text, no watermark, detailed, high quality").Trim();
        }

        private void TryTagOutput(string path, string coverOverride = null)
        {
            bool wasLoaded = audioStream != null &&
                string.Equals(loadedAudioPath, path, StringComparison.OrdinalIgnoreCase);
            bool wasPlaying = wasLoaded && audioOutput != null &&
                audioOutput.PlaybackState == PlaybackState.Playing;
            TimeSpan resume = wasLoaded ? audioStream.CurrentTime : TimeSpan.Zero;
            if (wasLoaded) DisposeAudio();
            try
            {
                string sidecar = Path.ChangeExtension(path, ".json");
                if (!File.Exists(sidecar)) return;
                Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(sidecar, Encoding.UTF8));
                Dictionary<string, object> config = null;
                object configValue;
                if (data.TryGetValue("config", out configValue)) config = configValue as Dictionary<string, object>;
                long seed = 0;
                object seedValue;
                if (data.TryGetValue("actual_seed", out seedValue) && seedValue != null)
                    seed = Convert.ToInt64(seedValue);
                string prompt = "", bpm = "", key = "", format = "";
                if (config != null)
                {
                    prompt = GetString(config, "caption", "");
                    bpm = GetString(config, "bpm", "");
                    key = GetString(config, "keyscale", "");
                    format = GetString(config, "audio_format", "");
                }
                AudioTools.WriteTags(path, prompt, seed, bpm, key, format,
                    string.IsNullOrEmpty(coverOverride) ? coverPath : coverOverride);
                AppendLog("=== 已寫入音檔標籤（prompt/seed/bpm）===");
            }
            catch (Exception ex)
            {
                AppendLog("標籤寫入失敗: " + ex.Message);
            }
            finally
            {
                if (wasLoaded)
                {
                    try
                    {
                        LoadAudio(path);
                        if (audioStream != null)
                        {
                            audioStream.CurrentTime = resume;
                            if (wasPlaying && audioOutput != null) audioOutput.Play();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog("重新載入播放失敗: " + ex.Message);
                    }
                }
            }
        }

        private void OnHistorySelected(object sender, EventArgs e)
        {
            if (lstHistory.SelectedItem is HistoryItem)
            {
                lastOutputFile = ((HistoryItem)lstHistory.SelectedItem).Path;
                UpdateFileButtons();
            }
        }

        private void UpdateFileButtons()
        {
            bool hasFile = lastOutputFile.Length > 0 && File.Exists(lastOutputFile);
            btnPlay.Enabled = hasFile;
            btnOpen.Enabled = !jobRunning && (hasFile || Directory.Exists(txtOutDir.Text.Trim()));
        }

        private void RefreshHistory()
        {
            string dir = txtOutDir.Text.Trim();
            string selected = lastOutputFile;
            lstHistory.BeginUpdate();
            try
            {
                lstHistory.Items.Clear();
                if (Directory.Exists(dir))
                {
                    List<FileInfo> files = new List<FileInfo>();
                    foreach (FileInfo file in new DirectoryInfo(dir).GetFiles())
                    {
                        string ext = file.Extension.ToLowerInvariant();
                        if (ext == ".wav" || ext == ".flac" || ext == ".mp3" ||
                            ext == ".opus" || ext == ".aac")
                            files.Add(file);
                    }
                    files.Sort(delegate(FileInfo a, FileInfo b)
                    {
                        return b.LastWriteTime.CompareTo(a.LastWriteTime);
                    });
                    int count = 0;
                    foreach (FileInfo file in files)
                    {
                        lstHistory.Items.Add(new HistoryItem(file.FullName, BuildHistoryLabel(file)));
                        if (++count >= 200) break;
                    }
                }
            }
            finally
            {
                lstHistory.EndUpdate();
            }
            if (selected.Length > 0)
            {
                for (int i = 0; i < lstHistory.Items.Count; i++)
                {
                    HistoryItem item = lstHistory.Items[i] as HistoryItem;
                    if (item != null && string.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        lstHistory.SelectedIndex = i;
                        break;
                    }
                }
            }
            UpdateFileButtons();
        }

        private string BuildHistoryLabel(FileInfo file)
        {
            string label = file.Name;
            string sidecar = Path.ChangeExtension(file.FullName, ".json");
            if (!File.Exists(sidecar)) return label;
            try
            {
                Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(sidecar, Encoding.UTF8));
                object configValue;
                Dictionary<string, object> config = null;
                if (data.TryGetValue("config", out configValue))
                    config = configValue as Dictionary<string, object>;
                long seed = 0;
                object seedValue;
                if (data.TryGetValue("actual_seed", out seedValue) && seedValue != null)
                    seed = Convert.ToInt64(seedValue);
                string prompt = config != null ? GetString(config, "caption", "") : "";
                if (prompt.Length > 42) prompt = prompt.Substring(0, 42) + "…";
                string labelText = file.Name + "  |  seed " + seed + "  |  " + prompt;
                return labelText.Length > 70 ? labelText.Substring(0, 70) + "…" : labelText;
            }
            catch (Exception)
            {
                return label;
            }
        }

        private sealed class HistoryItem
        {
            public readonly string Path;
            private readonly string display;

            public HistoryItem(string path, string display)
            {
                Path = path;
                this.display = display;
            }

            public override string ToString()
            {
                return display;
            }
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

        private void OnAlbum(object sender, EventArgs e)
        {
            if (jobRunning || (albumProcess != null && !albumProcess.HasExited)) return;
            using (AlbumDialog dialog = new AlbumDialog(txtCaption.Text, (int)numDuration.Value))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                List<Dictionary<string, object>> tracks = new List<Dictionary<string, object>>();
                List<string> tooLong = new List<string>();
                int longestTrack = 0;
                int lineNumber = 0;
                foreach (string raw in dialog.TrackText.Replace("\r\n", "\n").Split('\n'))
                {
                    lineNumber++;
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    string[] parts = line.Split('|');
                    string title = parts[0].Trim();
                    string prompt = parts.Length > 1 && parts[1].Trim().Length > 0
                        ? parts[1].Trim() : title;
                    int duration = dialog.TrackDuration;
                    if (parts.Length > 2) int.TryParse(parts[2].Trim(), out duration);
                    if (duration < 10) duration = dialog.TrackDuration;
                    if (duration > 7200)
                    {
                        tooLong.Add("第 " + lineNumber + " 行: " + line +
                            "（" + duration + " 秒）");
                        continue;
                    }
                    if (duration > longestTrack) longestTrack = duration;
                    Dictionary<string, object> track = new Dictionary<string, object>();
                    track["title"] = title;
                    track["prompt"] = prompt;
                    track["duration"] = duration;
                    tracks.Add(track);
                }
                if (tooLong.Count > 0)
                {
                    MessageBox.Show(this,
                        "以下曲目超過 7200 秒上限，請修正後再試：\n\n" +
                        string.Join("\n", tooLong.ToArray()),
                        "曲目長度錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (tracks.Count == 0)
                {
                    MessageBox.Show(this, "沒有有效曲目。", "專輯");
                    return;
                }
                if (!ConfirmLongDuration(longestTrack, true)) return;
                string safeTitle = Regex.Replace(dialog.AlbumTitle, "[^\\w\\-\\u4e00-\\u9fff]+", "_");
                string folder = Path.Combine(txtOutDir.Text.Trim(),
                    "album_" + safeTitle + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(folder);
                currentAlbumFolder = folder;

                Dictionary<string, object> shared = BuildConfig(
                    Convert.ToString(tracks[0]["prompt"]), -1);
                shared["output_dir"] = folder;
                Dictionary<string, object> album = new Dictionary<string, object>();
                album["album_title"] = dialog.AlbumTitle;
                album["artist"] = dialog.Artist;
                album["output_dir"] = folder;
                album["shared"] = shared;
                album["tracks"] = tracks;
                string configPath = Path.Combine(Paths.TempDir,
                    "album_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                Directory.CreateDirectory(Paths.TempDir);
                File.WriteAllText(configPath, serializer.Serialize(album), new UTF8Encoding(false));

                albumCancelled = false;
                Process oldDaemon = daemon;
                if (oldDaemon != null)
                {
                    albumStarting = true;
                    KillDaemon();
                    try { oldDaemon.WaitForExit(1500); } catch (Exception) { }
                }

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Paths.PythonExe);
                    psi.Arguments = "-u \"" + Paths.Backend + "\" --album \"" + configPath +
                        "\" --gpu-wait 20";
                    psi.WorkingDirectory = Paths.Workspace;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    albumProcess = new Process();
                    albumProcess.StartInfo = psi;
                    albumProcess.EnableRaisingEvents = true;
                    albumProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs args)
                    {
                        if (args.Data != null) PostLine(args.Data, false);
                    };
                    albumProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs args)
                    {
                        if (args.Data != null) PostLine(args.Data, true);
                    };
                    albumProcess.Exited += OnAlbumExited;
                    albumProcess.Start();
                    albumProcess.BeginOutputReadLine();
                    albumProcess.BeginErrorReadLine();
                    txtLog.Clear();
                    AppendLog("=== 專輯製作開始：" + dialog.AlbumTitle + " / " + tracks.Count + " 首 ===");
                    SetBusy(true);
                    bar.Style = ProgressBarStyle.Marquee;
                    SetStatus("專輯製作中...");
                }
                catch (Exception ex)
                {
                    albumProcess = null;
                    albumStarting = false;
                    SetBusy(false);
                    MessageBox.Show(this, "無法啟動專輯生成：" + ex.Message, "專輯");
                }
            }
        }

        private void OnAlbumExited(object sender, EventArgs e)
        {
            if (closing || IsDisposed) return;
            try { BeginInvoke(new Action(HandleAlbumExit)); }
            catch (InvalidOperationException) { }
        }

        private void HandleAlbumExit()
        {
            Process finished = albumProcess;
            albumProcess = null;
            albumStarting = false;
            if (albumCancelled)
            {
                albumCancelled = false;
                currentAlbumFolder = "";
                if (finished != null)
                {
                    try { finished.WaitForExit(); finished.Dispose(); } catch (Exception) { }
                }
                SetBusy(false);
                bar.Style = ProgressBarStyle.Blocks;
                bar.Value = 0;
                SetStatus("已取消專輯");
                AppendLog("=== 專輯已取消 ===");
                RefreshHistory();
                return;
            }
            int code = -1;
            string folder = currentAlbumFolder.Length > 0
                ? currentAlbumFolder : txtOutDir.Text.Trim();
            currentAlbumFolder = "";
            if (finished != null)
            {
                try { finished.WaitForExit(); finished.WaitForExit(); code = finished.ExitCode; }
                catch (Exception) { }
                finished.Dispose();
            }
            SetBusy(false);
            bar.Style = ProgressBarStyle.Blocks;
            bar.Value = 0;
            RefreshHistory();
            if (code == 75)
            {
                SetStatus("GPU 使用中");
                AppendLog("=== 專輯未啟動：GPU 使用中 ===");
                MessageBox.Show(this,
                    "GPU 正被其他生成程序使用（可能是 Telegram 遠端生成）。\n" +
                    "請關閉其他生成程序後再試。",
                    "GPU 使用中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<string> files = new List<string>();
            string firstPrompt = txtCaption.Text;
            try
            {
                string albumJson = Path.Combine(folder, "album.json");
                if (File.Exists(albumJson))
                {
                    Dictionary<string, object> meta = serializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(albumJson, Encoding.UTF8));
                    object tracksValue;
                    if (meta.TryGetValue("tracks", out tracksValue))
                    {
                        object[] list = tracksValue as object[];
                        if (list != null)
                        {
                            foreach (object item in list)
                            {
                                Dictionary<string, object> track = item as Dictionary<string, object>;
                                if (track == null) continue;
                                object fileValue;
                                if (track.TryGetValue("file", out fileValue) && fileValue != null)
                                    files.Add(Convert.ToString(fileValue));
                                object promptValue;
                                if (firstPrompt.Length == 0 && track.TryGetValue("prompt", out promptValue))
                                    firstPrompt = Convert.ToString(promptValue);
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            if (sdEnabled && sdModel.Length > 0 && files.Count > 0)
            {
                Process oldDaemon = daemon;
                if (oldDaemon != null)
                {
                    KillDaemon();
                    try { oldDaemon.WaitForExit(5000); } catch (Exception) { }
                }
                StartAlbumCover(files, firstPrompt);
                SetStatus("專輯完成，正在產生 SD 封面...");
            }
            else
            {
                foreach (string file in files)
                {
                    TryTagOutput(file);
                    QueueAutoVideo(file);
                }
                SetStatus("專輯完成 (" + files.Count + " 首) exit=" + code);
            }
            AppendLog("=== 專輯完成：共 " + files.Count + " 首 ===");
        }

        private void StartAlbumCover(List<string> files, string prompt)
        {
            List<string> targets = new List<string>(files);
            Thread worker = new Thread(delegate()
            {
                string folder = Path.GetDirectoryName(targets[0]);
                string cover = Path.Combine(folder, "album_cover.png");
                GpuLease lease = null;
                try
                {
                    string leaseError;
                    lease = GpuLease.Acquire("sd", 30, out leaseError);
                    if (lease == null)
                        throw new InvalidOperationException("GPU 使用中: " + leaseError);
                    if (!SdCoverClient.EnsureApi(sdUrl, sdPath, delegate(string message)
                    {
                        AppendLog(message);
                    }))
                        throw new InvalidOperationException("SD API 無法啟動");
                    if (sdModel.Length > 0)
                    {
                        AppendLog("SD 切換模型: " + sdModel);
                        SdCoverClient.SelectModel(sdUrl, sdModel);
                    }
                    string coverPrompt = (prompt ?? "") +
                        ", album cover art, square composition, cinematic lighting, warm atmosphere, "
                        + "no text, no watermark, high quality";
                    SdCoverClient.GenerateCover(sdUrl, coverPrompt,
                        "text, watermark, logo, letters, blurry, low quality, deformed",
                        sdSize, sdSize, sdSteps, cover);
                    SdCoverClient.UnloadCheckpoint(sdUrl);
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            foreach (string file in targets)
                            {
                                TryTagOutput(file, cover);
                                QueueAutoVideo(file);
                            }
                            SetStatus("專輯完成，封面已寫入 " + targets.Count + " 首");
                            RefreshHistory();
                        }));
                    }
                    catch (InvalidOperationException) { }
                }
                catch (Exception ex)
                {
                    AppendLog("專輯 SD 封面失敗: " + ex.Message);
                    try { SdCoverClient.Stop(); } catch (Exception) { }
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            foreach (string file in targets)
                            {
                                TryTagOutput(file);
                                QueueAutoVideo(file);
                            }
                        }));
                    }
                    catch (InvalidOperationException) { }
                }
                finally
                {
                    if (lease != null) lease.Dispose();
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void OnMediaTools(object sender, EventArgs e)
        {
            string dir = txtOutDir.Text.Trim();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (audioOutput != null && audioOutput.PlaybackState == PlaybackState.Playing)
                audioOutput.Pause();
            using (MediaToolsDialog dialog = new MediaToolsDialog(dir, delegate
            {
                RefreshHistory();
            }))
            {
                dialog.ShowDialog(this);
            }
        }

        private void OnTelegram(object sender, EventArgs e)
        {
            if (telegramProcess != null && !telegramProcess.HasExited)
            {
                MessageBox.Show(this, "Telegram bot 已在執行。", "Telegram",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (daemon != null && !jobRunning &&
                (albumProcess == null || albumProcess.HasExited))
            {
                AppendLog("=== 釋放常駐模型，切換為 Telegram 遠端模式 ===");
                Process oldDaemon = daemon;
                KillDaemon();
                try { oldDaemon.WaitForExit(5000); } catch (Exception) { }
            }
            string script = Path.Combine(Paths.Workspace, "AI_Music", "scripts", "telegram_bot.py");
            if (!File.Exists(script))
            {
                MessageBox.Show(this, "找不到 telegram_bot.py。", "Telegram",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Paths.PythonExe);
                psi.Arguments = "-u \"" + script + "\"";
                psi.WorkingDirectory = Paths.Workspace;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                telegramProcess = new Process();
                telegramProcess.StartInfo = psi;
                telegramProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs args)
                {
                    if (args.Data != null) AppendLog("[tg] " + args.Data);
                };
                telegramProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs args)
                {
                    if (args.Data != null) AppendLog("[tg] " + args.Data);
                };
                telegramProcess.Start();
                telegramProcess.BeginOutputReadLine();
                telegramProcess.BeginErrorReadLine();
                AppendLog("=== Telegram bot 已啟動 ===");
                SetStatus("Telegram bot 執行中");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "無法啟動 Telegram bot: " + ex.Message, "Telegram",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSd(object sender, EventArgs e)
        {
            using (SdDialog dialog = new SdDialog(sdEnabled, sdUrl, sdPath, sdModel, sdSteps, sdSize))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                sdEnabled = dialog.EnabledForCover;
                sdUrl = dialog.ApiUrl;
                sdPath = dialog.InstallPath;
                sdModel = dialog.ModelTitle;
                sdSteps = dialog.Steps;
                sdSize = dialog.CoverSize;
                SaveSettings();
                AppendLog("=== SD 封面設定已更新：enabled=" + sdEnabled +
                          " model=" + sdModel + " ===");
            }
        }

        private void OnAdvanced(object sender, EventArgs e)
        {
            using (AdvancedDialog dialog = new AdvancedDialog(
                negativePrompt, fadeInValue, fadeOutValue, shiftValue,
                mp3BitrateValue, mp3SampleRateValue))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                negativePrompt = dialog.NegativePrompt;
                fadeInValue = dialog.FadeIn;
                fadeOutValue = dialog.FadeOut;
                shiftValue = dialog.Shift;
                mp3BitrateValue = dialog.Mp3Bitrate;
                mp3SampleRateValue = dialog.Mp3SampleRate;
                SaveSettings();
                AppendLog("=== 已更新進階音樂設定 ===");
            }
        }

        private void OnLinkPrompt(object sender, EventArgs e)
        {
            using (LinkPromptDialog dialog = new LinkPromptDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                txtCaption.Text = dialog.SuggestedPrompt;
                AppendLog("=== 已套用參考連結提示詞（metadata 分析）===");
            }
        }

        private void OnCover(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "選擇封面圖片";
                dialog.Filter = "圖片|*.png;*.jpg;*.jpeg|所有檔案|*.*";
                if (File.Exists(coverPath)) dialog.FileName = coverPath;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                coverPath = dialog.FileName;
                SaveSettings();
                AppendLog("封面圖片: " + coverPath);
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

        private Dictionary<string, object> BuildConfig(string prompt, int seed)
        {
            Dictionary<string, object> config = new Dictionary<string, object>();
            config["caption"] = prompt;
            config["lyrics"] = txtLyrics.Text.Trim().Length == 0 ? "[Instrumental]" : txtLyrics.Text.Trim();
            config["instrumental"] = chkInstrumental.Checked;
            config["thinking"] = chkThinking.Checked;
            config["allow_long_thinking"] = allowLongThinkingThisJob;
            config["bpm"] = chkAutoBpm.Checked ? null : (object)(int)numBpm.Value;
            config["duration"] = (double)numDuration.Value;
            config["inference_steps"] = (int)numSteps.Value;
            config["guidance_scale"] = (double)numGuidance.Value;
            config["keyscale"] = comboKey.SelectedIndex <= 0 ? "" : Convert.ToString(comboKey.SelectedItem);
            config["timesignature"] = comboSignature.SelectedIndex <= 0 ? "" : Convert.ToString(comboSignature.SelectedItem);
            config["vocal_language"] = Convert.ToString(comboLanguage.SelectedItem);
            config["audio_format"] = Convert.ToString(comboFormat.SelectedItem);
            config["offload_mode"] = Convert.ToString(comboOffload.SelectedItem);
            config["lm_negative_prompt"] = negativePrompt;
            config["fade_in_duration"] = (double)fadeInValue;
            config["fade_out_duration"] = (double)fadeOutValue;
            config["shift"] = (double)shiftValue;
            config["mp3_bitrate"] = mp3BitrateValue;
            config["mp3_sample_rate"] = mp3SampleRateValue;
            config["seed"] = seed;
            config["output_dir"] = txtOutDir.Text.Trim();
            config["filename_prefix"] = "ace";
            return config;
        }

        private void OnGenerate(object sender, EventArgs e)
        {
            if (jobRunning || (albumProcess != null && !albumProcess.HasExited)) return;

            string caption = txtCaption.Text.Trim();
            if (caption.Length == 0)
            {
                MessageBox.Show(this, "請輸入提示詞。", "缺少提示詞", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            if (!ConfirmLongDuration((double)numDuration.Value, false)) return;

            SaveSettings();
            finishTimer.Stop();
            lastOutputFile = "";
            txtLog.Clear();
            lastLogWasProgress = false;
            exitOk = false;
            exitCode = -1;
            cancelRequested = false;
            restartRequested = false;

            batchQueue.Clear();
            batchOutputs.Clear();
            batchPrompt = "";
            batchActive = false;
            if (chkBatch.Checked)
            {
                string[] rawLines = txtCaption.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                List<string> prompts = new List<string>();
                foreach (string rawLine in rawLines)
                {
                    string promptLine = rawLine.Trim();
                    if (promptLine.Length > 0) prompts.Add(promptLine);
                }
                if (prompts.Count == 0)
                {
                    MessageBox.Show(this, "批次模式需要每行一個提示詞。", "缺少提示詞",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                batchActive = true;
                batchPrompt = prompts[0];
                batchTotal = prompts.Count;
                batchCompleted = 0;
                batchErrors = 0;
                for (int i = 0; i < prompts.Count; i++)
                {
                    int jobSeed = seed < 0 ? -1 : seed + i;
                    batchQueue.Enqueue(BuildConfig(prompts[i], jobSeed));
                }
            }

            Dictionary<string, object> config = batchActive
                ? batchQueue.Dequeue()
                : BuildConfig(caption, seed);

            jobRunning = true;
            lastJobActivity = DateTime.Now;
            jobStarted = DateTime.Now;
            elapsedTimer.Start();
            SetBusy(true);
            bar.Style = ProgressBarStyle.Marquee;
            AppendLog("=== 開始生成 ===");
            AppendLog("描述: " + caption);
            int safeDuration = chkThinking.Checked ? maxDurationWithLm : 600;
            if (numDuration.Value > safeDuration)
                AppendLog("=== 注意：目前單次生成建議上限約 " + safeDuration +
                          " 秒，超過可能因 VRAM 不足失敗；長曲分段尚未實作 ===");

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
                lastJobActivity = DateTime.Now;
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
            Process album = albumProcess;
            if (album != null && !album.HasExited)
            {
                albumCancelled = true;
                try
                {
                    ProcessSupervisor.StopTree(album, 5000);
                    AppendLog("=== 已送出取消專輯指令 ===");
                    SetStatus("正在取消專輯...");
                }
                catch (Exception ex)
                {
                    AppendLog("取消專輯失敗: " + ex.Message);
                }
                return;
            }
            if (daemon == null) return;
            cancelRequested = true;
            restartRequested = false;
            batchActive = false;
            batchQueue.Clear();
            batchOutputs.Clear();
            if (chkKeepModel.Checked && daemonReady)
            {
                softCancel = true;
                SetStatus("已取消（背景收尾中，模型保留）");
                AppendLog("=== 已取消；目前工作會在背景完成，模型保持載入 ===");
                return;
            }
            softCancel = false;
            SetStatus("正在取消...");
            AppendLog("=== 已送出取消指令（模型將卸載，下次需重新載入）===");
            KillDaemon();
        }

        private void OnDaemonIdle(object sender, EventArgs e)
        {
            if (daemon == null || jobRunning) return;
            if (albumProcess != null && !albumProcess.HasExited) return;
            if ((DateTime.Now - lastJobActivity).TotalMinutes < 5) return;
            AppendLog("=== 閒置 5 分鐘，卸載模型以釋放 GPU ===");
            try
            {
                daemon.StandardInput.WriteLine("{\"command\":\"quit\"}");
                daemon.StandardInput.Flush();
                SetStatus("閒置卸載模型...");
            }
            catch (Exception)
            {
                KillDaemon();
            }
        }

        private void KillDaemon()
        {
            Process running = daemon;
            if (running == null) return;
            if (!ProcessSupervisor.StopTree(running, 5000))
                AppendLog("取消程序失敗: PID " + running.Id);
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
            bool videoProgress = clean.StartsWith("[video]") &&
                clean.TrimEnd().EndsWith("%");
            if (videoProgress)
            {
                int space = clean.TrimEnd().LastIndexOf(' ');
                int percent;
                if (space >= 0 && int.TryParse(
                        clean.TrimEnd().Substring(space + 1).TrimEnd('%'), out percent))
                {
                    percent = Math.Max(0, Math.Min(100, percent));
                    bar.Style = ProgressBarStyle.Blocks;
                    bar.Minimum = 0;
                    bar.Maximum = 100;
                    bar.Value = percent;
                    SetStatus("轉換影片中... " + percent + "%");
                }
                return;
            }
            EnqueueLog(clean);

            if (clean.StartsWith("[stage]"))
            {
                SetStatus(clean.Substring(7).Trim());
            }
            else if (clean.StartsWith("[progress]"))
            {
                HandleProgress(clean.Substring(10).Trim());
            }
            else if (clean.StartsWith("[warn]"))
            {
                SetStatus("警告: " + clean.Substring(6).Trim());
                AppendLog("=== 警告: " + clean.Substring(6).Trim() + " ===");
            }
            else if (clean.StartsWith("[busy]"))
            {
                string owner = clean.Substring(6).Trim();
                SetStatus("GPU 使用中：" + owner);
                AppendLog("=== GPU 使用中：" + owner + " ===");
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
                lastJobActivity = DateTime.Now;

                if (batchActive && !cancelRequested)
                {
                    if (code == 0) batchCompleted++; else batchErrors++;
                    if (code == 0 && lastOutputFile.Length > 0)
                    {
                        if (sdEnabled && sdModel.Length > 0)
                            batchOutputs.Add(lastOutputFile);
                        else
                        {
                            TryTagOutput(lastOutputFile);
                            QueueAutoVideo(lastOutputFile);
                        }
                    }
                    exitCode = code;
                    exitOk = code == 0;
                    AppendLog("=== 批次進度 " + (batchCompleted + batchErrors) + "/" + batchTotal +
                              (code == 0 ? " 完成" : " 失敗 (exit " + code + ")") + " ===");
                    if (batchQueue.Count > 0)
                    {
                        Dictionary<string, object> next = batchQueue.Dequeue();
                        bar.Style = ProgressBarStyle.Marquee;
                        bar.Value = 0;
                        jobRunning = true;
                        SetBusy(true);
                        if (daemonReady)
                        {
                            SendJob(next);
                        }
                        else
                        {
                            pendingConfig = next;
                            StartDaemon(Convert.ToString(next["offload_mode"]));
                        }
                        SetStatus("批次 " + (batchCompleted + batchErrors + 1) + "/" + batchTotal + " 生成中...");
                        return;
                    }
                    batchActive = false;
                    jobRunning = false;
                    elapsedTimer.Stop();
                    bar.Style = ProgressBarStyle.Blocks;
                    bar.Value = 0;
                    SetBusy(false);
                    if (sdEnabled && sdModel.Length > 0 && batchOutputs.Count > 0)
                    {
                        AppendLog("=== 批次完成，開始產生 SD 封面 ===");
                        Process oldDaemon = daemon;
                        if (oldDaemon != null)
                        {
                            KillDaemon();
                            try { oldDaemon.WaitForExit(5000); } catch (Exception) { }
                        }
                        List<string> coverFiles = new List<string>(batchOutputs);
                        batchOutputs.Clear();
                        StartAlbumCover(coverFiles, batchPrompt);
                        SetStatus("批次完成，正在產生 SD 封面...");
                        RefreshHistory();
                        return;
                    }
                    SetStatus("批次完成 " + batchCompleted + "/" + batchTotal +
                              (batchErrors > 0 ? "，失敗 " + batchErrors : ""));
                    AppendLog("=== 批次完成：成功 " + batchCompleted + "，失敗 " + batchErrors + " ===");
                    RefreshHistory();
                    return;
                }

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
            if (albumStarting)
            {
                albumStarting = false;
                Process old = daemon;
                daemon = null;
                daemonReady = false;
                if (old != null) { try { old.WaitForExit(); old.WaitForExit(); old.Dispose(); } catch (Exception) { } }
                return;
            }
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

            if (code == 75)
            {
                SetStatus("GPU 使用中");
                AppendLog("=== GPU 使用中，模型未啟動 ===");
                MessageBox.Show(this,
                    "GPU 正被其他生成程序使用（可能是 Telegram 遠端生成）。\n" +
                    "請關閉其他生成程序後再試。",
                    "GPU 使用中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (cancelRequested)
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
                SetStatus(softCancel ? "已取消（模型保留）" : "已取消");
                cancelRequested = false;
                softCancel = false;
                RefreshHistory();
                return;
            }
            if (exitOk && lastOutputFile.Length > 0)
            {
                HandleCoverThenTag(lastOutputFile);
                RefreshHistory();
                SetStatus("完成: " + Path.GetFileName(lastOutputFile));
                AppendLog("=== 完成: " + lastOutputFile + " ===");
            }
            else if (exitOk)
            {
                RefreshHistory();
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
            numHours.Enabled = !busy;
            numMinutes.Enabled = !busy;
            numSeconds.Enabled = !busy;
            comboStyleTemplate.Enabled = !busy;
            btnApplyTemplate.Enabled = !busy;
            btnExpandPrompt.Enabled = !busy;
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
            chkKeepModel.Enabled = !busy;
            chkAutoVideo.Enabled = !busy;
            chkBatch.Enabled = !busy;
            btnAlbum.Enabled = !busy;
            btnMediaTools.Enabled = !busy;
            btnSd.Enabled = !busy;
            btnAdvanced.Enabled = !busy;
            btnLink.Enabled = !busy;
            UpdateFileButtons();
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
            ThemeManager.SetTheme(GetBool(s, "theme_dark", true));
            ApplyTheme();
            savedSplitRatio = (double)Clamp(GetDecimal(s, "layout_split_ratio", 0.5m), 0.2m, 0.8m);
            negativePrompt = GetString(s, "lm_negative_prompt", "");
            fadeInValue = Clamp(GetDecimal(s, "fade_in_duration", 0m), 0m, 30m);
            fadeOutValue = Clamp(GetDecimal(s, "fade_out_duration", 0m), 0m, 30m);
            shiftValue = Clamp(GetDecimal(s, "shift", 1.0m), 0.5m, 10m);
            mp3BitrateValue = GetString(s, "mp3_bitrate", "128k");
            mp3SampleRateValue = (int)Clamp(GetDecimal(s, "mp3_sample_rate", 48000m), 44100m, 48000m);
            sdEnabled = GetBool(s, "sd_enabled", false);
            sdUrl = GetString(s, "sd_url", "http://127.0.0.1:7860");
            sdPath = GetString(s, "sd_path", @"G:\stable-diffusion-webui");
            sdModel = GetString(s, "sd_model", "");
            sdSteps = (int)Clamp(GetDecimal(s, "sd_steps", 20m), 8m, 40m);
            sdSize = (int)Clamp(GetDecimal(s, "sd_size", 512m), 512m, 768m);
            coverPath = GetString(s, "cover_path", Paths.CoverFile);
            if (!File.Exists(coverPath)) coverPath = "";
            chkInstrumental.Checked = GetBool(s, "instrumental", true);
            chkThinking.Checked = GetBool(s, "thinking", true);
            chkKeepModel.Checked = GetBool(s, "keep_model_on_cancel", false);
            chkAutoVideo.Checked = GetBool(s, "auto_video", true);
            chkAutoBpm.Checked = GetBool(s, "auto_bpm", true);
            SelectCombo(comboKey, GetString(s, "keyscale", ""));
            SelectCombo(comboSignature, GetString(s, "timesignature", ""));
            SelectCombo(comboLanguage, GetString(s, "vocal_language", "unknown"));
            SelectCombo(comboFormat, GetString(s, "audio_format", "wav"));
            SelectCombo(comboOffload, GetString(s, "offload_mode", "auto"));
            numBpm.Value = Clamp(GetDecimal(s, "bpm", 78m), numBpm.Minimum, numBpm.Maximum);
            numDuration.Value = Clamp(GetDecimal(s, "duration", 60m), 10m, 7200m);
            SetDurationFromSeconds((int)numDuration.Value);
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
                s["theme_dark"] = ThemeManager.Dark;
                s["layout_split_ratio"] = CurrentSplitRatio();
                s["lm_negative_prompt"] = negativePrompt;
                s["fade_in_duration"] = (double)fadeInValue;
                s["fade_out_duration"] = (double)fadeOutValue;
                s["shift"] = (double)shiftValue;
                s["mp3_bitrate"] = mp3BitrateValue;
                s["mp3_sample_rate"] = mp3SampleRateValue;
                s["sd_enabled"] = sdEnabled;
                s["sd_url"] = sdUrl;
                s["sd_path"] = sdPath;
                s["sd_model"] = sdModel;
                s["sd_steps"] = sdSteps;
                s["sd_size"] = sdSize;
                s["cover_path"] = coverPath;
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
                s["keep_model_on_cancel"] = chkKeepModel.Checked;
                s["auto_video"] = chkAutoVideo.Checked;
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


        internal void StartSingleUiTest()
        {
            Shown += delegate
            {
                WindowState = FormWindowState.Minimized;
                uiTestTimer = new WinTimer();
                uiTestTimer.Interval = 1500;
                uiTestTimer.Tick += OnSingleUiTestTick;
                uiTestTimer.Start();
            };
        }

        private void OnSingleUiTestTick(object sender, EventArgs e)
        {
            uiTestTicks++;
            try
            {
                if (uiTestState == 0)
                {
                    uiTestState = 1;
                    AppendLog("=== UI TEST SINGLE: generate ===");
                    OnGenerate(this, EventArgs.Empty);
                    return;
                }
                if (uiTestState == 1)
                {
                    string cover = lastOutputFile.Length > 0
                        ? Path.ChangeExtension(lastOutputFile, ".cover.png") : "";
                    if (cover.Length > 0 && File.Exists(cover))
                    {
                        FinishUiTest(0);
                        return;
                    }
                    if (lblStatus.Text.Contains("SD 封面失敗") ||
                        lblStatus.Text.Contains("失敗 (exit") ||
                        lblStatus.Text.Contains("錯誤:"))
                    {
                        FinishUiTest(2);
                        return;
                    }
                    if (uiTestTicks > 900) FinishUiTest(3);
                }
            }
            catch (Exception ex)
            {
                AppendLog("UI TEST SINGLE error: " + ex.Message);
                FinishUiTest(4);
            }
        }

        internal string PlayerTest(string path)
        {
            LoadAudio(path);
            audioOutput.Volume = 0f;
            audioOutput.Play();
            System.Threading.Thread.Sleep(900);
            string result = "state=" + audioOutput.PlaybackState +
                            " position=" + audioStream.CurrentTime.TotalSeconds.ToString("F2") +
                            " duration=" + audioStream.TotalTime.TotalSeconds.ToString("F2");
            DisposeAudio();
            return result;
        }

        internal string AutoVideoTest(string path)
        {
            string output = Path.ChangeExtension(path, ".mp4");
            try
            {
                if (File.Exists(output)) File.Delete(output);
            }
            catch (Exception ex)
            {
                return "delete_failed " + ex.Message;
            }
            chkAutoVideo.Checked = true;
            QueueAutoVideo(path);
            DateTime deadline = DateTime.Now.AddMinutes(5);
            while (DateTime.Now < deadline)
            {
                if (File.Exists(output))
                {
                    try
                    {
                        using (FileStream stream = File.Open(output, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            return "ok output=" + output + " bytes=" + stream.Length;
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
                System.Threading.Thread.Sleep(200);
            }
            return "timeout output=" + output;
        }

        internal string StyleReport()
        {
            return string.Format("multiline={0} wordwrap={1} width={2} client={3} font={4} textlen={5} lines={6} capH={7} logH={8}",
                txtCaption.Multiline, txtCaption.WordWrap, txtCaption.Width, txtCaption.ClientSize.Width,
                txtCaption.Font.Height, txtCaption.TextLength, txtCaption.Lines.Length, txtCaption.Height, txtLog.Height) +
                string.Format(" scroll={0}", txtCaption.ScrollBars) +
                string.Format(" size={0} client={1} scale={2} dims={3} current={4}", Size, ClientSize, AutoScaleMode, AutoScaleDimensions, CurrentAutoScaleDimensions);
        }

        internal void StartUiTest(bool batch)
        {
            uiTestBatch = batch;
            Shown += delegate
            {
                WindowState = FormWindowState.Minimized;
                if (batch)
                {
                    txtCaption.Text = "Warm lo-fi piano instrumental, no vocals.\r\nMellow acoustic guitar instrumental, no vocals.";
                    chkBatch.Checked = true;
                }
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
                    if (uiTestBatch)
                    {
                        if (lblStatus.Text.Contains("批次完成")) { FinishUiTest(0); return; }
                        if (uiTestTicks > 900 || lblStatus.Text.Contains("失敗")) { FinishUiTest(2); return; }
                        return;
                    }
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
            if (albumProcess != null && !albumProcess.HasExited)
                ProcessSupervisor.StopTree(albumProcess, 5000);
            if (telegramProcess != null && !telegramProcess.HasExited)
                ProcessSupervisor.StopTree(telegramProcess, 5000);
            SdCoverClient.Stop();
            DisposeAudio();
            finishTimer.Stop();
            logTimer.Stop();
            daemonIdleTimer.Stop();
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
                    ProcessSupervisor.StopTree(running, 3000);
                }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
