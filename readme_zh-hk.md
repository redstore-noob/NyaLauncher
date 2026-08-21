# 🐱 NyaLauncher

> 一個現代、跨平台的輕量 Minecraft 啟動器，為自由而生。
<br>
![.NET](img/badges/dotnet.svg)
![Avalonia](img/badges/avalonia.svg)
![Platform](img/badges/platform.svg)
![License](img/badges/license.svg)

---

## ✨ 簡介

**NyaLauncher** 是一款基於 **Avalonia UI 12.1.1** 與 **.NET 10** 構建的跨平台 Minecraft 啟動器。<br>
它不但輕量、快速，更注重 **私隱保護** 與 **介面自訂**，讓你在享受遊戲的同時，擁有完全自主的控制權。<br>
NyaLauncher 是一款自由軟件，除了必要時保留的庫檔案之外，所有程式碼均遵循 [Apache License 2.0](LICENSE)。<br>
啟動器不會在用戶不知情的情況下進行任何遙測，不會侵犯用戶的任何私隱，亦不會對用戶作出任何功能限制。

---

## 📦 技術選項

| 元件                    | 技術                          |
|-------------------------|-------------------------------|
| UI 框架                 | Avalonia UI 12.1.1            |
| 執行環境                | .NET 10                       |
| 元件擴充契約            | .NET 10，不依賴 Avalonia      |

---

## 🔧 項目結構

| 項目                                | 相關功能                                                                       |
|-------------------------------------|--------------------------------------------------------------------------------|
| NyaLauncher.Core                    | 🐱 NyaLauncher 核心的啟動功能集合                                              |
| NyaLauncher.Avalonia                | NyaLauncher 的前端介面，基於 Avalonia 技術構建                                 |
| NyaLauncher.Avalonia.Animations     | NyaLauncher 的前端介面動畫庫，為 NyaLauncher 而準備                           |
| NyaLauncher.Plugin.Abstractions     | 與 UI 框架無關的元件契約、幾何、元素、執行時狀態與驗證                         |
| NyaLauncher.MinecraftTokenCrypto    | （**由於演算法不宜公開，此庫為閉源庫**）關於 Minecraft 正版帳戶登入權杖的加密演算法／儲存 |

---

## 🔃 更新計劃

### 📝 更新命名規則
| 版本階段       | 相關代表                                                                                     |
|----------------|----------------------------------------------------------------------------------------------|
| beta           | 啟動器編寫階段，完全不可用                                                                   |
| preview        | 啟動器測試階段，已經部分可用但不建議用於日常（現時 0.1.0preview-3 所處階段）                 |
| release        | 啟動器正式版本，完全可用狀態                                                                 |
| gp（特殊）     | newgui 分支時的特定版本號，對應為主要分支的 preview                                          |

### 待實現功能
- 插件功能（已成功在下游分支 testplug 驗證完畢）
- 自訂主題（預計將在下一個 preview 版本發布）
- 多語言（實現時間待定）
- AI 輔助翻譯／查錯（未知）
- 聯機（???）

---

## 🛠️ 快速開始

### 🪟🍎🐧 環境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
- 系統運行於 Windows 10+、macOS Ventura+、Linux Kernel 5.0+
- 桌面執行環境（Windows/macOS/Linux）
> 鴻蒙移植計劃待定。

### 🔧 複製與建置

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

---

## 📈 更新日誌
近期改動

v0.1.0-preview3
- newgui 功能完善，已合併回 main 分支
- 增加了大量元件
- 對 Core 模組進行了小規模重構（進度：25/100%）
- 增加了 Minecraft 相關的下載功能
- 增加了 Log 功能，用於保存啟動器／遊戲運行時產生的運行檔案
- 修復了前端硬編碼樣式問題，並對其他問題進行了小幅優化
- 插件系統正在測試中，即將推出第一個可用 API
- 已移除後端重複的部分程式碼／死程式碼
- animations 模組移除部分程式碼，即將進行重構
- 更改各更新版本的命名規則
- 移除了 Herobrine
![v0.1.0-preview3 主介面截圖](img/v0.1.0preview-3-mainwindow.png)
![v0.1.0-preview3 遊戲管理介面截圖](img/v0.1.0preview-3-game.png)
![v0.1.0-preview3 帳戶管理介面截圖](img/v0.1.0preview-3-account.png)

v0.1.0-gp2（newgui 分支）

> `v0.1.0-gp2` 僅表示 v0.1.0 newgui 的第二次介面演進，不會寫入 Core 版本號。<br>該版本與 main 分支無關。

- 對 GUI 進行了重構（位於 newgui 分支），主頁變成可更改的元件塊，增加了自訂自由度（尚不完善，舊版 GUI 介面保存在 main 分支）
- 增加了離線啟動、正版啟動功能
- 修復了 readme.md 的錯誤（?）
- 增加了多帳戶管理
- 增加配置保存功能，配置一次後終於能保留下來了 😭
- 對 Java 搜尋進行優化，修復了曾經存在的「Java 可啟動但無法使用」問題
- 移除了 Herobrine
![v0.1.0-gp2 主介面截圖](img/v0.1.0pre2-mainwindow.png)
![v0.1.0-gp2 啟動介面截圖](img/v0.1.0pre2.png)
![v0.1.0-gp2 設定截圖](img/v0.1.0pre2-settings.png)
![v0.1.0-gp2 個人化介面截圖](img/v0.1.0pre2-settings2.png)

v0.1.0-pre1
- 將用戶介面中的 GUI 拆分為獨立庫（NyaLauncher.Avalonia.Animations）
- 改善了出現的部分畫面抽搐現象
- 移除了 Herobrine
![v0.1.0-pre1 主介面截圖](img/v0.1.0pre1.png)