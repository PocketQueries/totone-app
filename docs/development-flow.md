# Issue 駆動開発フロー

GitHub Issue を起点とした開発フロー。実装は Claude Code で行う。

## フロー全体像

```
1. Issue 作成（GitHub上で起票）
2. Issue のステータスを更新（作業開始を明示）
3. 最新の main からブランチを作成（命名規則に従う）
4. Claude Code で実装（進捗を Issue コメントに記録）
5. PR 作成（Issue を自動クローズ）
6. レビュー → マージ
```

## ブランチ命名規則

Issue 番号とカテゴリを含めた命名を使う。

| カテゴリ | パターン | 例 |
|----------|----------|----|
| 新機能 | `feature/#123-説明` | `feature/#42-add-export-csv` |
| バグ修正 | `fix/#123-説明` | `fix/#15-fix-wav-header-crash` |
| リファクタリング | `refactor/#123-説明` | `refactor/#30-extract-audio-service` |
| ドキュメント | `docs/#123-説明` | `docs/#8-update-setup-guide` |

- 説明部分は英語のケバブケース（小文字・ハイフン区切り）
- 簡潔に内容がわかる程度にする（3〜5単語）
- **必ず最新の main ブランチから分岐する**（`git checkout main && git pull origin main` してからブランチ作成）

## Claude Code への指示方法

Claude Code に Issue 番号または URL を渡して実装を依頼する。

### 指示例

```
# Issue 番号で指示
#42 を実装して

# gh コマンド形式
gh issue view #42 の内容を実装して

# URL で指示
https://github.com/PocketQueries/online_meeting_recorder/issues/42 を対応して
```

### Claude Code が行うこと

1. `gh issue view <番号>` で Issue の内容を確認
2. Issue に着手コメントを投稿し、ステータスを更新
3. 最新の main から Issue の種類に応じたブランチを作成
4. 関連ドキュメント（`docs/` 配下）を確認
5. 実装・テスト
6. コミット（Issue 番号を含む）
7. 完了コメントを Issue に投稿
8. PR を作成（Issue を自動クローズ）

## Issue ステータス管理

作業中に Issue のステータスを更新し、進捗を可視化する。

### ステータス遷移

```
Open → 作業開始（着手コメント投稿） → 実装中 → PR 作成 → マージで自動 Close
```

### Claude Code が行う Issue 操作

| タイミング | 操作 | コマンド例 |
|-----------|------|-----------|
| 作業開始時 | 着手コメント投稿 | `gh issue comment <番号> --body "着手します。ブランチ: feature/#42-add-export-csv"` |
| 作業完了時 | 完了コメント投稿 | `gh issue comment <番号> --body "実装完了。PR #XX を作成しました。"` |

### コミットと Issue の連携について

- コミットメッセージに `#123` を含めると、GitHub の Issue タイムラインに**リンクが自動表示**される
- ただしこれは「コメント」ではなく「参照リンク」として表示される
- 作業状況の明示的な報告には `gh issue comment` を使う

## コミットメッセージ規則

日本語で記述し、Issue 番号を末尾に含める。

```
feat: 録音キャンセル機能を追加 #42
fix: WAVヘッダ修復時のクラッシュを修正 #15
refactor: AudioRecorder のキャプチャロジックを分離 #30
docs: セットアップガイドを更新 #8
```

### プレフィックス

| プレフィックス | 用途 |
|----------------|------|
| `feat` | 新機能追加 |
| `fix` | バグ修正 |
| `refactor` | リファクタリング |
| `docs` | ドキュメント |
| `chore` | ビルド・設定等の雑務 |

## PR 作成ルール

- タイトル: コミットメッセージと同じ形式
- 本文に `Closes #123` を含めて Issue を自動クローズ
- PR テンプレート（`.github/pull_request_template.md`）に従って記載

### PR 本文の例

```markdown
## 変更内容

録音中にキャンセルボタンを押すと録音を中断できる機能を追加。
Closes #42

## 変更の種類

- [ ] バグ修正 (fix)
- [x] 新機能 (feat)
- [ ] リファクタリング (refactor)
- [ ] ドキュメント (docs)
- [ ] その他

## テスト方法

1. アプリを起動し録音を開始
2. キャンセルボタンをクリック
3. 録音が停止し、セッションが保存されることを確認

## チェックリスト

- [x] `dotnet build` が成功する
- [x] 動作確認済み
- [x] 必要に応じてドキュメントを更新した
```

## Issue テンプレートの使い分け

| テンプレート | いつ使うか |
|-------------|------------|
| Bug Report | 既存機能の不具合を報告する場合 |
| Feature Request | 新機能の追加や既存機能の改善を提案する場合 |

Issue に十分な情報を書くことで、Claude Code が文脈を正確に理解して実装できる。
特に以下の点を意識する：

- **Bug Report**: 再現手順を具体的に書く
- **Feature Request**: 背景・動機と期待する動作を明確にする
