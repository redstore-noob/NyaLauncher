# 🐱 NyaLauncher

> エレガンスと自由のために作られた、モダンでクロスプラットフォームな軽量Minecraftランチャー。
<br>
![License](https://img.shields.io/badge/license-GPLv3-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10-purple.svg)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-green.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

---

## ✨ 概要

**NyaLauncher** は **Avalonia UI 12.0** と **.NET 10** を用いて構築されたクロスプラットフォームの Minecraft ランチャーです。  
軽量で高速なだけでなく、**プライバシー保護** と **プラグイン拡張性** に重点を置き、ゲームを楽しみながら完全なコントロールを提供します。

---

## 🎯 主な特徴

- 🚀 **クロスプラットフォーム対応** — Windows、macOS、Linux をサポートし、ほぼ同一の見た目を提供。  
- 🔌 **強力なプラグインシステム** — サードパーティプラグインの動的読み込みに対応し、機能を自由に拡張可能。  
- 🛡️ **プライバシーファースト** — テレメトリや第三者トラッキングなし。レジストリ等からのトークン漏洩を抑える独自のトークン暗号化技術を使用。  
- ⚡ **軽量かつ高効率** — .NET 10 のネイティブ AOT をベースに不要な機能を追加せずに必要に応じてカスタマイズ可能。  
- 🎨 **モダンなUI** — Avalonia ベースの滑らかなデザインとカスタマイズ対応。  
- ✊ **完全オリジナル** — 他のランチャーのコードを流用せず、模倣もしない方針。

---

## 実装済み機能と未実装機能
<table>
  <tr>
	<th>機能</th>
	<th>状態</th>
  </tr>
  <tr>
	<td>🚀 クロスプラットフォーム対応</td>
	<td><span>✅ 実装済み</span></td>
  </tr>
  <tr>
	<td>⚡️ スムーズなアニメーション</td>
	<td><span>✅ 実装済み</span></td>
  </tr>
  <tr>
	<td>🎮 オフラインアカウント起動</td>
	<td><span>✅ 実装済み</span></td>
  </tr>
  <tr>
	<td>🔌 プラグインシステム</td>
	<td><span>🚧 開発中</span></td>
  </tr>
  <tr>
	<td>🛡️ プライバシー保護</td>
	<td><span>🚧 開発中</span></td>
  </tr>
  <tr>
	<td>🎭 カスタムテーマ</td>
	<td><span>🚧 開発中</span></td>
  </tr>
  <tr>
	<td>🧩 Mod 管理</td>
	<td><span>🚧 開発中</span></td>
  </tr>
  <tr>
	<td>🎮 マルチプレイ......?</td>
	<td><span>🤔 将来的に検討、オプションとなる可能性あり</span></td>
  </tr>
</table>

---

## 🎮 オフライン起動

- Minecraft のルートディレクトリや `versions/<version>` の独立インスタンスディレクトリをスキャン可能。  
- オンラインのアカウントトークンに依存せず、オフラインユーザー名の検証と決定的な UUID 生成をサポート。  
- バージョン継承、モダンおよび旧式の起動引数、OS ルール、classpath の構築に対応。  
- ネイティブファイルを安全に展開し、ゲーム終了後に一時ファイルをクリーンアップ。  
- バージョン JSON の最低 Java 要件に基づき、Minecraft ランタイム、`NYALAUNCHER_JAVA`、`JAVA_HOME`、または `PATH` から要件に最も近い互換性のある JRE/JDK を選択。明示的な設定があればより高いメジャーバージョンの利用を許可。  
- classpath の結合は通常ライブラリ、`natives-*`、`unsafe` といった Maven classifier を区別し、同名の LWJGL 依存が上書きされるのを防止。  
- 起動ページにはディレクトリスキャン、バージョン選択、オフラインユーザー名、実行状態と終了コードの表示がある。

> オフラインアカウントは正規認証が必要なサーバーには参加できません。起動前に対象バージョン、依存ライブラリ、リソースファイルが揃っていることを確認してください。

---

## 📦 技術スタック

| コンポーネント    | 技術                          |
|------------------|-------------------------------|
| UI フレームワーク | Avalonia UI 12.0              |
| ランタイム        | .NET 10                       |

---

## 🔧 プロジェクト構成

| プロジェクト                         | 役割                                            |
|-------------------------------------|-------------------------------------------------|
| NyaLauncher.Core                    | NyaLauncher のコア起動機能                       |
| NyaLauncher.Avalonia                | Avalonia ベースのフロントエンド                   |
| NyaLauncher.Avalonia.Animations     | NyaLauncher 用の UI アニメーションライブラリ     |
| NyaLauncher.MinecraftTokenCrypto    | （一部の理由により公開されていません）Minecraft のアカウントトークン暗号化アルゴリズム |

---

## 🛠️ クイックスタート

### 必要環境

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 以上  
- Windows 10+, macOS Ventura+, Linux Kernel 5.0+  
- 各プラットフォーム向けデスクトップランタイム

### クローンとビルド

```
git clone https://github.com/your-username/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

> 注意: 本プロジェクトはまだ開発途上です。実行時やビルド時に問題が発生する可能性があります。

---

## 更新履歴

最近の変更
- バージョン継承時の classpath 依存インデックスの上書き問題を修正。  
- Java バージョン要件を最小バージョン制約に変更し、より高いメジャーバージョンでの起動をサポート。Minecraft 1.21.4 は Java 21 および Java 25 での動作を確認済み。

v0.1.0pre1
- GUI を独立ライブラリ（NyaLauncher.Avalonia.Animations）へ分割。  
- 発生していた一部のカクつきを改善。  
![v0.1.0pre1 main screen](img/v0.1.0pre1.png)
