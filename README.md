# UnityTemplate

YAUIとURPを使ってゲーム制作を始めるためのテンプレートです。共通の起動・入力・描画・パッケージ連携をUnity Test Runnerで確認します。ゲーム固有の機能や、すべての配信環境への対応を保証するものではありません。

## 描画とUI

優先する配信先はiOS・Android・macOS・Windowsです。WebGLは任意対応とし、現在のYAUI構成では対応済みとは扱いません。AndroidはYAUIの要件に合わせてVulkanのみを指定しているため、Vulkan非対応端末は対象外です。iOSはMetalを使用します。Steam配信やSteam固有機能との連携は未設定です。

URPのレンダーパイプラインと標準レンダラーを割り当て済みです。2D専用レンダラーなどへの変更は、制作するゲームに合わせて行ってください。

UIはYAUIを主軸にしています。YAUIは開発中のパッケージで、uGUI互換ではありません。利用できる描画APIはVulkan、Metal、Direct3D 11/12で、OpenGL ESは対象外です。配信先を決めた段階で、実機での描画・入力確認が必要です。[YAUIの導入条件](https://ruccho.com/YAUI/ja/getting-started/installation)

## テスト

- EditMode：ビルドシーン、Input System、YAUIのレイアウト・日本語テキスト・ボタン、音声取り込み設定、BuildMagicの設定適用。
- PlayMode：フレーム進行、UniTask、Cinemachine、Timeline、YAUIの描画、非同期保存とベータ版からの引き継ぎ。

YAUIの描画テストはRender Textureのピクセルを読み取って判定します。描画デバイスがない環境ではスキップされるため、結果のスキップ件数も確認してください。GPUを使った検証では `-nographics` を指定しません。

これらはテンプレートの基本機能を確認するテストです。ゲーム固有の仕様は、別途テストを追加してください。

## 音声取り込み

次のフォルダーを使用した場合のみ、読み込み方を自動設定します。

| フォルダー | 読み込み方式 |
| --- | --- |
| `Assets/Audio/BGM/` | Streaming |
| `Assets/Audio/Action/` | Compressed In Memory |

両方ともバックグラウンド読み込みを有効にし、音声データの事前読み込みを無効にします。モノラル化、圧縮形式、品質、サンプルレートは上書きしません。効果音の遅延やメモリ量はゲームごとに検証し、この方針が不要なら別のフォルダーを使用するか `SoundImporter` を変更してください。

## ローカル保存

[SQLite-net for Unity](https://github.com/gilzoide/unity-sqlite-net/tree/1.3.2)を使用します。ゲーム側の保存APIはUniTask版の `Kappa.LocalSaveStore` に統一しています。既定の保存先は `Application.persistentDataPath/Saves/save-data.sqlite3`。`OpenAsync` で接続し、ゲームの保存管理と同じ寿命で保持して、終了前に `DisposeAsync` を待ちます。独自のアセンブリ定義から使う場合は `Kappa.Runtime` と `UniTask` を参照してください。

```csharp
[System.Serializable]
public class Progress
{
    public int level;
    public string playerName;
}

public static class ProgressStorage
{
    public static async Cysharp.Threading.Tasks.UniTask SaveAsync(Progress progress)
    {
        var saves = await Kappa.LocalSaveStore.OpenAsync();
        try { await saves.SaveJsonAsync("slot-1", "progress", progress); }
        finally { await saves.DisposeAsync(); }
    }

    public static async Cysharp.Threading.Tasks.UniTask<(bool Found, Progress Data)> TryLoadAsync()
    {
        var saves = await Kappa.LocalSaveStore.OpenAsync();
        try { return await saves.TryLoadJsonAsync<Progress>("slot-1", "progress"); }
        finally { await saves.DisposeAsync(); }
    }
}
```

`SaveAsync` / `TryLoadAsync` は任意のバイト列、`SaveJsonAsync` / `TryLoadJsonAsync` はUnity標準のJSONで扱えるデータクラス用です。JSONではフィールドを使用し、辞書・任意の多態的データ・Unityオブジェクト参照は対象にしません。JSONの保存が完了するまで、渡したデータとその配下の配列・オブジェクトを変更しないでください。バイト列は受付時にコピーします。別のシリアライザーを使う場合はバイト列のAPIを使用します。スロット名とデータベース名は英数字・ハイフン・アンダースコアで1〜64文字です。

保存APIはUnityのメインスレッドから呼び出します。接続・JSON変換・ファイル操作はワーカースレッドで実行し、完了・例外はメインスレッドへ戻します。同じ接続の要求は受付順に処理され、前の処理が失敗しても後続は実行できます。毎フレーム保存を積み上げる設計にはしないでください。JSON変換時のデータを変更しない条件は[Unityのスレッド利用条件](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.ToJson.html)にも従います。

複数項目をまとめて保存する場合は `await saves.RunTransactionAsync(save => { ... })` 内で `Save`・`SaveJson`・`Remove` などを呼びます。途中で例外が発生した場合は全変更が取り消されます。このコールバックだけはワーカースレッド上の同期処理です。`async` にしたり、Unity APIや外部システムへの更新を含めたり、渡された操作窓口を外へ保持したりしないでください。

キャンセルはワーカーで処理を開始する前に確認し、待機中に取り消された要求は順番が来た時点で書き込まず終了します。開始後は確定または失敗まで待ち、保存済みの結果をキャンセルとして返しません。`DisposeAsync` は受付済みの要求を処理してから接続を閉じ、その呼び出し後は新しい要求を拒否します。アプリ終了やモバイルの強制終了で待ち続けられる保証はないため、保存を終了時だけに依存させないでください。

データがない場合だけ読み込み結果の `Found` が `false` になります。破損・入出力エラー・想定外のデータバージョンは例外として通知し、自動初期化しません。各項目のバージョンは保存時に指定できます。移行が必要なら `TryLoadAsync` で元データとバージョンを取得し、ゲーム固有の変換後にトランザクション内で保存してください。

`await saves.CreateBackupAsync("backup-1")` は同じフォルダーに別名のスナップショットを作成し、既存ファイルは上書きしません。`await LocalSaveStore.OpenAsync(databaseName: "backup-1")` でそのスナップショットを開けます。元データベースへの自動復元・世代整理・自動保存のタイミング・暗号化キーの管理は含みません。

パッケージはiOS・Android・macOS・Windowsを対応対象としていますが、このプロジェクトでの実機・IL2CPPビルド検証は別途必要です。WebGLは任意対応で、YAUIとの互換性も含めて対応済みとは扱いません。テストデータは一時フォルダーに隔離し、終了後に削除します。

## ビルドと保存環境

BuildMagic v1.12.1を導入しています。Window → Build Magicで `Development`・`Beta`・`Production` を選び、右クリックのApply Pre-build Nowから設定を適用できます。Development Buildと `Resources/SaveEnvironment` の保存設定、PC向けアプリ識別子をまとめて切り替えます。既定は `Production` で、Editorでも製品相当の保護を検証できます。Editor起動時の自動適用はありません。

`Production` と `Beta` は低いデータバージョンでの上書きを拒否し、`Development` だけ明示的な上書きを許可します。ベータ版は通常の製品ビルドとして作成でき、Development Buildを有効にする必要はありません。保存先は製品用の `Application.persistentDataPath/Saves/`、ベータ用の `Saves/Beta/`、開発用の `Saves/Development/` に分離します。JSON読み込み時のバージョン確認は省略しません。保存設定は接続作成時に確定するため、切り替え後は既存の `LocalSaveStore` を閉じて作り直してください。独自の保存先を指定するときは、3系統の分離も呼び出し側で行います。

PC向け識別子は製品用を基本形とし、ベータ用に `.beta`、開発用に `.development` を付けています。テンプレートを改名するときは3つの設定セット内の識別子も更新してください。3種類で同じ会社名・製品名を使い、ビルドの出力先は別々に指定します。会社名・製品名はPCの保存先に影響するため、種類ごとに変えると下記の既定の引き継ぎ先を共有できなくなります。[Unityの保存先仕様](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html)

### ベータ版からの引き継ぎ

自動取り込み・双方向同期は行いません。ゲーム側の確認画面でプレイヤーが取り込みを選んだ場合だけ、製品用の `LocalSaveStore` を初めて開く前に次を呼びます。確認画面自体とゲーム固有のデータ変換はテンプレートに含みません。

```csharp
var result = await Kappa.BetaSaveImport.ImportAsync((slot, key, version) =>
    key == "progress" && version == 1);
```

互換性確認のコールバックもワーカースレッドで実行され、全項目のスロット・キー・形式バージョンが渡されます。Unity APIは呼ばず、製品版が読み取りまたは明示的に移行できる形式だけ許可してください。キャンセルは取り込み開始前だけ有効です。アプリのリリース番号や更新日時で互換性を判断しません。元のベータ版セーブは残し、全項目の確認が成功したスナップショットだけを製品用へ複製します。引き継ぎ後の進行状況は別々です。

製品用ファイルが空でも既にあれば `DestinationExists`、ベータ用がなければ `SourceMissing` を返して何もしません。非対応形式・破損・入出力エラーは例外で通知し、製品用ファイルを公開しません。同時に製品用ファイルが作られた場合も上書きせず、入出力エラーになります。拒否・失敗後に自動で初期データを保存するかどうかは、ゲーム側で判断してください。

既定の取り込みは同じPC上で保存領域を共有できる構成向けです。iOS・Androidの別アプリ間や、macOSのサンドボックスで分離された領域を越える転送は含みません。Steamの配信・クラウド保存設定も別途必要です。

## CIと依存更新

CIの `BuildCI.BuildClientCMD` はビルド前に `Production` を適用し、開発用設定での製品ビルドを拒否します。CIでは別の設定セットや継承を指定できません。通常のUnityビルドにも同じ製品設定チェックが入ります。[BuildMagicの操作とCI手順](https://github.com/CyberAgentGameEntertainment/BuildMagic/tree/v1.12.1)

GitHub ActionsはUnity Test Runner、macOS向けビルド、ビルドしたアプリの起動確認の順に実行します。起動確認はクラッシュの検出が中心で、ゲーム内操作の正しさまで保証するものではありません。

新しいリポジトリでは、Actionsの利用を有効にし、Unityライセンスに必要なSecretsを設定してください。現行ワークフローは `UNITY_LICENSE`（旧名 `UNITY_LICENSE_2020` も参照）、`UNITY_EMAIL`、`UNITY_PASSWORD` を参照します。ライセンスの種類とCIでの利用可否を確認したうえで設定し、認証情報をファイルとしてコミットしないでください。

RenovateのGitHub Appも、新しいリポジトリに対して有効にする必要があります。自動マージの条件を確実にするため、RulesetsでUnityのテスト・ビルドを必須チェックに設定してください。Secrets、Appのアクセス許可、Rulesetsはテンプレートのファイルとは別に設定します。

Windowsで開発する場合でも、macOS向けCIの成功は配信先すべての検証にはなりません。必要なプラットフォームのビルドと実機テストを追加してください。

## 個人設定

`UserSettings`、`Library`、`Temp`、`Logs` は共有しません。Editorの個人設定やローカルのライセンス、認証情報をリポジトリへ追加しないでください。
