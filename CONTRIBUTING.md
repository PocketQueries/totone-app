# Contributing to Totonoe (Online Meeting Recorder)

このプロジェクトへの貢献に興味を持っていただきありがとうございます。

## コントリビューションポリシー

このリポジトリは **Pocket Queries Inc. の社内メンバーのみ** がコミット・プルリクエストを行う運用としています。

### 社外の方へ

- **Issue での提案・バグ報告は歓迎です。** [Issues](../../issues) からお気軽にどうぞ。
- **プルリクエストは受け付けておりません。** 提案いただいた内容は社内メンバーが検討・実装いたします。
- ご提案が採用された場合は Issue 上でクレジットいたします。

### 社内メンバーへ

1. `main` ブランチへの直接 push は避け、feature ブランチから PR を作成してください
2. コミットメッセージは日本語で記述してください（例: `feat: 新機能の追加`, `fix: バグ修正`）
3. PR には変更内容の説明を記載してください

## 開発環境

- .NET 8.0 SDK
- Windows 10 以降
- Visual Studio 2022 または VS Code

```bash
# ビルド
dotnet build src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj

# 実行
dotnet run --project src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj
```

## ライセンス

[Apache License 2.0](LICENSE) の下でライセンスされています。
