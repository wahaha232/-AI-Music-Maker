# 程式碼審查修正紀錄

日期：2026-11-17  
來源審查：`AI_Music_Maker_Review_20261117.md`（Telegram 收到的審查檔）

## 已修正

| 編號 | 修正內容 |
|---|---|
| P0-1 | `run_job()` 改為回傳 `(code, Path)`；專輯直接使用回傳音檔，不再用 mtime glob，避免把 `.json` 當音檔。 |
| P0-2 | 專輯先把 `prompt`／`duration` 合併進設定再 `normalize_config()`，超界 duration 會被拒絕。 |
| P0-3 | 音訊統計改為 `sf.info`＋`sf.blocks` 分塊計算 peak／RMS，不再整檔讀入記憶體；格式不支援時只跳過統計。 |
| P0-5 | Telegram 日誌與訊息加入 token redaction，例外訊息不再輸出 bot token。 |
| P0-6 | 關閉 App 時 Telegram 子程序改用 `taskkill /T /F`，避免 `generate_backend.py` 變成孤兒。 |
| P0-7 | 測試改為 10–7200 秒，新增專輯檔名與 duration 驗證回歸測試；`build.ps1` 會執行 `unittest`，失敗即中止。 |
| P0-8 | `verify_migration.ps1` 發現殘留舊路徑時會呼叫 `Add-Failure`，不再誤報 PASS。 |
| P1-3 | 專輯資料夾改用 `currentAlbumFolder`，不再永久改寫主畫面輸出資料夾。 |
| P1-4 | 批次完成時補上 `TryTagOutput()`；SD 封面仍未排入批次佇列。 |
| P1-6 | 影片預設改為 5 fps、`veryfast`、CRF 23、48 kHz，並用 `-t` 指定音訊長度；8 分鐘轉檔由約 7 分鐘降到約 81 秒，長度 8:00.00。 |
| P1-7 | TagLib 開檔改用 `using`，避免檔案鎖住。 |

## 尚未修正（需較大變更）

> 此段為第 1 批當時的紀錄；後續批次已完成多項，最新狀態請見文末「最新狀態（2026-11-17）」表。

- P0-4 GPU 跨程序互斥：仍需單一生成服務或跨程序鎖；目前 App daemon、Telegram、SD 仍可能同時搶 VRAM。
- P1-1 取消保留模型：仍是軟取消，daemon 會把目前曲跑完。
- P1-2 只把警告門檻改為 `maxDurationWithLm`；未自動關閉 thinking 或阻擋超界。
- P1-5 日誌進度行仍會整段重寫，長日誌可能卡頓。
- P1-8 SD WebUI 啟動埠與 static 程序鎖未處理。
- P1-9 其他小問題（LinkAnalyzer 子字串、UI 跨執行緒、原子寫入、seed 溢位等）未處理。
- P2 架構重構（拆 MainForm、狀態機、JSON Lines job id）未處理。

## 驗證

- `python -m unittest discover -s AI_Music\tests`：9 tests OK。
- `build.ps1`：編譯＋selftest＋unittest 通過。
- `--ui-selftest`：通過（時長 2h、模板 10、媒體工具 15 控制項）。
- 影片實測：6 秒輸出 6.00 秒、5 fps、48 kHz；8 分鐘輸出 8:00.00，轉檔約 81 秒。
- Python 語法檢查：`generate_backend.py`、`telegram_bot.py`、`audio_edit.py`、`music_to_video.py` 全部通過。

## 第 2 批（2026-11-17）

分支：`fix/review-batch2`
Commit：`f8cbcf0`

