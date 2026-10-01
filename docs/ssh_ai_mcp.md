# AI 透過既有 SSH 連線設定執行命令：可行性與最小設計

- 日期：2026-10-01
- 狀態：架構評估，尚未實作或啟用 AI 命令入口。
- 目標：保留目前滿意的 SFTP 掛載體驗，讓 AI 經使用者明確授權後，以相同遠端連線設定執行測試、查看錯誤及協助 debug。
- 本輪交付：文件。未安裝元件、啟動服務、執行遠端 SSH、讀取實際認證內容或建立持久存取權。
- 原始碼基準：主專案 `76a3051acb262d12b0db825702ac72a34e6b6832`；SSH.NET 以主專案記錄的 `f099365c9d4cf2ade92b92c203bbb2b345d2cd74` 為重現基準。檢視時本機 submodule 位於不同 commit，已核對本文涉及的命令 API、取消與輸出緩衝檔案在兩者相同；既存 submodule 變動不納入本次文件提交。

## 1. 結論與代價

**可行，不必重寫掛載功能，也不必先建立本機 SSH Server。** 專案已包含 SSH.NET，底層已有 SSH 命令、stdout／stderr、exit status、timeout 與取消 API。

最小方案是沿用 Profile、認證來源及固定主機指紋，另外建立命令專用 `SshClient`。AI 的工作流程可以是：透過掛載讀寫程式 → 提交遠端測試或診斷命令 → 使用者預覽核准 → 取得執行結果 → 繼續修正。

SSH 執行核心的新增量較小；完整可用的擴充屬中等規模，主要工作在本機入口、每連線授權、預覽確認、輸出限制，以及退出／更新／取消的協調。每項工作另建 SSH 連線會增加登入延遲與伺服器連線數，但能把命令的斷線及清理與掛載 client 分開。

此隔離不保證遠端負載互不影響：測試或 build 仍會與 SFTP 共用伺服器 CPU、磁碟及網路。SFTP 登入成功也不代表伺服器允許 SSH exec；目標主機的 shell、forced-command 或僅 SFTP 政策需另行授權驗證。

## 2. 現有程式證據

