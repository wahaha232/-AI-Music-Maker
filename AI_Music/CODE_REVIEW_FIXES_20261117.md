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
