# AI Music Maker 自動化建置報告

日期：2026-11-17  
範圍：測試音樂＋封面＋自動轉影片、時長與提示詞 UI、風格模板、GPU 面板評估、YouTube 上傳規劃

## 1. 總結

- 已完成 8 分鐘測試曲的生成、封面套用與自動轉 MP4，並實際驗證可播放。
- 已完成時長上限 120 分鐘、小時／分鐘輸入、提示詞高度解除固定上限、10 組音樂風格模板。
- GPU 監控面板已完成評估，結論是不直接併入 AI Music Maker，避免與 ACE-Step 搶 VRAM、連接埠與 F.A.H. 控制權。
- YouTube 自動上傳已完成流程與條件規劃，依指示尚未開發。
- 已重新編譯 App 並通過 selftest、UI selftest、自動轉影片測試。

## 2. 測試成品

輸出資料夾：`G:\AIMUSIC\AI_Music\generated`

| 檔案 | 大小 | 說明 |
|---|---:|---|
| `ace_20261117_021553_seed1706262108.mp3` | 7,813,920 bytes | 480.0 秒、48 kHz、立體聲、MP3 |
| `ace_20261117_021553_seed1706262108.json` | 2,318 bytes | 生成參數與音訊資訊 |
| `ace_20261117_021553_seed1706262108.mp4` | 17,993,038 bytes | 1920×1080、30 fps、H.264、AAC |

- 封面來源：`G:\AIMUSIC\AI_Music\app\cover.png`（目前 SD 封面未啟用，因此使用 App 預設封面）。
- 生成時間：約 591 秒（8 分鐘曲）。
- 影片轉檔時間：約 7 分鐘。
- MP4 實測長度：8 分 1.77 秒；音訊 480 秒，結尾多約 1.77 秒，為 MP3 解碼尾端補齊，不影響播放。
- 已抽格確認畫面為模糊背景＋中央封面，正常顯示。

## 3. 已完成變更

### 3.1 時長放寬與小時／分鐘

- 時長上限由 600 秒提高到 7200 秒（120 分鐘）。
- 主畫面改為「時」＋「分」兩個輸入，不再直接輸入秒數。
- 內部仍以秒傳給後端，設定檔 `duration` 保持秒數，相容舊設定。
- 專輯對話框每首長度上限同步提高到 7200 秒。
- 後端 `generate_backend.py` 驗證範圍改為 10–7200 秒。

驗證：

- `duration_max=2h0m total=7200`
- 舊設定 480 秒載入為 `0h8m total=480`
- 後端實測接受 7200、拒絕 7201。

### 3.2 提示詞高度

- 移除 `260 px` 固定上限，數字上限改為 10000。
- 高度只受目前視窗可用空間限制；在 1080p 最大化下實測可到 294 px。
- 新增「放大」按鈕，開啟 1000×760 可調大小的大型提示詞編輯視窗，可輸入任意長度內容。

### 3.3 音樂風格模板

新增 10 組模板，套用時會設定：

- 提示詞（英文，利於模型理解）
- BPM
- 調性
- 拍號
- 純音樂開關
- 歌詞欄設為 `[Instrumental]`

模板清單：

1. Lo-fi 深夜雨景
2. Chillhop 咖啡廳
3. Ambient 漂浮
4. Cinematic 電影感
5. Acoustic 木吉他
6. Jazz 深夜鋼琴
7. Synthwave 復古
8. EDM 柔和電子
9. Rock 溫暖抒情
10. Hip-Hop 慵懶

介面位置：BPM／時長同一列右側「模板」下拉選單＋「套用」按鈕。  
實測套用第一組：`72 BPM / C Minor / 206 chars`，控制項未超出列寬（右緣 1449 / 列寬 1476）。

### 3.4 自動轉影片

- 生成完成後依「自動轉影片」勾選自動排入轉檔佇列。
- 單曲與批次皆支援；SD 封面啟用時會等封面完成再轉。
- 已存在且大於 1 KB 的 MP4 會跳過；未完成的小檔會刪除重轉。
- 使用背景單一佇列，避免同時多個 ffmpeg 搶 CPU。

## 4. GPU 監控面板評估

評估對象：`D:\VS CODE\PROJECT\開啟AI監控面板.bat` 與 `GPU Intelligent Regulation System\ai_proxy_daemon.py`。

### 4.1 實際內容

- 不是單純的 GPU 監控面板，而是：
  - OpenAI 相容代理（127.0.0.1:8080）
  - 管理 llama-server（127.0.0.1:8081）
  - 載入 Qwen GGUF 模型
  - 協調 Folding@home 暫停／恢復
  - 休眠時釋放 VRAM
- 使用 nvidia-smi 讀取 GPU 使用率、VRAM、溫度與功耗。

### 4.2 結論

不建議直接整合進 AI Music Maker。

原因：

- 它會額外啟動一個 LLM 服務，與 ACE-Step 爭用 VRAM。
- 連接埠 8080／8081 與現有服務可能衝突。
- 它會控制 Folding@home，與音樂生成流程無關，誤操作風險高。
- 它無法控制 ACE-Step 的模型卸載；對 ACE-Step 的 VRAM 智慧調節沒有實質幫助。

建議：

1. 若要監控，只在 App 讀取 `nvidia-smi` 顯示使用率、VRAM、溫度、功耗（唯讀）。
2. 若要完整面板，維持獨立視窗，不併入 App。
3. 若要智慧調節 ACE-Step，應在生成流程中加入「生成前後卸載模型／清空 CUDA 快取」，而不是接這支 daemon。

