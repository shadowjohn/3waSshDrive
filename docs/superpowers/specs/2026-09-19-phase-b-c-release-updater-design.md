# Phase B 發行建置與 Phase C 自動更新設計

- 狀態：設計已核准，等待書面規格 review
- 日期：2026-09-19
- 適用專案：3waSshDrive
- 實作順序：Phase B → Phase C；Phase D 延後另案執行

## 1. 目的

Phase B 先建立可重複、可下載、可驗證的 Windows 發行流程，讓 GitHub Actions 能產出可直接測試的 EXE 壓縮包，以及正式版本使用的 per-user 安裝程式。

Phase C 再以 Velopack 加入應用程式內更新：使用者看到新版後可選擇更新或稍後，程式在安全卸載磁碟與停止背景工作後安裝新版並重新啟動。

Phase D 的實機效能量測與優化不屬於本輪實作，僅在本文記錄邊界，之後回本機環境另開規格。

## 2. 成功條件

1. 每次 push 與 pull request 都能在 Windows runner 建置、測試並提供 ZIP artifact。
2. 符合格式的版本 tag 能建立包含 ZIP、per-user Setup、Velopack 更新 feed 與 SHA-256 清單的正式 GitHub Release。
3. Setup 安裝版能檢查 GitHub Release、提示使用者、下載、驗證、安裝並重新啟動。
4. 更新前若無法安全卸載磁碟，必須取消更新，既有版本繼續可用。
5. Portable ZIP 不啟用自動更新，避免來源與安裝狀態不明。
6. 正常啟動、自我檢查與更新重啟均不會被殘留的 lock.pid 卡住。
7. build.bat 與 run.bat 保留，讓本機與 CI 使用相同入口。

## 3. 非目標

- 本輪不進行 Phase D 的效能量測或效能調校。
- Setup 不內含 WinFsp 安裝包。
- 應用程式更新器不負責升級 WinFsp。
- 不強制 Authenticode 簽章；沒有憑證時仍可產出正式版本。
- Microsoft Security Intelligence 線上送檢完全在 workflow 外，由維護者下載 Setup 後自行上傳；Release 不等待送檢結果。
- 不製作無提示、強制安裝的背景更新。
- 不自行開發更新格式、差分演算法或安裝器。

## 4. 已選方案

採用 Velopack 處理 per-user 安裝、更新 feed、套件下載、完整性驗證、套用更新與重新啟動。3waSshDrive 仍為 .NET Framework 4.7.2 應用程式。

選擇 Velopack 的原因是安裝器與更新器使用同一套發行資料，可減少 Inno Setup 加另一套 updater 的整合面；自行開發 updater 則會增加程序交接、檔案替換、驗證與回復風險。

參考 lattice-term 的使用體驗與發行概念：啟動後非阻塞檢查、顯示新版、使用者確認、下載安裝、安全重啟。實作不引用其 Tauri updater 程式碼，改用適合本專案的 Velopack API。

## 5. 版本規則

### 5.1 對外版本

正式 tag 與 UI 顯示採用：

vYYYY.MM.DD.RR

規則如下：

- YYYY、MM、DD 必須構成有效日期。
- RR 為同一天的發行序號，範圍 01–99。
- tag 必須符合固定寬度格式，例如 v2026.09.19.01。
- 同一 tag 不可重用。

### 5.2 Velopack 內部版本

Velopack 套件使用三段 SemVer：

YYYY.(M × 100 + D).R

範例：

| 對外 tag | Velopack 套件版號 | Assembly/File version |
|---|---:|---:|
| v2026.09.19.01 | 2026.919.1 | 2026.9.19.1 |
| v2026.09.19.02 | 2026.919.2 | 2026.9.19.2 |
| v2026.12.31.01 | 2026.1231.1 | 2026.12.31.1 |

更新排序以 Velopack 三段版號為準；視窗與版本資訊顯示完整對外 tag。版本值由 tag 注入，不再靠人工修改專案檔。

## 6. Phase B：GitHub Actions 發行流程

### 6.1 一般 CI

