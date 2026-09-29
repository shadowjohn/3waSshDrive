# 本機 SSH Server 與 Windows 帳號／公鑰管理設計

- 日期：2026-09-29
- 狀態：需求方向已確認；本文件供設計 review，尚未開始實作或修改 Windows 設定。
- 已確認需求：本機同時提供 SFTP 傳檔與完整 SSH 終端機；使用 Windows 原生帳號與密碼；可啟動／停用服務；公鑰管理與來源 IP 白名單是第一版必要功能。
- 以下服務操作、權限邊界與驗收方式是依已確認方向整理的設計提案。

## 1. 目的與架構

3waSshDrive 新增「本機 SSH Server」管理頁面，由 Windows OpenSSH 的 sshd 服務處理登入、加密、SFTP 與終端機。既有 SSH.NET／WinFsp 遠端磁碟掛載流程保留；不要求升級 .NET Framework 4.7.2，也不自行實作 SSH 協定。

```text
3waSshDrive WinForms（一般使用者權限）
    +-- 既有遠端掛載功能
    +-- 本機 Server 狀態與設定頁
            +-- 權限允許的唯讀查詢：服務、帳號、診斷
            +-- 按需提權唯讀查詢：其他帳號的公鑰清單
            +-- 按需 UAC 管理操作：服務／帳號／公鑰／設定
                    +-- Windows SCM、帳號與 ACL API
                    +-- Windows OpenSSH sshd
                            +-- SFTP
                            +-- 以登入使用者身分執行 PowerShell
```

sshd 獨立於桌面程式運作。關閉、登出或更新 3waSshDrive 不主動停止 sshd；實際登出情境的連線持續性列入驗收。Server 管理本身不需要 WinFsp。

## 2. 第一版範圍

| 功能 | 預期行為 |
|---|---|
| 偵測環境 | 顯示 OpenSSH 安裝、服務執行狀態、啟動類型、Port 與生效中的設定 |
| 啟動／停止／停用服務 | 使用 Windows 服務管理功能；回讀狀態後才顯示成功 |
| 開機啟動 | 可設定手動或自動；與目前是否運行分開顯示 |
| 帳號管理 | 建立專用本機標準使用者、重設其 Windows 密碼、允許／禁止該帳號的 SSH 登入 |
| 公鑰管理 | 貼上或匯入 OpenSSH 公鑰；同帳號多把 key；查看指紋與逐把移除 |
| 驗證模式 | 每個受管帳號可選「Windows 密碼或公鑰」或「只允許公鑰」 |
| IP 白名單 | Server 共用的來源 IP 規則，同時限制 SSH／SFTP；支援單一 IPv4／IPv6 與 CIDR 網段 |
| 連線資訊 | 顯示 IP、Port、帳號與可複製的 SSH／SFTP 連線範例 |
| 診斷 | 顯示安全的失敗原因與 OpenSSH 診斷入口；不將服務運行誤判為登入成功 |

第一版先管理本機標準帳號。既有帳號須明確加入管理範圍，不能因為列出帳號就重設密碼或改寫公鑰。管理員、網域、Microsoft／Entra 帳號不在第一版寫入範圍。暫不提供刪除 Windows 帳號／個人檔案、強制踢除既有會話、網路磁碟再分享、對外 NAT 設定及 SSH 憑證管理。

## 3. 服務操作的精確語意

| 操作 | 必須確認的結果 |
|---|---|
| 啟動服務 | 若為 Disabled，將啟動類型改為 Manual，再啟動；若原為 Automatic 則保留。等待 Running 並核對監聽結果 |
| 停止服務 | 停止目前 listener，保留啟動類型；確認 Stopped 與無新的 listener，不宣稱已終止所有會話 |
| 停用服務 | 停止服務並設為 Disabled；兩項各自驗證，部分失敗要如實顯示 |
| 開機自動啟動 | 設定 Automatic；不自動啟動當下已停止的服務 |
| 關閉開機自動啟動 | 設定 Manual；不自動停止目前正在運行的服務 |

