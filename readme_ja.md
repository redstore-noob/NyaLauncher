# 🐱 NyaLauncher

> 自由のために生まれた、モダンでクロスプラットフォームな軽量 Minecraft ランチャー。
<br>
![.NET](img/badges/dotnet.svg)
![Avalonia](img/badges/avalonia.svg)
![Platform](img/badges/platform.svg)
![License](img/badges/license.svg)

---

## ✨ 概要

**NyaLauncher** は **Avalonia UI 12.1.1** と **.NET 10** を用いて構築されたクロスプラットフォームの Minecraft ランチャーです。<br>
軽量で高速なだけでなく、**プライバシー保護** と **画面のカスタマイズ性** を重視し、ゲームを楽しみながら完全なコントロールを提供します。<br>
NyaLauncher は自由ソフトウェアです。必要に応じて保持するライブラリファイルを除き、すべてのコードは [Apache License 2.0](LICENSE) に従います。<br>
ランチャーはユーザーの知らないテレメトリを一切行わず、ユーザーのプライバシーを侵害せず、機能を制限することもありません。

---

## 📦 技術スタック

| コンポーネント            | 技術                          |
|---------------------------|-------------------------------|
| UI フレームワーク         | Avalonia UI 12.1.1            |
| ランタイム                | .NET 10                       |
| コンポーネント拡張契約    | .NET 10、Avalonia 依存なし    |

---

## 🔧 プロジェクト構成

| プロジェクト                        | 役割                                                                       |
|-------------------------------------|----------------------------------------------------------------------------|
| NyaLauncher.Core                    | 🐱 NyaLauncher のコア起動機能セット                                         |
| NyaLauncher.Avalonia                | Avalonia 技術で構築された NyaLauncher のフロントエンド UI                   |
| NyaLauncher.Avalonia.Animations     | NyaLauncher のフロントエンド UI 用アニメーションライブラリ                 |
| NyaLauncher.Plugin.Abstractions     | UI フレームワーク非依存のコンポーネント契約、形状、要素、ランタイム状態、検証 |
| NyaLauncher.MinecraftTokenCrypto    | （**アルゴリズムを公開するのが適切ではないため、クローズドソースです**）Minecraft 正規アカウントのログイントークンの暗号化アルゴリズム／保存 |

---

## 🔃 更新計画

### 📝 バージョン命名規則
| 段階          | 説明                                                                                    |
|---------------|-----------------------------------------------------------------------------------------|
| beta          | ランチャー作成段階、完全に使用不可                                                      |
| preview       | テスト段階、部分的に使用可能だが日常利用は非推奨（現在 0.1.0preview-3 の段階）           |
| release       | 正式版、完全に使用可能な状態                                                            |
| gp（特殊）    | newgui ブランチでの特定バージョン番号、main ブランチの preview に対応                    |

### 実装予定の機能
- プラグイン機能（下流ブランチ testplug で検証済み）
- カスタムテーマ（次の preview 版で公開予定）
- 多言語サポート（実装時期未定）
- AI 支援による翻訳・誤りチェック（未定）
- オンラインマルチプレイ（???）

---

## 🛠️ クイックスタート

### 🪟🍎🐧 必要環境

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 以上
- Windows 10+、macOS Ventura+、Linux Kernel 5.0+
- デスクトップランタイム（Windows/macOS/Linux）
> HarmonyOS への移植は未定です。

### 🔧 クローンとビルド

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

---

## 📈 更新ログ
最近の変更

v0.1.0-preview3
- newgui 機能を完成させ、main ブランチにマージしました
- 多数のコンポーネントを追加
- Core モジュールを小規模にリファクタリング（進捗: 25/100%）
- Minecraft 関連のダウンロード機能を追加
- ランチャー／ゲーム実行時に生成されるランタイムファイルを保存する Log 機能を追加
- フロントエンドのハードコードされたスタイルの問題を修正し、その他の問題を少し最適化
- プラグインシステムはテスト中で、最初の利用可能な API がまもなく公開されます
- バックエンドの重複コード／デッドコードを削除
- animations モジュールの一部のコードを削除し、まもなくリファクタリング予定
- 各更新バージョンの命名規則を変更
- Herobrine を削除しました
![v0.1.0-preview3 メイン画面のスクリーンショット](img/v0.1.0preview-3-mainwindow.png)
![v0.1.0-preview3 ゲーム管理画面のスクリーンショット](img/v0.1.0preview-3-game.png)
![v0.1.0-preview3 アカウント管理画面のスクリーンショット](img/v0.1.0preview-3-account.png)

v0.1.0-gp2（newgui ブランチ）

> `v0.1.0-gp2` は v0.1.0 newgui の 2 回目の UI イテレーションを示すだけで、Core のバージョンには書き込まれません。<br>このバージョンは main ブランチとは関係ありません。

- GUI を再構築（newgui ブランチ上）。ホームページが変更可能なコンポーネントブロックになり、カスタマイズの自由度が向上（まだ不完全。旧 GUI は main ブランチに保存）
- オフライン起動と正規（オンライン）起動機能を追加
- readme.md のバグを修正（?）
- マルチアカウント管理を追加
- 設定保存機能を追加。一度設定すればようやく保持されるようになりました 😭
- Java 検索を最適化し、以前の「Java は起動できるが使用できない」問題を修正
- Herobrine を削除しました
![v0.1.0-gp2 メイン画面のスクリーンショット](img/v0.1.0pre2-mainwindow.png)
![v0.1.0-gp2 起動画面のスクリーンショット](img/v0.1.0pre2.png)
![v0.1.0-gp2 設定のスクリーンショット](img/v0.1.0pre2-settings.png)
![v0.1.0-gp2 カスタマイズ画面のスクリーンショット](img/v0.1.0pre2-settings2.png)

v0.1.0-pre1
- ユーザーインターフェース内の GUI を独立ライブラリ（NyaLauncher.Avalonia.Animations）に分割
- 発生していた一部のカクつき問題を改善
- Herobrine を削除しました
![v0.1.0-pre1 メイン画面のスクリーンショット](img/v0.1.0pre1.png)