| 項目 | 修改檔案 | 狀態 | 驗證證據 |
|---|---|---|---|
| N1 專輯驗證中斷 | `generate_backend.py`、`MainForm.cs`、`test_backend.py` | 通過（單元測試）；App 實機未驗證 | 13 tests OK；全 invalid CLI 回傳 1 並輸出 `[error]`／`[albumdone]`；`album.json` 含 `invalid` |
| P0-4 GPU 跨程序鎖 | `app/gpu_lock.py`、`generate_backend.py`、`MainForm.cs`、`Paths.cs`、`telegram_bot.py`、`test_gpu_lock.py`、`.gitignore` | 後端與單元測試通過；App／Telegram 實機未驗證 | 子程序持鎖時 `acquire` 拋 `GpuBusy`，kill 後可取得；`run_oneshot` 回傳 75 且未呼叫 `load_handlers`；backend CLI 持鎖測試 exit 75＋`[busy]` |
| P1-2 超時確認 | `MainForm.cs` | 程式與 UI selftest 通過；實機對話框未驗證 | `--ui-selftest` 通過；對話框三分支未手動操作 |
| N2 文件同步 | `YOUTUBE_AUTO_UPLOAD_PLAN.md`、`AUTOMATION_REPORT_20261117.md` | 通過 | `git diff` 僅顯示指定兩處 |

### N1 做法

- `run_album()` 在 `load_handlers()` 前先驗證全部曲目；失敗輸出
  `[error] album track <N> invalid: <原因>`，寫入 `invalid`。
- 全部不合法時不載入模型，仍寫 `album.json`（`tracks: []`）並輸出 `[albumdone]`。
- 合法曲目生成時若 `run_job` 拋例外，記錄到 `invalid` 後繼續；`album.json` 在
  `finally` 寫出，已完成的曲目不會遺失。
- `main()` 的 `--album` 加入 try/except，例外輸出 `[error] <類型>: <訊息>`。
- App `OnAlbum()` 拒絕任何超過 7200 秒的曲目，列出實際行號與內容後不送出。

### P0-4 做法

- 新增 `app/gpu_lock.py`：使用 `msvcrt.locking` 鎖定 `AI_Music/.gpu.lock`，
  持有者資訊寫入 `.gpu.owner.json`，程序死亡由 OS 釋放。
- `run_oneshot`、`run_daemon`、`run_album` 在載入模型前取得鎖，釋放模型後釋放；
  `--preflight`、`--limits` 不取鎖。
- 拿不到鎖輸出 `[busy] <owner>`、`[error] GPU 使用中`，exit code 75。
- App daemon／專輯啟動參數加 `--gpu-wait 20`；收到 `[busy]` 更新狀態列，
  exit 75 顯示 MessageBox 並恢復 UI。
- Telegram `/gen`、`/album` 遇到 exit 75 讀取 run.log 的 `[busy]` 並回覆使用者。

### P1-2 做法

- `ConfirmLongDuration()`：當 `chkThinking.Checked` 且超過 `maxDurationWithLm` 時，
  顯示 Yes／No／Cancel；Yes 關閉 LM 思考，Cancel 不生成。
- 單曲在送出前問一次；批次在開始前問一次；專輯以最長曲目問一次。
- `uiTestTimer != null` 時略過對話框，避免 UI 測試卡住；原警告日誌保留。

### N2 做法

- YouTube Gate 改為 `fps ≥ 1（目前預設 5 fps 靜態畫面）`。
- 自動化報告只在測試成品表格下方新增一行 P1-6 註記，未改寫歷史內容。

### 實機測試結果

- 後端持鎖 CLI：`[busy] pid=... source=child`、`[error] GPU 使用中`、exit 75（通過）。
- Telegram `--test-gen` 搭配假鎖：`run.log` 出現 `[busy]`／`[error]`，backend exit 75；
  未獨立確認聊天室訊息（未驗證）。
- App daemon 常駐後從 Telegram `/gen` 的完整 OOM 驗證：**未執行／未驗證**
  （需要載入模型與實際 Telegram 收發）。
- 關閉 App 後 `/gen` 成功、切換 offload 後重啟：**未執行／未驗證**。
- P1-2 15 分鐘對話框三分支：**未執行／未驗證**（UI selftest 僅確認不卡住）。

