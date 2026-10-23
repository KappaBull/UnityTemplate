using System;
using System.IO;
using SQLite;
using UnityEngine;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kappa
{
    public enum BetaSaveImportResult { Imported, SourceMissing, DestinationExists }

    public static class BetaSaveImport
    {
        // 同意を得た後、製品用ストアを初めて開く前に呼ぶ。モバイルでは他アプリの保存先を探索せず、
        // OSのファイル選択・共有などで読み取り可能になったセーブファイルを呼び出し側から渡す。
        // supportsVersionはワーカー上で同期実行するため、Unity APIや共有するゲーム状態を参照しない。
        public static async UniTask<BetaSaveImportResult> ImportAsync(string sourceDatabasePath, Func<string, string, int, bool> supportsVersion,
            string saveRoot = null, string databaseName = "save-data", SaveEnvironmentSettings configuration = null,
            CancellationToken cancellationToken = default)
        {
            LocalSaveStore.EnsureMainThread();
            if (string.IsNullOrWhiteSpace(sourceDatabasePath) || !Path.IsPathRooted(sourceDatabasePath))
                throw new ArgumentException("Selected beta save path must be absolute.", nameof(sourceDatabasePath));
            string source = Path.GetFullPath(sourceDatabasePath);
            string root = ResolveRoot(supportsVersion, saveRoot, databaseName, configuration);
            return await UniTask.RunOnThreadPool(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ImportCore(supportsVersion, source, root, databaseName);
            });
        }

        private static string ResolveRoot(Func<string, string, int, bool> supportsVersion,
            string saveRoot, string databaseName, SaveEnvironmentSettings configuration)
        {
            if (configuration == null) configuration = SaveEnvironmentSettings.Current;
            configuration.Validate(SaveEnvironmentSettings.IsProductionPlayer);
            if (configuration.Environment != SaveEnvironment.Production)
                throw new InvalidOperationException("Beta saves can only be imported into Production.");
            if (supportsVersion == null) throw new ArgumentNullException(nameof(supportsVersion));
            SQLiteSaveBackend.ValidateName(databaseName, nameof(databaseName));
            saveRoot ??= configuration.GetDefaultDirectory();
            if (!Path.IsPathRooted(saveRoot)) throw new ArgumentException("Save root must be absolute.", nameof(saveRoot));
            return saveRoot;
        }

        private static BetaSaveImportResult ImportCore(Func<string, string, int, bool> supportsVersion, string source, string saveRoot, string databaseName)
        {
            string destination = Path.Combine(saveRoot, databaseName + ".sqlite3");
            if (File.Exists(destination)) return BetaSaveImportResult.DestinationExists;
            if (!File.Exists(source)) return BetaSaveImportResult.SourceMissing;

            Directory.CreateDirectory(saveRoot);
            string temporary = Path.Combine(saveRoot, ".beta-import-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            try
            {
                // 稼働中のベータ版に未確定変更があっても、SQLiteの確定済みスナップショットだけを複製する。
                using (var reader = new SQLiteConnection(source, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex))
                {
                    reader.BusyTimeout = TimeSpan.FromSeconds(5);
                    reader.Execute("VACUUM INTO ?", temporary);
                }
                using (var snapshot = new SQLiteConnection(temporary, SQLiteOpenFlags.ReadOnly))
                {
                    if (snapshot.ExecuteScalar<int>("PRAGMA user_version") != 1 ||
                        snapshot.ExecuteScalar<string>("PRAGMA quick_check") != "ok")
                        throw new InvalidDataException("Unsupported or corrupt beta save database.");
                    var entries = snapshot.Query<ImportEntry>("SELECT slot AS Slot, entry_key AS EntryKey, version AS Version, payload AS Payload FROM saves");
                    foreach (var entry in entries)
                    {
                        SQLiteSaveBackend.ValidateName(entry.Slot, "slot");
                        if (string.IsNullOrWhiteSpace(entry.EntryKey) || entry.Payload == null || entry.Version < 1 ||
                            !supportsVersion(entry.Slot, entry.EntryKey, entry.Version))
                            throw new InvalidDataException("Beta save contains an unsupported entry or data version.");
                    }
                }
                // 同時に製品用セーブが作成されても上書きしない。検証を終えるまで製品用ファイルは公開しない。
                File.Move(temporary, destination);
                return BetaSaveImportResult.Imported;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        [UnityEngine.Scripting.Preserve]
        public sealed class ImportEntry
        {
            [UnityEngine.Scripting.Preserve] public ImportEntry() { }
            [UnityEngine.Scripting.Preserve] public string Slot { get; set; }
            [UnityEngine.Scripting.Preserve] public string EntryKey { get; set; }
            [UnityEngine.Scripting.Preserve] public int Version { get; set; }
            [UnityEngine.Scripting.Preserve] public byte[] Payload { get; set; }
        }
    }
}