觸發條件為 push 與 pull request。Windows runner 執行：

1. checkout 原始碼與 submodules；
2. 設定 .NET 建置環境；
3. 呼叫 build.bat；
4. 執行既有測試與新增的發行相關測試；
5. 呼叫 run.bat --check；
6. 將 Release 輸出整理成 3waSshDrive-win-x64-<short-sha>.zip；
7. 驗證 ZIP 必要檔案並計算 SHA-256；
8. 上傳 GitHub Actions artifact。

CI ZIP 是免安裝測試包，不是正式安裝或自動更新的基礎。build.bat 與 run.bat 均保留；workflow 不另寫一套與本機不同的核心建置命令。

### 6.2 自我檢查模式

新增 3waSshDrive.exe --self-check，供本機、CI 與安裝後 smoke test 共用。

此模式：

- 不建立視窗；
- 不連 SSH；
- 不掛載磁碟；
- 不要求已安裝 WinFsp；
- 檢查必要 DLL、原生元件與靜態資源；
- 檢查對外版本、Assembly/File version 與 Velopack 套件版號的一致性；
- 驗證 Velopack bootstrap 能在檢查模式初始化；
- 成功回傳 exit code 0，任何檢查失敗回傳非 0，並輸出不含敏感資料的原因。

run.bat --check 必須執行真正的 --self-check，不得只確認 EXE 存在。

### 6.3 正式 tag 發行

tag vYYYY.MM.DD.RR 觸發 release job：

1. 驗證 tag 格式、日期、序號與三段版號映射；
2. 用 tag 注入對外版本、Velopack 套件版號與 Assembly/File version；
3. 以 x64 Release 執行 build.bat、完整測試與 --self-check；
4. 產生 portable ZIP；
5. 以 Velopack 產生 per-user Setup 與該版本完整的更新 feed 資產；
6. 先建立 draft GitHub Release；
7. 上傳 portable ZIP、3waSshDrive-Setup.exe、Velopack 完整／差分套件與更新索引；
8. 對最終資產建立 SHA256SUMS.txt；
9. 在乾淨 Windows runner 靜默安裝 Setup；
10. 執行已安裝程式的 --self-check，核對安裝路徑與版本；
11. 靜默解除安裝並確認完成；
12. 所有技術驗證成功後才將 draft 發布。

若任一步驟失敗，Release 維持 draft 或 workflow 失敗，不發布不完整資產。

### 6.4 安裝範圍

Setup 採 per-user 安裝，目標路徑為：

%LocalAppData%\Programs\3waSshDrive

應用程式本身的安裝與更新不要求系統管理員權限。WinFsp 是獨立的系統元件，其安裝可另外觸發 UAC。

### 6.5 發行安全

- 一般 CI 僅使用唯讀 repository 權限。
- 只有 tag release job 取得建立 Release 所需的 contents: write。
- SHA-256 以完成簽章與封裝後的最終資產計算。
- Authenticode 為選配；未配置憑證時走明確的 unsigned 發行路徑，不假裝已簽章。
- 若日後配置簽章，必須先簽章再計算 checksum 與執行安裝 smoke test。
- workflow、更新 log 與 self-check 不得輸出密碼、私鑰內容、私鑰路徑、主機敏感資料或完整連線設定。
- Microsoft 線上送檢不是自動或人工 release gate，也不由 workflow 呼叫。

## 7. WinFsp 邊界

Setup 不打包 WinFsp，沿用應用程式現有的前置檢查與輔助安裝流程。

為避免 winget 安裝到比應用程式允許清單更新、但尚未驗證的版本，輔助安裝命令固定指定 WinFsp 2.1.25156，並沿用已核准的版本與雜湊檢查。WinFsp 缺少或不符合要求時，應用程式提示使用者處理；此流程與應用程式自動更新彼此獨立。

## 8. Phase C：應用程式內更新

### 8.1 元件責任