### 範圍外發現（只記錄，未修）

- 批次模式仍不會產生 SD 封面；目前只補標籤與自動影片。
- App daemon 生成完成後仍常駐並持有 GPU 鎖，遠端 `/gen` 會一直被視為忙碌；
  沒有「卸載模型」按鈕，唯一釋放方式是關閉 App。
- P1-1 協作式取消、P1-5 日誌效能、P1-8 SD 啟動鎖、P1-9 小問題與 P2 重構仍未處理。

### 範圍外變更（補記）

1. 時長新增「秒」欄位，最短可設 10 秒（原本最短 1 分鐘）；`SetDurationFromSeconds` 不再進位到整分鐘，舊設定檔的 90 秒現在顯示為 1 分 30 秒。
2. 專輯完成後，每首都會加入自動轉影片佇列（`QueueAutoVideo`）。

## 第 3 批（2026-11-17）

分支：`fix/review-batch3`
Commit：`be0ca57`（主要）、`8541887`（N7 backend 測試）

| 項目 | 修改檔案 | 狀態 | 驗證證據 |
|---|---|---|---|
| N3 時長欄位截斷 | `MainForm.Layout.cs` | 通過 | `build.ps1` 通過；截圖 `shots_batch3\dark_150.png`、`shots_batch3\dark_100_max.png` 可見 0 時 8 分 0 秒 |
| N4 補記第 2 批 | `CODE_REVIEW_FIXES_20261117.md` | 通過 | 僅新增「範圍外變更（補記）」兩點 |
| N6 測試專用 GPU 鎖 | `gpu_lock.py`、`test_gpu_lock.py` | 通過 | 13 tests OK；正式 `.gpu.lock`／`.gpu.owner.json` 前後 SHA256 與時間戳不變；另持正式鎖執行 unittest 仍 OK |
| N7 測試中文輸出 | `test_backend.py`、`test_gpu_lock.py` | 通過 | PowerShell 輸出 `[error] GPU 使用中` 正常，13 tests OK |

### N3 做法

- 時長列改為標題獨立一行，欄位另起一列：`S(80)`／`S(84)`／`S(84)`。
- 150% 截圖：`G:\AIMUSIC\AI_Music\shots_batch3\dark_150.png`。
- 100% + `AIMUSIC_SHOT_SIZE=max` 截圖：`G:\AIMUSIC\AI_Music\shots_batch3\dark_100_max.png`。

### N6 做法

- `gpu_lock.py` 改為執行時讀取 `AIMUSIC_GPU_LOCK_DIR`；未設定才使用 `AI_Music`。
- 測試 `setUpClass` 建立暫存鎖目錄，子程序以 `env=os.environ.copy()` 繼承。
- 正式鎖狀態檢查：`.gpu.lock`、`.gpu.owner.json` 測試前後 SHA256、大小、mtime 完全相同。
- 實測正式鎖被持有時，unittest 仍 13 tests OK。

### N7 做法

- `test_backend.py`、`test_gpu_lock.py` 在 import `generate_backend` 後呼叫 `backend._stdio()`。
- 驗證輸出：`[error] GPU 使用中` 中文正常顯示。

### 範圍外發現（只記錄，未修）

- N5 App 閒置自動卸載模型未實作（本批明令不做）。
- SD WebUI 仍未取得 `gpu_lock`；目前只在啟動 SD／Telegram 前釋放 ACE-Step daemon。
- 批次 SD 封面與 Telegram 切換釋放模型已在前一個 commit `1335653` 完成，不属于本批。

## 第 4 批（2026-11-17）

分支：`fix/review-batch4`
Commit：`f3512ce`

