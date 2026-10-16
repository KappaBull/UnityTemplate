using System;
using System.Collections;
using System.IO;
using SQLite;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("UnityTemplate.PlayModeTests")]

namespace Kappa
{
    internal sealed class SQLiteSaveBackend : IDisposable
    {
        private const int DatabaseVersion = 1;
        private static readonly System.Text.Encoding Utf8 = new System.Text.UTF8Encoding(false, true);
        private readonly object gate = new object();
        private readonly string directory;
        private SQLiteConnection connection;
        private bool inTransaction;

        public string DatabasePath { get; }
        public bool AllowsVersionDowngrade { get; }

        public SQLiteSaveBackend(string directory = null, string databaseName = "save-data", SaveEnvironmentSettings configuration = null)
            : this(ResolveDirectory(directory, configuration), databaseName, ResolveDowngrade(configuration)) { }

        internal static string ResolveDirectory(string directory, SaveEnvironmentSettings configuration)
        {
            if (configuration == null) configuration = SaveEnvironmentSettings.Current;
            configuration.Validate(SaveEnvironmentSettings.IsProductionPlayer);
            return directory ?? configuration.GetDefaultDirectory();
        }

        internal static bool ResolveDowngrade(SaveEnvironmentSettings configuration)
        {
            if (configuration == null) configuration = SaveEnvironmentSettings.Current;
            configuration.Validate(SaveEnvironmentSettings.IsProductionPlayer);
            return configuration.AllowsVersionDowngrade;
        }