| 能力／邊界 | 原始碼證據 | 對設計的含意 |
|---|---|---|
| .NET Framework 4.7.2 WinForms | [3waSshDrive.App.csproj](../src/3waSshDrive.App/3waSshDrive.App.csproj#L3) | 可新增小型 Console CLI；GUI 繼續持有連線及互動確認。 |
| 掛載使用 SSH.NET SFTP | [SshNetRemoteFileSystem.Connect](../src/3waSshDrive.Sftp/SshNetRemoteFileSystem.cs#L45) | 每個 remote instance 自行建立及管理 `SftpClient`，目前沒有命令 executor。 |
| 認證建立 | [CreateAuthenticationMethod](../src/3waSshDrive.Sftp/SshNetRemoteFileSystem.cs#L280) | 已支援記憶體密碼及私鑰檔，可抽出小型 factory，為不同 client 建立各自的物件。 |
| 生命週期與序列化 | [Execute](../src/3waSshDrive.Sftp/SshNetRemoteFileSystem.cs#L244)、[CleanupClient](../src/3waSshDrive.Sftp/SshNetRemoteFileSystem.cs#L375) | 檔案操作走 `SyncRoot`，連線錯誤會重試一次；命令不得共用這個鎖與重試流程。 |
| 掛載持有及釋放 | [MountManager.Mount](../src/3waSshDrive.FileSystem/Mounting/MountManager.cs#L35)、[MountedDrive.Dispose](../src/3waSshDrive.FileSystem/Mounting/MountedDrive.cs#L39) | 掛載擁有自己的 host／remote；命令不能借用其 Dispose 或斷線作為取消手段。 |
| 背景重連 | [MainForm](../src/3waSshDrive.App/MainForm.cs#L916) | 每 10 秒檢查掛載連線；命令應有獨立狀態，不能把掛載重連視為命令續跑。 |
| 密碼只在記憶體 | [DriveProfile.Password](../src/3waSshDrive.Core/Models/DriveProfile.cs#L42) | `[IgnoreDataMember]` 不寫入設定檔；另開 CLI 讀取設定檔不能取得 GUI 當次輸入的密碼。 |
| 主機指紋固定比對 | [HostKeyPolicy.ForMount](../src/3waSshDrive.Sftp/Security/HostKeyPolicy.cs#L18) | 命令連線應沿用嚴格比對，不使用 `ForCapture` 自動接受新指紋。 |
| 命令列入口尚未存在 | [StartupOptions.Parse](../src/3waSshDrive.App/StartupOptions.cs#L15) | 目前只有 `--self-check`，沒有產品級 SSH exec、MCP 或 named pipe 入口。 |
| 更新已有活動協調 | [UpdateActivityGate](../src/3waSshDrive.App/Updates/UpdateActivityGate.cs#L23)、[PrepareAsync](../src/3waSshDrive.App/MainForm.Update.cs#L200) | 命令工作可登記 activity lease，配合更新時拒收新工作及等待 idle。 |

`DriveProfile` 目前沒有 AI 執行授權欄位，也沒有不可變的 Profile ID。新增入口時需要明確的授權狀態與連線識別，不能只根據可編輯的名稱判斷核准對象。首版可使用 GUI 工作階段內產生的連線 ID，不必先做持久化授權資料庫。

既有 [本機 SSH Server 設計](superpowers/specs/2026-09-29-local-ssh-server-design.md) 記錄的是讓外部連進 Windows 的 sshd PoC，以及後續常駐掛載候選方案。該文件明確說明尚未整合產品，不能視為本次 AI 遠端命令入口已存在。

## 3. 復用設定與認證，不共用掛載 transport

SSH 協定有多 channel 的能力，但目前 vendor 的公開 API 沒有提供把掛載中 `SftpClient` 的 session 借給 `SshClient` 的方式：

- [BaseClient.cs:38](https://github.com/sshnet/SSH.NET/blob/f099365c9d4cf2ade92b92c203bbb2b345d2cd74/src/Renci.SshNet/BaseClient.cs#L38)：`Session` 為 `internal` 且 private setter。
- [SshCommand.cs:215](https://github.com/sshnet/SSH.NET/blob/f099365c9d4cf2ade92b92c203bbb2b345d2cd74/src/Renci.SshNet/SshCommand.cs#L215)：接受 session 的建構子為 `internal`。
- [SshClient.cs:190](https://github.com/sshnet/SSH.NET/blob/f099365c9d4cf2ade92b92c203bbb2b345d2cd74/src/Renci.SshNet/SshClient.cs#L190)：公開入口為 `CreateCommand`，使用該 `SshClient` 自己的 session 建立命令。
- [ConnectionInfo.cs:29](https://github.com/sshnet/SSH.NET/blob/f099365c9d4cf2ade92b92c203bbb2b345d2cd74/src/Renci.SshNet/ConnectionInfo.cs#L29)：明確註記不得讓多個 client 共用同一個 `ConnectionInfo` instance；authentication method 亦包含連線中的狀態。

第一版從同一份已核准的 Profile 快照，各自建立 `ConnectionInfo`、authentication method 及私鑰物件，套用相同的 host-key policy。密碼及私鑰處理仍留在 GUI 程序內；CLI／MCP 不取得或轉送認證內容。不同 client 的 key 物件也有各自的釋放生命週期。

每項命令工作使用獨立 `SshClient` 及 exec channel，完成後釋放。掛載繼續使用原有 `SftpClient`，不修改 SSH.NET、不以反射取得 session，也不把命令塞進 `IRemoteFileSystem`。

## 4. 最小入口與資料流

```text
可執行本機工具的 AI
    → 3waSshDrive.Cli（Console 程序）
    → 僅限本機、受 Windows ACL 保護的 named pipe
    → 既有 GUI 的 broker：連線授權、預覽、核准、工作追蹤
    → 獨立 SshClient／exec channel
    → 已核准的遠端 SSH 帳號

後續可加入：AI MCP client → stdio MCP adapter → 同一個 broker
```

CLI 僅能列出已授權連線的必要資訊、提交命令及取消自己的工作；不接受任意 host、密碼、私鑰路徑或提權旗標。GUI 未執行或未授權時直接回報不可用，不自行建立背景服務。

請求以 stdin JSON 傳送 `connectionId`、`requestId`、`cwd`、`command`、`timeoutSeconds`；回應可使用 NDJSON 的開始、輸出片段及結束事件。這些是擬議契約，尚非既有 CLI 指令。避免把完整命令或秘密塞進本機程序參數，減少 PowerShell／cmd 與遠端 shell 的多層 quoting 問題。

| 入口 | 建議與代價 |
|---|---|
| CLI＋named pipe | 首版推薦。適合已有本機工具執行能力的 AI；沿用 GUI 認證與確認流程。需補 IPC framing、ACL、取消及程序生命週期。 |
| stdio MCP adapter＋同一 broker | 第二步。增加工具 schema、發現與 MCP client 整合，沿用同一套授權及 executor；不複製認證邏輯。 |
| localhost HTTP MCP | 首版不需要。會新增瀏覽器可觸及的 listener，以及 Origin 驗證、認證與 DNS rebinding 防護責任。 |

[MCP stdio](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio) 使用 client 啟動的子程序與標準輸入輸出，不需要 HTTP port；stdout 必須保留給 MCP 訊息。MCP 與 named pipe 分屬 AI 工具協定及本機 IPC，並非互斥選項。實際使用仍需 AI client 支援並設定本機工具或 MCP；一般網頁聊天不會自動取得此能力。

## 5. 命令工作的必要語意

### 輸出、結束碼與記憶體

vendor 的 `SshCommand` 已有 `ExecuteAsync(CancellationToken)`、`OutputStream`、`ExtendedOutputStream`、`CommandTimeout`、`int? ExitStatus` 及 `ExitSignal`。產品需包裝成明確結果：`jobId`、狀態、nullable `exitCode`、signal、耗時、timeout／cancel、輸出是否截斷，以及遠端終止是否已確認。

stdout 與 stderr 同時持續讀取，片段附 stream 名稱及接收序號；不宣稱兩個 stream 有精確的遠端全域順序。設定每項工作的輸出大小上限，超量後截斷但繼續排空，或按已定義政策取消。vendor `PipeStream`／`ArrayBuffer` 會擴充記憶體，不能等命令完成才讀，也不能達上限後直接停止消費。

串流後自行保存有限大小的結果；`Result`／`Error` 會消費同一個 stream，不能當作完整輸出的第二份重播。未收到 exit status 時維持未知，不把空值轉成成功的 0。

### cwd 與 quoting

首版明確限定 Linux／POSIX shell。每項請求攜帶遠端 cwd，預設 `Profile.RemoteRoot`；每次命令重新設定 cwd，不保留前一次的 `cd` 或環境變數。非互動 shell 的 PATH／初始化也不能假定等同使用者手動登入的終端機。

SSH exec 的命令本質是字串，沒有獨立的 cwd／argv 協定。由 executor 統一處理 POSIX quoting，將 cwd 與可結構化的參數當成資料；需要管線或多行 script 時，預覽真正送出的完整內容。不可套用 Windows 的 escaping 規則。

`RemoteRoot` 只是起始位置；禁止 `..` 也不能阻止任意 shell 讀寫其他路徑。掛載的 `ReadOnly` 同樣不限制 SSH 命令。若未來要求嚴格隔離，需遠端帳號權限、受限執行器或容器等真正邊界，不能靠命令名稱黑名單宣稱已完成。

### timeout、取消與重試

- 分別設定連線／啟動期限、命令期限及整項工作期限。`CommandTimeout` 預設無限，且 vendor 在 channel open／exec request 之後才登記命令取消與計時；只設定這一項不足以涵蓋啟動卡住。
- CLI Ctrl+C 或 MCP 取消映射到該項工作；呼叫端消失時首版取消工作，不默默轉成持久背景任務。取消處理在背景執行，避免阻塞 GUI。
- `CancelAsync` 發送 TERM／KILL，伺服器未回應時預設等待 500 ms 仍可完成本機 task。它不保證遠端 process group／所有子孫程序均已停止。
- `finally` 釋放命令及其專用 client；仍須區分「本機取消完成」與「遠端終止未確認」，不能把關閉 channel 回報為遠端已清理乾淨。
- 斷線後可能無法知道命令執行到哪裡。不得自動重送，也不得沿用 SFTP 的重試一次流程；不確定結果要回報，由使用者或後續明確授權的檢查判斷。
- broker 以 request ID 去除本工作階段內的重複提交；重啟後不宣稱具備 exactly-once 或可續跑能力。

### 長任務、並行及掛載一致性

第一版每個 Profile 同時一項工作，另設整體工作數上限；忙碌時清楚回報，不建立無限佇列。長測試可保持 CLI 連線並串流，期限在核准時可見；不支援互動 PTY、密碼提示或離線續跑。

掛載目前有 [2 秒 metadata／10 秒目錄快取](../src/3waSshDrive.FileSystem/SftpReadOnlyFileSystem.cs#L31)。SSH 命令在遠端改檔會繞過掛載的 `InvalidateCache`，AI 或 Explorer 可能暫時看到舊內容；初版應說明此限制，後續可新增針對工作目錄的快取失效介面，不必為此重寫檔案系統。

## 6. 授權、安全與生命週期

**掛載成功不等於命令已授權。** 每個 Profile 的 AI exec 預設關閉；首版 opt-in 僅存於 GUI 當次工作階段，可從系統匣撤銷。第一版限一般遠端帳號，不自動 sudo／su；已有 root Profile 也不自動開放 AI。帳號名稱或 shell 黑名單不是權限隔離的保證。

每次預覽顯示連線目標、遠端帳號、指紋、cwd、完整命令及期限。核准必須綁定不可變的請求與 Profile 快照，執行前重新確認授權仍有效；不能核准後改掉主機或命令。命令文字、遠端輸出及 AI 自稱的授權均不能代替本機使用者確認。

named pipe 使用明確的目前登入工作階段 ACL，核驗實際 Windows 身分並拒絕遠端連入；管線名稱不是認證。不可依賴預設 ACL；[Microsoft 的 named pipe 安全說明](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights) 特別說明預設權限及 logon SID 的用途。同帳號其他程序仍可能具備相同存取權，因此 ACL 不能取代 GUI 授權。主程式及 broker 保持一般權限，不新增管理員／SYSTEM 萬用 shell。

host、port、username、fingerprint 或認證變更即撤銷舊授權；首版卸載也同步撤銷並取消該 Profile 的命令。按 X 目前只是縮到系統匣，不代表撤銷；真正退出、更新或撤銷時，先拒收新工作、取消待確認請求，再限時等待或取消執行中的工作。AI 工作登記到 `UpdateActivityGate`，未能完成本機清理時阻擋更新，避免更新直接切斷工作。Windows 登出／GUI 重啟後不自動恢復授權或命令。

日誌只保存必要審計：request／job ID、受控連線代號、核准結果、開始／結束時間、exit／timeout／cancel 及輸出大小。原始 command、stdout、stderr 預設不落盤；回傳 AI 的內容也需限量及去敏，不將任意環境變數或秘密檔案傾印納入預設診斷。去敏只能降低外洩風險，不能保證辨認所有秘密。

可沿用 [App 安全日誌](../src/3waSshDrive.App/Diagnostics/CrashLogger.cs#L13) 只記 operation／exception type／HResult 的風格。[Core logger](../src/3waSshDrive.Core/Logging/CrashLogger.cs#L53) 會記完整例外；SSH.NET 的命令 timeout 例外可含完整命令文字，不能直接把原始例外交給該 logger。

## 7. 實作切分與後續驗證

第一版只新增四個責任：認證 factory、獨立 command executor、GUI broker／核准視窗、Console CLI。保持既有 `IRemoteFileSystem`、掛載 client 及 SSH.NET API 邊界，避免先重構整個應用程式。

後續才考慮 stdio MCP、受控的常用診斷模板、`jobId` 查詢／取消與有限並行。持久 job、跨登出運作、PTY／互動 debugger、process-group 終止保證及同 transport multiplexing 都需要另外設計，不屬第一版承諾。

實作後應在另行授權的測試環境驗證：

1. 未 opt-in、核准逾期、Profile 變更或卸載後一律拒絕；錯誤指紋不執行命令。
2. stdout／stderr 同時大量輸出、非零 exit、無 exit status、UTF-8 與輸出截斷的結果正確。
3. 含空白、單引號及 shell 特殊字元的 cwd／參數不改變預期語意，預覽與實際命令一致。
4. timeout、取消、呼叫端退出、斷網、伺服器拒絕 exec 等情況不重送命令、不關閉掛載 client，且如實回報遠端終止未知。
5. 命令與 SFTP 同時運作、GUI 更新／退出、重複 request、未授權 Windows 身分及日誌去敏符合上述邊界。

以上是驗收清單，尚未執行；本輪沒有連線遠端或跑會啟動產品的測試。唯讀檢查未在相關專案路徑發現 `AGENTS.md` 或 `.agents/skills`。