| 項目 | 修改檔案 | 狀態 | 驗證證據 |
|---|---|---|---|
| P0-01 SD GPU 鎖 | `scripts/gpu_lock_hold.py`、`src/GpuLease.cs`、`Paths.cs`、`MainForm.cs` | 程式完成；SD 實機未驗證 | GPU lease helper 實測：持鎖時 `GpuBusy`、送 `quit` 後可再取得；build 通過 |
| P1-01 Daemon 閒置卸載 | `MainForm.cs` | 程式完成；5 分鐘實機未驗證 | 閒置 timer、Telegram/SD 前釋放；build 通過 |
| P1-02 模型參照釋放 | `generate_backend.py` | 程式完成；VRAM 實機未驗證 | `release_models` 後再清空 dit／llm 並再次 empty_cache；13 tests OK |
| P1-03 長時長共用警告 | `generate_backend.py`、`MainForm.cs` | 部分完成（僅結構化警告） | 新增 `[warn] thinking_duration_over_lm ...`；未拒絕高風險設定 |
| P1-04 SD 埠與啟動互斥 | `SdCoverClient.cs` | 程式完成；實機未驗證 | 由 URL 解析 Port；`EnsureApi` 加入 StartGate；build 通過 |
| P1-05 fallback／Sidecar | `generate_backend.py` | 程式完成；故障注入未驗證 | fallback 只接受音訊副檔名並以 `sf.info` 驗證；sidecar 先寫 `.tmp` 再 `os.replace` |
| P1-07 Telegram 設定快照 | `telegram_bot.py` | 程式完成；實機未驗證 | `run_job` 建立 `job_settings`；fade／video 只讀快照 |

### 驗證

- `python -m py_compile`：`generate_backend.py`、`gpu_lock.py`、`gpu_lock_hold.py`、`telegram_bot.py` 通過。
- `build.ps1`：編譯＋selftest＋13 tests OK。
- `--ui-selftest`：通過。
- `GpuLease` helper 實測：`[locked]` → 另一個 acquire 得到 `GpuBusy` → 送 `quit` 後可重新取得。
- SD 封面與 ACE-Step 不重疊的完整 OOM 實機驗證：**未驗證**。
- Daemon 閒置 5 分鐘卸載：**未驗證**。

### 範圍外（未修）

- P1-06 統一 ProcessSupervisor 未實作。
- P2-01 鎖例外分類（檔案系統錯誤仍可能被當成 GpuBusy）未修。
- P2-02 協作式鎖仍不涵蓋外部 GPU 程式；文件未新增 UI 說明。
- P2-03 舊審查段落仍有過時狀態。
- P2-04 相依套件未產生完整 lock 檔。

## 第 5 批（2026-11-17）

分支：`fix/review-batch5`
Commit：`33dfd4a`

| 項目 | 修改檔案 | 狀態 | 驗證證據 |
|---|---|---|---|
| P1-03 Backend 長時長防護 | `generate_backend.py`、`MainForm.cs`、`telegram_bot.py`、`test_backend.py` | 通過 | `thinking=True` 且 >480 秒預設拒絕；`allow_long_thinking=true` 才放行；Telegram 長曲自動關閉 thinking；15 tests OK |
| P1-06 統一 ProcessSupervisor | `ProcessSupervisor.cs`、`MainForm.cs` | 通過（程式與建置） | Daemon／專輯／Telegram 改用 StopTree，taskkill 後等待並確認退出；build OK |
| P2-01 鎖例外分類 | `gpu_lock.py`、`generate_backend.py`、`gpu_lock_hold.py`、`test_gpu_lock.py` | 通過 | 新增 `GpuLockError`；檔案系統錯誤不再誤報 GpuBusy；15 tests OK |
| P2-02 協作鎖範圍 | `MainForm.Layout.cs` | 部分完成（UI 提示＋文件） | Offload 下拉加入 tooltip：只涵蓋本程式／Telegram／SD 封面 |
| P2-03 文件狀態 | `CODE_REVIEW_FIXES_20261117.md` | 通過 | 新增本「最新狀態」表，不改寫歷史批次 |
| P2-04 相依版本鎖定 | `requirements_lock.txt`、`scripts/check_lock.py`、`build.ps1` | 通過 | 69 packages；`build.ps1 -Preflight` 顯示 lock matched |