Windows OpenSSH 官方曾明列：停止 sshd 可保留已建立的 SSH 連線。因此「停止／停用」表示停止接收新連線；第一版不承諾強制終止既有 SSH／SFTP 會話。實際 Windows／OpenSSH 版本上的結果須驗收並記錄。[官方行為說明](https://github.com/PowerShell/Win32-OpenSSH/issues/1681)

操作期間禁止同時送出相反的服務命令。逾時、UAC 取消、無權限、服務消失或外部狀態變更皆重新查詢並顯示實際結果，不只依程序 exit code 或請求已送出判定完成。

## 4. Windows 帳號與密碼

- 帳號以 Windows SID 識別，顯示名稱只供 UI 使用；建立專用帳號預設為標準使用者，不加入 Administrators。
- 密碼由 Windows 驗證與保存；不是 Windows Hello PIN。建立／重設時只在必要操作期間使用，不寫入 profiles.json、設定、暫存檔、命令列、環境變數或 log。
- 密碼輸入與 Windows 帳號操作由短生命週期的提權管理介面處理，避免經一般文字 IPC 傳遞密碼。遵守 Windows 密碼原則，不以空密碼繞過。
- 「允許 SSH 登入」由專用本機群組與 sshd 存取規則管理。「禁止 SSH 登入」只撤除 SSH 登入資格，UI 不將它描述為停用整個 Windows 帳號。
- 重設密碼確實會改變該 Windows 帳號在其他使用情境的密碼，操作介面需明示帳號與影響。
- 「只允許公鑰」只限制 SSH 認證，Windows 帳號的密碼仍然存在。
- 已認證會話不因移除公鑰或撤除 SSH 登入資格而被保證即刻終止；撤銷驗證使用全新連線。

## 5. 公鑰管理

### 5.1 使用流程

選擇受管帳號 → 貼上一行 OpenSSH 公鑰或選擇 .pub → 驗證格式 → 顯示類型、SHA-256 指紋、備註 → 新增。

- 同帳號可保留桌機、筆電等多把 key；按實際 key blob 去重，備註不作為識別依據。
- 新匯入先支援普通 Ed25519、RSA 與 ECDSA 公鑰；須通過格式／長度與已安裝 OpenSSH 的支援檢查。RSA 公鑰不等於必須啟用 SHA-1 ssh-rsa 簽章。
- 新增介面不接受任意 authorized_keys options、SSH 憑證、DSA、PEM／OpenSSH 私鑰或 PuTTY PPK 私鑰。PuTTY 使用者可貼上其 OpenSSH 格式公鑰。
- Server 管理只接收公鑰；不讀取、匯入或複製用戶端私鑰，也不碰 Codex／瀏覽器登入資料。
- 移除操作列出所選指紋；「只允許公鑰」且只剩一把 key 時，要求先補上另一把、改回密碼登入或明確禁止該帳號 SSH，避免意外鎖定。
- 公鑰能解析、成功寫檔、ACL 正確與成功登入是不同驗證層次，UI 分開顯示；不要求把私鑰交給管理程式才能測試。

### 5.2 檔案位置與權限

遵循標準帳號實際 profile 下的 .ssh/authorized_keys，不直接猜測 C:/Users/帳號名稱。新帳號尚無 profile 時，由 Windows profile API 建立／取得路徑；失敗則維持未開放 SSH 狀態。[Windows CreateProfile](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createprofile)

標準帳號與管理員的預設 key 位置不同，管理員可能使用共用的 administrators_authorized_keys。第一版拒絕把管理員當成普通帳號編輯；每次寫入前重新檢查 SID、群組與實際生效的 AuthorizedKeysFile。[Microsoft 公鑰管理](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_keymanagement)

檔案 owner／ACL 使用 SID 處理，不能授予其他一般使用者寫入權。提權操作不得跟隨使用者可控制的 junction／symlink／reparse point 寫入其他位置；實作必須驗證最終目標並防止檢查後替換路徑的競態，無法保證時停止該操作。

既有 authorized_keys 的未知行、進階 options、編碼及換行均保留。只增刪明確識別的目標項目；若存在同 key 不同 options 的多筆項目，不合併或一併刪除。寫入前比對外部修改，使用受限權限的同目錄暫存檔及原子替換，替換後重新驗證 ACL。備份與暫存資料不能成為權限較寬的副本。

## 6. OpenSSH 設定與既有安裝

以 Windows 選用功能提供的 OpenSSH 為第一版基準，保留 Microsoft 維護與更新路徑。缺少 Server 時顯示 Windows 安裝引導；第一版不打包另一份 sshd，也不自動下載測試版本。[Microsoft 安裝說明](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse)

- 先確認服務實際執行檔、參數、版本及設定位置；不把 PATH 找到的任意 sshd 當成管理目標。
- 新初始化採專用群組 allowlist、拒絕管理員登入、SFTP 與 PowerShell、手動啟動；不默認開放所有 Windows 使用者。內建 Administrators 群組由 SID 取得本機名稱，再產生正確的 DenyGroups 規則，以涵蓋帳號日後被加入管理員群組的情境。
- 既有自訂 sshd 設定預設唯讀檢視。第一版只接管能辨識並驗證相容性的設定；無法解析的 Include、Match 或存取規則不以覆寫方式處理。
- 首次納管提供實際設定差異。只改受管範圍，保留未知內容、原始編碼、換行與既有 host keys；Host key 私鑰不進入 app 設定或 log。
- 寫入前備份並校驗外部修改；候選設定先以 sshd -t 檢查，再以 sshd -T -C 核對代表帳號的有效 allowlist、key 路徑與認證模式。
- 每帳號的「只允許公鑰」使用有效的 Match 規則限制為 publickey；至少有一把有效公鑰才可設定。套用前提示尚未實際登入測試的狀態。
- 設定需重啟才生效時，介面提供明確的「套用並重啟」操作，顯示可能影響連線；保存草稿不等於已生效。
- 啟動失敗須能恢復原設定，且不能意外啟動原本停止的服務；回復失敗明確顯示，不宣稱已回復。

## 7. IP 白名單、網路與終端機權限

### 7.1 使用行為

- 第一版採整個 Server 共用的遠端來源白名單，所有受管帳號、密碼登入、公鑰登入、SSH terminal 與 SFTP 都適用；不只限制管理介面。
- 可新增／移除多筆單一 IPv4、IPv6 或 CIDR，例如 192.168.1.20、192.168.1.0/24。輸入先驗證、正規化及去重；CIDR 有 host bits 時顯示並要求確認實際網段，不默默擴大範圍。
- 第一版不接受網域名稱、萬用字元、Any 或全網段 /0。來源 IP 使用 Server 實際看到的連線來源；有 NAT、VPN 或跳板時，可能不同於用戶端本機 IP，UI 說明此點。
- 初始狀態只綁定 loopback。沒有遠端白名單時維持／回復只允許本機測試，不能把空清單轉成允許所有來源；loopback 例外須明確顯示。
- 新增白名單不自動開放網路；「套用並開放」明確呈現 Port、介面 profile 與來源範圍。第一版提供 Private profile 的受限開放，不自動開啟 Public profile 或設定路由器轉發。
- 移除 IP 只承諾阻止套用生效後的新連線／登入；既有連線不宣稱已被強制中斷。UI 同時顯示草稿、已生效清單與驗證狀態。

### 7.2 執行與驗證

防火牆在受管 TCP Port／sshd 範圍使用明確的 RemoteAddress 白名單；OpenSSH 另外以 AllowUsers 的來源 IP／CIDR 條件配合受管 AllowGroups，限制准許登入的帳號來源。兩層使用同一份正規化清單，避免只靠新增一條窄防火牆規則就宣稱已完成限制。[Windows RemoteAddress](https://learn.microsoft.com/en-us/powershell/module/netsecurity/new-netfirewallrule)、[OpenSSH AllowUsers](https://man.openbsd.org/sshd_config#AllowUsers)

- 套用前核對防火牆啟用狀態、effective policy、profile、預設入站行為及重疊的允許／封鎖規則，包含舊的 OpenSSH 規則與群組原則。不能默默修改其他工具或管理者的規則；有無法安全處理的衝突時不開放新的範圍，顯示需處理的規則及目前仍生效的狀態。
- 不建立覆蓋白名單的「全部封鎖」規則：Windows 明確 Block 可壓過 Allow。應依既有有效政策採 default-deny 與窄 Allow，不能擅改整台電腦的預設入站政策。[Windows 防火牆優先順序](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/rules)
- OpenSSH 的既有 AllowUsers 條目可能累加；舊的無來源限制條目或 Match 規則不能繞過新白名單。若無法保留原設定並證明受管帳號都受來源限制，拒絕套用，不只在檔案尾端追加規則。
- IPv4 與 IPv6 一起驗證，不能只限制 IPv4 而留下 IPv6 入口。對 loopback、白名單內／外代表來源分別檢查候選 sshd 設定，並以全新實際連線驗證結果。
- 先驗證候選規則，採不擴大權限的套用順序，重啟後回讀兩層有效狀態；部分失敗時不顯示已生效。無法完成回復時停止新登入的 listener，清楚回報，不能留下已知較寬的放行狀態繼續聲稱安全。
- 「TCP 已擋住」、「SSH 認證拒絕」與「合法來源成功登入」分開記錄；若 firewall 被停用，OpenSSH 的登入限制不能被宣稱等同封包已被防火牆攔截。

### 7.3 終端機權限

完整終端機依登入使用者的 Windows／NTFS 權限執行。「起始資料夾」不構成沙箱；SFTP chroot 不能被宣稱也限制 PowerShell。公鑰登入亦不代表自動取得連往其他 Windows 機器／SMB 的可委派密碼憑證。[Windows Server 設定](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh-server-configuration)、[公鑰認證限制](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_keymanagement)

## 8. 程式整合與操作安全

- 新增 Server 管理邊界，與既有 DriveProfile、ProfileStore、SFTP mount 分開；Windows、sshd 與 authorized_keys 是實際狀態來源，app metadata 只保存受管 SID 與 UI 必要資料。
- 普通 UI 查詢和按需提權操作分開；不新增接受任意命令的常駐管理員服務。提權入口只接受固定操作、重新驗證目標，不能由呼叫端指定任意可執行檔或寫入路徑。
- 一般權限無法讀取其他帳號公鑰時，顯示需要管理員權限，使用固定用途的提權唯讀操作，只回傳類型、指紋、備註與必要診斷；不能將無權限讀取顯示成沒有公鑰，也不能為 UI 查詢放寬 authorized_keys ACL。
- 操作序列化，帳號建立／profile／ACL／SSH 登入資格分階段處理；未完整完成前不開放登入。失敗記錄已完成步驟，不自動刪除帳號或使用者資料。
- Server 寫入操作加入既有 app 更新協調：更新前不再接新寫入，等待已開始的操作完成；未完成則取消 app 更新。更新與 self-check 不啟停 sshd、不修改帳號與公鑰。
- log 可記錄操作、受管 SID／公鑰指紋、結果與安全的錯誤碼；不記錄密碼、私鑰、完整公鑰文字或未過濾的外部程序輸出。

## 9. 驗收與證據邊界

1. 建置與單元測試：服務狀態切換、設定差異／有效規則、公鑰驗證去重、保留未知行、外部修改偵測、更新互斥與失敗路徑。
2. 提權檔案操作測試：錯誤 SID、profile 缺失、ACL 不符、reparse point／路徑替換競態、原子寫入失敗時不改到錯誤檔案。
3. 隔離 Windows 測試環境：建立兩個標準帳號，完成 profile 與公鑰配置；分別驗證 Windows 密碼登入、publickey 登入、PowerShell 的 whoami 及 SFTP 上下載雜湊。
4. 公鑰案例：同帳號兩把 key 均可登入；移除其中一把後，使用被移除 key 建立全新連線須失敗，另一把仍成功。停用 agent 額外 key／密碼 fallback 與連線共用，避免假陽性。
5. 只允許公鑰案例：密碼認證失敗、公鑰成功；Windows 帳號密碼仍有效於其原本允許的本機用途。禁止 SSH 登入後，新的密碼與公鑰登入均失敗。
6. 服務案例：啟動、停止、停用、重開機與外部操作後的 UI 真實狀態；停用後新連線失敗，既有 SSH／SFTP 會話結果單獨記錄。
7. 網路案例：白名單內來源須能以正確帳號與密碼／公鑰使用 SSH 及 SFTP；名單外來源即使用相同有效憑證也不能登入。涵蓋 IPv4／IPv6、CIDR 邊界、空名單、刪除 IP、舊寬鬆 Allow 規則、GPO／firewall 關閉與部分套用失敗。至少用不同來源的實際區網連線驗證，不將 loopback 成功當成區網或 Internet 可用。
8. 回歸：既有遠端磁碟掛載、密碼不落盤、self-check、app 更新與獨立 sshd 存活；另外驗證現有 client 掛載 Windows SFTP 的路徑／metadata 相容性，未測前不宣稱雙向掛載完成。

目前只有程式碼與官方文件的可行性證據；以上測試尚未執行。實作驗收時逐項記錄已通過、失敗或未驗證的範圍，不以 API 回傳、設定寫入、服務 Running 或工作完成推論真人帳號已能登入。