## 5. YouTube 自動上傳規劃

完整內容見：`G:\AIMUSIC\AI_Music\YOUTUBE_AUTO_UPLOAD_PLAN.md`

重點：

- 使用 YouTube Data API v3 與 OAuth 2.0 Desktop app。
- 每次上傳約 1,600 quota units；每日 10,000 units，實務上每天約 6 支為上限。
- 上傳前必須通過：MP4 可解析、格式正確、音軌存在、標題／說明／標籤長度、縮圖 1280×720 且小於 2 MB、無重複內容、token 有效、quota 足夠。
- 建議預設上傳為「私人」或「不公開」，確認後再公開。
- 狀態機：pending → validated → uploading → uploaded → thumbnail_set → playlist_set → published / failed。
- 4xx 不重試；5xx／網路錯誤最多重試 3 次，使用 resumable upload。
- 憑證以 DPAPI 加密保存，不進 Git。
- 分三階段：手動上傳 → 半自動（私人）→ 全自動排程。

## 6. 驗證紀錄

| 項目 | 結果 |
|---|---|
| `build.ps1` 編譯 | 通過 |
| `--selftest` | 通過 |
| `--ui-selftest` | 通過，`duration_max=2h0m`、`style_templates=10`、`prompt_max_height=294` |
| 後端 7200 秒驗證 | 接受 |
| 後端 7201 秒驗證 | 拒絕 |
| `--auto-video-test` | 通過，產出 187,142 bytes MP4 |
| 實際 8 分鐘曲轉檔 | 通過，17,993,038 bytes、1920×1080、H.264＋AAC |
| 影片抽格 | 封面與模糊背景正常 |

## 7. 限制與風險

1. **120 分鐘目前只是設定可接受，不代表引擎一定能單次生成。**  
   ACE-Step 在 12 GB GPU 上的單次生成上限依官方文件約為 4–8 分鐘；超過時可能耗盡 VRAM 或失敗。若要真正支援 120 分鐘，需另做「長曲分段生成＋交叉淡入串接」，本次未實作。

2. 8 分鐘影片以 libx264 medium 轉檔約需 7 分鐘；120 分鐘影片會非常久，建議之後改用較快 preset 或硬體編碼。

3. SD 封面目前未啟用，測試成品使用 App 預設封面；啟用 SD 後會優先生成 `<歌名>.cover.png` 並用於影片。

4. YouTube 上傳尚未開發，且需要 Google Cloud 專案、OAuth 授權與頻道驗證。

5. GPU 監控面板未整合，僅完成評估。

## 8. 建議下一步

1. 實作長曲分段生成與串接，才能真正支援 30–120 分鐘。
2. 在 App 加入唯讀 GPU 狀態列（nvidia-smi）。
3. 實作 YouTube 半自動上傳（私人）與 metadata／縮圖產生。
4. 啟用 SD 封面並驗證「生成 → 封面 → 影片」完整鏈路。
5. 將轉檔 preset 改為 `veryfast` 或加入 GPU 編碼選項，降低長曲轉檔時間。

## 9. 後續追加（2026-11-17）

### 9.1 媒體工具

App 輸出資料夾列新增「工具」按鈕，開啟「媒體工具」對話框：

- 淡入／淡出：對選取音檔套用，輸出 `<歌名>_fade.<副檔名>`，不覆蓋原檔。
- 音量正規化：批次套用 loudnorm，目標 -14 LUFS、TP -1.5 dB，輸出 `<歌名>_norm.<副檔名>`。
- 合併：多選 2 首以上，依清單順序合併為 `merged_日期_時間.mp3`。
- 預覽：顯示封面、播放音訊、進度拖曳；有同名 MP4 時可開啟影片。

底層：`AI_Music/scripts/audio_edit.py`（fade／normalize／merge）。
UI：`MediaToolsDialog.cs`、`PreviewDialog.cs`、`AudioEditRunner.cs`。

驗證：`--audio-edit-test` 通過，fade／normalize／merge 三項 exit code 均為 0；工具對話框建立成功（15 個控制項）。

### 9.2 Telegram 遠端控制

`telegram_bot.py` 已擴充，仍只接受 `telegram.json` 設定的 chat_id：

- `/gen <提示詞>`：生成並回傳音訊。
- `/album 名稱 | 提示詞1 ; 提示詞2`：專輯生成。
- `/status`：工作狀態、長度、影片／淡入淡出設定、近期檔案數。
- `/cancel`：終止目前生成程序樹。
- `/duration 95｜1:35｜1:02:03`：設定長度，支援 10 秒–2 小時。
- `/video on|off`：生成後自動轉 MP4 並回傳。
- `/fade 2 3`、`/fade off`：設定或關閉淡入淡出。
- `/merge 2`：合併最近 2–10 首。
- `/outputs`：列出最近檔案。

設定保存在 `%APPDATA%\AIMusicGenerator\telegram_settings.json`。  
啟用方式：App 上方「TG」按鈕，或手動執行 `python scripts/telegram_bot.py`。

驗證：語法檢查通過；`parse_duration` 實測 `1:35=95`、`1:02:03=3723`、`9=拒絕`；格式化 `7200=2:00:00`。

### 9.3 尚未完成

- 自動 metadata（標題／說明／標籤）尚未實作。
- YouTube 上傳佇列尚未實作（仍為規劃）。
- 長曲分段生成尚未實作。
- 遠端控制目前為 Telegram；尚未建立 Web 面板。
