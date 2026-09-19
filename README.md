# 3waSshDrive

<div align="center">

![3waSshDrive Mascot](snapshot/readme_mascot.jpg)

**「讓遠端的世界，成為你桌面的一部分！」**  
*Project a remote Linux workspace into Windows as a native drive letter through WinFsp and SFTP.*

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-informational.svg)](README.md)
[![.NET Framework](https://img.shields.io/badge/.NET-Framework%204.7.2-512BD4.svg)](README.md)
[![WinFsp](https://img.shields.io/badge/WinFsp-v2.1-orange.svg)](https://github.com/winfsp/winfsp)

</div>

---

## 🌟 簡介 (Introduction)

**3waSshDrive** 是專為工程師與 AI 協同開發（如 Antigravity、VS Code）打造的高效能工具。透過 **WinFsp** 原生檔案系統驅動與 **SFTP** 協定，直接將遠端 Linux 工作空間（例如 `/home/dev/project`）映射為 Windows 磁碟代號（如 `Z:`）。

不需透過 `cmd.exe`、不呼叫外部 `net use`，亦無需安裝肥重的第三方中介軟體。在 Windows 檔案總管、編輯器、Diff 工具或終端機中，即可享有原生磁碟等級的流暢操作體驗。

<div align="center">

![3waSshDrive Interface](snapshot/s1.png)

</div>

---

## ✨ 核心特色 (Key Features)

- ⚡ **完整讀寫與極速快取 (Full Read/Write & Metadata Cache)**
  - 支援完整檔案讀取、建立、覆寫、重新命名與刪除。
  - 內建高效能 LRU Metadata 快取，大幅縮減 Windows 檔案總管瀏覽深層目錄的 SFTP 延遲。
  - 支援「唯讀模式 (Read-only)」保護選項，防止重要伺服器環境被誤修改。

- 🛡️ **資安防護與防偽 (Security & MITM Protection)**
  - 嚴格的 **SHA-256 主機指紋 (Host Key Fingerprint)** 校驗流程，杜絕中間人攻擊（MITM）。
  - 支援 **SSH 私鑰 (Private Key)** 與 **密碼 (Password)** 驗證。
  - 密碼僅保留於記憶體中，絕不落盤儲存；設定檔僅記錄連線配置與私鑰路徑。

- 🔄 **斷線自動重連 (Disconnect Auto-Reconnect)**
  - **底層透明重試**：若檔案操作中途遭遇網路瞬斷，自動重新連線並重試該操作，避免拋出 `DEVICE_NOT_READY`。
  - **背景守護心跳**：10 秒定時探測連線健康度，WiFi 切換或網路恢復時自動重新掛載，系統匣同步跳出氣泡通知。

- 💽 **智慧槽位偵測 (Smart Drive Letter Detection)**
  - 自動偵測系統實體硬碟、虛擬光碟與現有網路磁碟，於下拉選單清楚標示 `(已掛載)`，防止槽位碰撞與覆蓋。

- 📋 **未預期異常崩潰日誌 (Crash Logger)**
  - 全方位攔截 UI 執行緒、AppDomain 與 TaskScheduler 異常。
  - 採用標準 PHP 日期格式儲存於 `log/Ymd.txt`（如 `log/20260919.txt`），當天多筆異常自動以附加模式 (Append) 完整保留 StackTrace。

- ☕ **系統匣常駐 (System Tray Mode)**
  - 點選視窗關閉按鈕預設最小化至右下角系統匣，背景安靜守護連線，不佔用工作列空間。

- 💡 **看板娘：芳寶 (Fang-Fang)**
  - 內建精緻流暢的滿版動畫看板娘，陪伴您的編程時光。
  - 具備連線狀態即時反饋、斷線加油打氣與多種趣味對話！

---

## 💻 系統需求 (Prerequisites)

1. **作業系統**：Windows 10 或 Windows 11 (x64)
2. **執行環境**：[.NET Framework 4.7.2](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472)
3. **檔案系統驅動**：[WinFsp v2.1](https://github.com/winfsp/winfsp/releases/tag/v2.1)（程式內建一鍵透過 `winget` 自動安裝按鈕）
4. **遠端伺服器**：支援 SSH/SFTP 的 Linux 主機

---

## 🚀 快速開始 (Quick Start)

### 1. 取得原始碼 (含 Submodules)

```powershell
git clone --recurse-submodules https://github.com/shadowjohn/3waSshDrive.git
cd 3waSshDrive
```

### 2. 一鍵建置與執行 (懶人腳本)

- 雙擊 **`build.bat`**：自動還原依賴、執行全套 44 項單元測試並編譯 Release 版本。
- 雙擊 **`run.bat`**：直接啟動應用程式。

### 3. 命令列建置 (Command Line)

```powershell
dotnet restore .\3waSshDrive.sln
dotnet test .\3waSshDrive.sln -c Release --no-restore
dotnet build .\src\3waSshDrive.App\3waSshDrive.App.csproj -c Release --no-restore
```

執行檔產出路徑：
```text
src\3waSshDrive.App\bin\Release\net472\3waSshDrive.exe
```

---

## 📖 使用教學 (Usage Walkthrough)

1. **建立連線設定檔 (Profile)**：
   - 輸入設定檔名稱、主機 IP / Domain、SSH Port (預設 22) 與登入帳號。
   - 設定遠端根目錄（如 `/home/john` 或 `/var/www/html`）。
   - 選擇登入方式（私鑰路徑或輸入密碼）。
   - 選擇欲掛載的 Windows 磁碟代號（如 `Z:`）。
2. **測試並信任 (Test & Trust)**：
   - 點擊「**▶ Test Mount (測試連線並掛載)**」或「**Test & Trust**」。
   - 系統會連線並自動擷取遠端伺服器的 SHA-256 主機指紋進行防偽綁定。
3. **掛載磁碟機 (Mount)**：
   - 點擊「**🖹 Mount (掛載)**」，成功後狀態燈轉為綠色 `●`。
4. **開啟工作空間 (Open Explorer)**：
   - 點擊「**📁 Open (開啟資料夾)**」，Windows 檔案總管立即展開該磁碟機。
   - 您可以直接在 VS Code、Antigravity 或任何編輯器中將該磁碟槽當作本機磁碟開啟與存檔！
5. **卸載 (Unmount)**：
   - 點擊「**⏏ Unmount (卸載)**」即可安全釋放磁碟槽。

---

## 📁 設定檔與紀錄檔位置 (Storage & Logs)

- **連線設定檔 (Profiles)**：
  ```text
  %APPDATA%\3waSshDrive\profiles.json
  ```
- **異常崩潰日誌 (Crash Logs)**：
  ```text
  log\YYYYMMDD.txt (例：log\20260919.txt)
  ```

---

## 👥 關於與版權資訊 (About & License)

- **專案名稱**：3waSshDrive
- **作者 (Author)**：羽山秋人 (shadowjohn)
- **團隊**：3WA 問題解決專家工作室
- **聯絡信箱**：linainverseshadow@gmail.com
- **專案原始碼**：[https://github.com/shadowjohn/3waSshDrive](https://github.com/shadowjohn/3waSshDrive)
- **看板娘**：芳寶 (Fang-Fang / 3WA-chan)
- **授權協議**：[GNU General Public License v3.0 (GPLv3)](LICENSE)

---

<div align="center">
  <sub>Linux × Windows = More Freedom. Made with 💙 by 3WA Studio.</sub>
</div>