- Program：在一般 UI 與單例鎖之前執行 Velopack bootstrap，解析 --self-check，再進入正常啟動。
- UpdateService：封裝 Velopack update source、版本比較、檢查、下載、驗證、套用與單一更新工作的同步控制。
- UpdateDialog：顯示目前版本、新版本、release notes、下載進度、錯誤，以及「更新」與「稍後」。
- MainForm／tray menu：啟動背景檢查與提供手動「檢查更新」入口。
- 既有 mount/session 元件：提供更新前停止重新連線、停止 heartbeat、停止背景 SFTP 工作與安全卸載的明確結果。

更新來源為本 repository 已發布的穩定 GitHub Releases。draft 或尚未發布的 Release 不可被客戶端採用。

### 8.2 運作流程

1. 程式啟動後顯示正常 UI，再以非阻塞方式檢查一次更新。
2. 使用者也可從主視窗或 tray menu 手動檢查。
3. 無新版時，手動檢查顯示已是最新；啟動背景檢查不打擾使用者。
4. 離線、GitHub 無法連線或 rate limit 時只記錄經過去敏的訊息，不影響掛載與檔案操作。
5. 發現新版時顯示目前版本、新版、release notes 與「更新／稍後」。
6. 選擇「稍後」立即結束本次更新工作，既有程式繼續運作。
7. 選擇「更新」後在背景下載並顯示進度；同一時間只允許一個檢查／下載／套用工作。
8. Velopack 驗證套件與 checksum 後，程式才進入套用階段。
9. 套用前先禁止新的 mount 與 reconnect，停止 heartbeat、背景 SFTP 與其他持有遠端或檔案系統資源的工作。
10. 對已掛載磁碟執行安全卸載。
11. 若任何磁碟無法卸載或背景工作無法在期限內停止，取消套用、恢復可操作狀態並保留舊版。
12. 安全靜止後關閉應用程式；程序結束釋放單例鎖。
13. Velopack 替換安裝內容並重新啟動新版本。
14. 新程序完成 bootstrap、取得單例鎖後正常顯示 UI。

更新不在使用者未確認時自動套用。

### 8.3 安裝版與 portable 的差異

只有由 Velopack Setup 安裝、且能辨識有效安裝狀態的程式啟用自動更新。從 CI artifact 解壓執行的 portable ZIP：

- --self-check 仍可使用；
- UI 不主動檢查更新；
- 手動檢查更新入口顯示此版本為 portable，需下載 Setup 才能啟用更新；
- 不嘗試原地替換解壓目錄。

## 9. 單例鎖與更新交接

現行 lock.pid 使用 FileShare.None 的持有中檔案鎖；因此單純留下舊檔不代表仍被鎖住。但鎖檔位於 repository 或安裝目錄會妨礙安裝器替換檔案，故固定搬到：

%LocalAppData%\3waSshDrive\state\lock.pid

啟動順序固定為：

1. Velopack bootstrap；
2. 若為 --self-check，執行檢查並結束，不取得一般模式單例鎖；
3. 一般模式取得 lock.pid 的 exclusive FileStream；
4. 啟動 WinForms。

lock.pid 可寫入 PID 供診斷，但是否已有執行個體只以能否取得 OS 檔案鎖判斷，不以檔案存在判斷。程序正常或異常結束後 handle 會由 OS 釋放，因此殘留檔案不得阻止下次啟動。

更新交接時，舊程序先完成卸載與關閉，退出後釋放鎖；Velopack 再替換安裝目錄並啟動新程序。新程序重新取得相同鎖，不需要刪除 lock.pid 才能前進。

## 10. 錯誤處理

| 情境 | 必要行為 |
|---|---|
| 沒有網路或 GitHub 錯誤 | 不影響正常使用；手動檢查才顯示簡短錯誤 |
| 沒有新版 | 背景檢查安靜結束；手動檢查顯示最新 |
| 下載中斷 | 不套用；允許之後重試 |
| checksum／套件驗證失敗 | 拒絕套用，保留目前版本並記錄去敏錯誤 |
| 已有更新工作 | 重用或拒絕重複工作，不並行下載或套用 |
| 卸載失敗 | 終止更新、恢復正常狀態、保留目前版本 |
| Setup smoke test 失敗 | Release 不發布 |
| 殘留 lock.pid | 仍可取得 OS lock 並啟動 |
| 另一正常 instance 持有 lock | 第二個一般 instance 結束，不破壞第一個 |
| portable 執行 | 不啟用原地更新 |