### 驗證

- `build.ps1`：編譯＋selftest＋15 tests OK。
- `build.ps1 -Preflight`：GPU 偵測、backend preflight、requirements lock（69 packages）全部通過。
- `--ui-selftest`：通過。
- 長時長：backend 拒絕 `thinking=True, duration=481`；`allow_long_thinking=true` 可通過。
- ProcessSupervisor：程式編譯通過；實際取消／關閉 App 的程序退出未做大規模實機測試。
- 實機短曲：`--ui-test` 以 10 秒設定生成，daemon 持有鎖，外部 probe 得到 `BUSY pid=17484 source=daemon`；UI test exit 0；產出 MP3 765,581 bytes＋MP4 326,144 bytes；結束後 probe `ACQUIRED`，Lock 已釋放；測試設定已還原。

### 最新狀態（2026-11-17）

| 項目 | 狀態 |
|---|---|
| P0-01 SD GPU 鎖 | 已實作 lease；實機 OOM 未驗證 |
| P1-01 Daemon 卸載／遠端交接 | 已實作（Telegram／SD 前釋放＋5 分鐘閒置）；實機未驗證 |
| P1-02 模型參照釋放 | 已實作；VRAM 實機未驗證 |
| P1-03 長時長防護 | 已實作 Backend 拒絕＋桌面 opt-in＋Telegram 自動關閉 thinking |
| P1-04 SD 埠／互斥 | 已實作；實機未驗證 |
| P1-05 fallback／Sidecar | 已實作；故障注入未驗證 |
| P1-06 ProcessSupervisor | 已實作 |
| P1-07 Telegram 設定快照 | 已實作；實機未驗證 |
| P1-08 測試鎖隔離 | 已完成 |
| P2-01 鎖例外分類 | 已完成 |
| P2-02 協作鎖範圍 | 已加 UI 提示與文件；仍非硬體鎖 |
| P2-03 文件狀態 | 已完成 |
| P2-04 相依版本鎖定 | 已完成 |

### 範圍外（未修）

- SD／ACE-Step 完整 OOM 實機驗證、Telegram 併發與 5 分鐘閒置卸載仍需實機測試。
- P2-02 本質上是協作式鎖，外部程式仍不受保護。

## 第 6 批（SD 實機驗證）

分支：`fix/review-batch6`
Commit：提交後填入

### 實機測試

- 第一次 `--ui-test-single`（SD 啟用、10 秒曲）：SD API 就緒，但封面請求「作業逾時」，測試 exit 3；無 cover。
- 診斷 `--sd-test`：SD WebUI 可正常啟動並產生 351,584 bytes cover。
- 修正：`SelectModel` timeout 60s→300s；`GenerateCover` 300s→900s；SD 失敗時 `SdCoverClient.Stop()`；狀態列顯示 SD 失敗原因；新增 `--gpu-lease-test`。
- 第三次 `--ui-test-single`：**exit 0**，產出 `ace_20261117_220745_seed2479470960.cover.png` 411,045 bytes；MP3 573,439 bytes；測試結束後 GPU lock 可重新取得，SD 程序已停止。
- 因為 `--ui-test-single` 在 cover 完成後立即關閉，該次沒有等到自動影片完成；正常 App 保持開啟時會照常轉 MP4。

### 驗證

- `build.ps1`：編譯＋selftest＋15 tests OK。
- `--gpu-lease-test`：`locked`，exit 0。
- SD 封面、GpuLease、ACE daemon 釋放流程已實機通過。
- 尚未大規模測試 Telegram 併發與 5 分鐘閒置卸載。