        internal SQLiteSaveBackend(string directory, string databaseName, bool allowsVersionDowngrade)
        {
            ValidateName(databaseName, nameof(databaseName));
            AllowsVersionDowngrade = allowsVersionDowngrade;
            if (!Path.IsPathRooted(directory)) throw new ArgumentException("Save directory must be absolute.", nameof(directory));
            this.directory = Path.GetFullPath(directory);
            DatabasePath = Path.Combine(this.directory, databaseName + ".sqlite3");
            Directory.CreateDirectory(this.directory);

            var opened = new SQLiteConnection(DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            try
            {
                int version = opened.ExecuteScalar<int>("PRAGMA user_version");
                if (version != 0 && version != DatabaseVersion)
                    throw new InvalidDataException("Unsupported save database version: " + version);
                if (version == 0)
                {
                    if (opened.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'") != 0)
                        throw new InvalidDataException("The file is not a SQLiteSaveBackend database.");
                    opened.RunInTransaction(() =>
                    {
                        opened.Execute("CREATE TABLE saves (slot TEXT NOT NULL, entry_key TEXT NOT NULL, version INTEGER NOT NULL, payload BLOB NOT NULL, PRIMARY KEY (slot, entry_key))");
                        opened.Execute("PRAGMA user_version = 1");
                    });
                }
                opened.BusyTimeout = TimeSpan.FromSeconds(5);
                opened.ExecuteScalar<string>("PRAGMA journal_mode = DELETE");
                opened.Execute("PRAGMA synchronous = FULL");
                connection = opened;
            }
            catch
            {
                opened.Dispose();
                throw;
            }
        }

        public void Save(string slot, string key, byte[] payload, int version = 1)
        {
            ValidateEntry(slot, key);
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            lock (gate)
            {
                // バージョン確認と更新を同じSQLにし、別接続からの更新との競合でも旧形式で上書きしない。
                int changed = GetConnection().Execute(
                    "INSERT INTO saves (slot, entry_key, version, payload) VALUES (?, ?, ?, ?) " +
                    "ON CONFLICT(slot, entry_key) DO UPDATE SET version=excluded.version, payload=excluded.payload " +
                    "WHERE ? OR saves.version <= excluded.version",
                    slot, key, version, payload, AllowsVersionDowngrade);
                if (changed == 0) throw new InvalidDataException("A newer save version cannot be overwritten by an older version.");
            }
        }

        public bool TryLoad(string slot, string key, out SaveData data)
        {
            ValidateEntry(slot, key);
            lock (gate)
            {
                var rows = GetConnection().Query<SaveData>("SELECT version AS Version, payload AS Payload FROM saves WHERE slot = ? AND entry_key = ?", slot, key);
                data = rows.Count == 0 ? null : rows[0];
                return data != null;
            }
        }

        public void SaveJson<T>(string slot, string key, T data, int version = 1) where T : class
        {
            ValidateJsonType<T>();
            if (data == null) throw new ArgumentNullException(nameof(data));
            Save(slot, key, Utf8.GetBytes(JsonUtility.ToJson(data)), version);
        }

        public bool TryLoadJson<T>(string slot, string key, out T data, int expectedVersion = 1) where T : class
        {
            ValidateJsonType<T>();
            if (expectedVersion < 1) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            data = null;
            if (!TryLoad(slot, key, out var saved)) return false;
            if (saved.Version != expectedVersion)
                throw new InvalidDataException($"Save version {saved.Version} does not match expected version {expectedVersion}.");
            try
            {
                data = JsonUtility.FromJson<T>(Utf8.GetString(saved.Payload));
                if (data == null) throw new InvalidDataException("Save payload is empty.");
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Save payload could not be decoded.", exception);
            }
            return true;
        }

        public bool Remove(string slot, string key)
        {
            ValidateEntry(slot, key);
            lock (gate) return GetConnection().Execute("DELETE FROM saves WHERE slot = ? AND entry_key = ?", slot, key) != 0;
        }

        public void DeleteSlot(string slot)
        {
            ValidateName(slot, nameof(slot));
            lock (gate) GetConnection().Execute("DELETE FROM saves WHERE slot = ?", slot);
        }

        // 同期コールバックの完了を保存単位とする。async voidや外部副作用は含めない。
        public void RunTransaction(Action<SQLiteSaveBackend> update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            lock (gate)
            {
                var database = GetConnection();
                if (inTransaction) throw new InvalidOperationException("Nested save transactions are not supported.");
                inTransaction = true;
                try { database.RunInTransaction(() => update(this)); }
                finally { inTransaction = false; }
            }
        }

        // 新しい名前へのスナップショットのみ許可し、既存のバックアップを上書きしない。
        public string CreateBackup(string backupName)
        {
            ValidateName(backupName, nameof(backupName));
            string destination = Path.Combine(directory, backupName + ".sqlite3");
            lock (gate)
            {
                var database = GetConnection();
                if (inTransaction) throw new InvalidOperationException("Cannot back up an open save transaction.");
                if (File.Exists(destination)) throw new IOException("Backup destination already exists.");
                database.Execute("VACUUM INTO ?", destination);
                return destination;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (inTransaction) throw new InvalidOperationException("Cannot dispose an open save transaction.");
                connection?.Dispose();
                connection = null;
            }
        }

        private SQLiteConnection GetConnection() => connection ?? throw new ObjectDisposedException(nameof(SQLiteSaveBackend));

        private static void ValidateEntry(string slot, string key)
        {
            ValidateName(slot, nameof(slot));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A save key is required.", nameof(key));
        }

        internal static void ValidateName(string name, string parameter)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) throw new ArgumentException("Use a name of 1 to 64 characters.", parameter);
            foreach (char character in name)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '-' && character != '_')
                    throw new ArgumentException("Use letters, numbers, hyphens or underscores in names.", parameter);
        }

        private static void ValidateJsonType<T>()
        {
            var type = typeof(T);
            if (!type.IsSerializable || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type))
                throw new ArgumentException("Use a serializable data class for JSON saves, or use the byte API with another serializer.");
        }

        [UnityEngine.Scripting.Preserve]
        public sealed class SaveData
        {
            [UnityEngine.Scripting.Preserve] public SaveData() { }
            [UnityEngine.Scripting.Preserve] public int Version { get; set; }
            [UnityEngine.Scripting.Preserve] public byte[] Payload { get; set; }
        }
    }
}