## 11. 驗證策略

### 11.1 每次 push／PR

- build.bat 成功；
- 現有 44 項測試全部通過；
- 新增的版本、self-check、更新協調與鎖定測試通過；
- run.bat --check 實際執行 EXE --self-check；
- ZIP 內容完整；
- SHA-256 成功產生。

### 11.2 tag release

- tag 與三種版本資訊映射正確；
- Velopack Setup 與 feed 資產完整；
- 乾淨 Windows runner 可靜默 per-user 安裝；
- 安裝路徑為 %LocalAppData%\Programs\3waSshDrive；
- 已安裝 EXE --self-check 回傳 0 且版本正確；
- 靜默解除安裝成功；
- 上述檢查完成前 Release 保持 draft。

### 11.3 Phase C 自動測試

至少涵蓋：

- 無新版；
- 發現新版；
- 下載／網路錯誤；
- checksum 或套件驗證失敗；
- 成功停止背景工作並卸載後允許更新；
- 卸載失敗時禁止更新；
- 同時觸發多次檢查時只有一個更新工作；
- portable 模式停用 updater；
- 殘留 lock.pid 不阻擋啟動；
- 一般 instance 已持鎖時第二個一般 instance 被拒絕；
- 一般 instance 持鎖時 --self-check 仍可執行；
- 更新重新啟動能取得鎖，沒有 deadlock。

## 12. Phase D：延後範圍

Phase D 只在可控制的本機 WinFsp、SFTP 與真實網路環境執行，不設 GitHub CI 效能 gate。

未來基準包含：

- 小檔：1 KB／10 KB 的 create、stat、read、overwrite、rename、delete；
- 大檔：100 MB／1 GB／5 GB 上傳與下載；
- 真實工作：git status、搜尋、build、Codex 掃描；
- 指標：吞吐量、p50／p95 latency、CPU、記憶體、錯誤率、重新連線行為。

先建立 baseline，再依證據檢查全域 SFTP lock、單一 connection、buffer 與 metadata round trip。每項優化都必須重跑正確性測試。Phase D 會另開設計與實作計畫，不阻擋 Phase B／C 完成。

## 13. 驗收標準

### Phase B

- 綠燈的 push／PR workflow 可下載可執行的 win-x64 ZIP artifact。
- 合法 tag 可產出並發布 ZIP、3waSshDrive-Setup.exe、Velopack feed 資產與 SHA256SUMS.txt。
- Setup 在乾淨 runner 完成 per-user 安裝、自我檢查與解除安裝。
- 沒有簽章憑證仍可走明確的 unsigned 發行流程。
- workflow 不包含 Microsoft Security Intelligence 上傳或等待步驟。
- WinFsp 不被包進 Setup，build.bat 與 run.bat 保留。

### Phase C

- Setup 安裝版能發現較新的已發布 Release，顯示版本與 release notes。
- 使用者按「更新」後可完成下載、驗證、安全卸載、安裝與自動重啟。
- 使用者按「稍後」時不改動目前安裝。
- 離線與檢查失敗不干擾既有掛載。
- 卸載失敗時不套用新版且舊版保持可用。
- Portable ZIP 不嘗試原地更新。
- stale lock.pid、self-check 與更新重啟均符合第 9 節鎖定規則。
- log 不洩漏密碼、私鑰資訊或主機敏感資料。

## 14. 實作交付順序

1. 完成 Phase B 的版本注入、self-check、CI artifact 與 tag release／Setup 流程。
2. 驗證 Phase B 能從 tag 穩定產出可安裝版本。
3. 完成 Phase C 的 bootstrap、UpdateService、UI、安裝模式判斷、安全卸載與更新重啟。
4. 完成 Phase C 測試矩陣與安裝版端到端驗證。
5. Phase D 保持延後，另案啟動。
