using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kappa;
using NUnit.Framework;
using SQLite;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTemplate.Tests.PlayMode
{
    public class LocalSavingIntegrationTests
    {
        private string directory;
        private SQLiteSaveBackend store;
        private SaveEnvironmentSettings configuration;

        private SQLiteSaveBackend OpenStore(string name = "save-data") => new SQLiteSaveBackend(directory, name, configuration);

        [Serializable]
        public class Progress
        {
            public string playerName;
            public int level;
            public int[] inventory;
        }

        [Serializable]
        public class LegacyProgress
        {
            public string playerName;
            public int level;
        }

        [Serializable]
        public class UpdatedProgress
        {
            public string displayName;
            public int level;
            public int coins;
        }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "unity-template-save-tests-" + Guid.NewGuid().ToString("N"));
            configuration = ScriptableObject.CreateInstance<SaveEnvironmentSettings>();
            store = OpenStore();
        }

        [TearDown]
        public void TearDown()
        {
            store?.Dispose();
            UnityEngine.Object.DestroyImmediate(configuration);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [UnityTest]
        public IEnumerator SavedProgressSurvivesFrameAndConnectionReopen()
        {
            store.SaveJson("slot-1", "progress", new Progress { playerName = "セーブ確認", level = 42, inventory = new[] { 1, 9 } });
            store.Dispose();
            yield return null;
            store = OpenStore();
            Assert.That(store.TryLoadJson<Progress>("slot-1", "progress", out var progress), Is.True);
            Assert.That(progress.playerName, Is.EqualTo("セーブ確認"));
            Assert.That(progress.level, Is.EqualTo(42));
            Assert.That(progress.inventory, Is.EqualTo(new[] { 1, 9 }));
        }

        [Test]
        public void BinaryPayloadAndEmptyPayloadRoundTrip()
        {
            byte[] bytes = { 0, 255, 1, 0 };
            store.Save("slot-1", "binary", bytes, 2);
            store.Save("slot-1", "empty", Array.Empty<byte>());
            Assert.That(store.TryLoad("slot-1", "binary", out var saved), Is.True);
            Assert.That(saved.Version, Is.EqualTo(2));
            Assert.That(saved.Payload, Is.EqualTo(bytes));
            Assert.That(store.TryLoad("slot-1", "empty", out var empty), Is.True);
            Assert.That(empty.Payload, Is.Empty);
        }

        [Test]
        public void MissingSaveReturnsFalseWithoutCreatingProgress()
        {
            Assert.That(store.TryLoadJson<Progress>("slot-1", "progress", out var missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(store.TryLoad("slot-1", "progress", out _), Is.False);
        }

        [Test]
        public void SavingSameKeyReplacesPreviousValue()
        {
            store.Save("slot-1", "value", new byte[] { 1 });
            store.Save("slot-1", "value", new byte[] { 2 });
            Assert.That(store.TryLoad("slot-1", "value", out var saved), Is.True);
            Assert.That(saved.Payload, Is.EqualTo(new byte[] { 2 }));
        }

        [Test]
        public void OlderWriterCannotOverwriteNewerDataFromAnotherConnection()
        {
            using (var newerWriter = OpenStore()) newerWriter.Save("slot-1", "value", new byte[] { 2 }, 2);
            Assert.Throws<InvalidDataException>(() => store.Save("slot-1", "value", new byte[] { 1 }, 1));
            store.Dispose();
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "value", out var retained), Is.True);
            Assert.That(retained.Version, Is.EqualTo(2));
            Assert.That(retained.Payload, Is.EqualTo(new byte[] { 2 }));
        }

        [Test]
        public void RejectedDowngradeRollsBackOtherTransactionChanges()
        {
            store.Save("slot-1", "newer", new byte[] { 2 }, 2);
            Assert.Throws<InvalidDataException>(() => store.RunTransaction(save =>
            {
                save.Save("slot-1", "other", new byte[] { 9 });
                save.Save("slot-1", "newer", new byte[] { 1 }, 1);
            }));
            Assert.That(store.TryLoad("slot-1", "other", out _), Is.False);
            Assert.That(store.TryLoad("slot-1", "newer", out var retained), Is.True);
            Assert.That(retained.Version, Is.EqualTo(2));
        }

        [Test]
        public void DevelopmentAllowsExplicitDowngradeButDoesNotRelaxJsonReading()
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":1}", configuration);
            using var development = new SQLiteSaveBackend(directory, "development", configuration);
            development.SaveJson("slot-1", "progress", new Progress { level = 2 }, 2);
            Assert.Throws<InvalidDataException>(() => development.TryLoadJson<Progress>("slot-1", "progress", out _, 1));
            development.SaveJson("slot-1", "progress", new Progress { level = 1 }, 1);
            Assert.That(development.TryLoadJson<Progress>("slot-1", "progress", out var loaded, 1), Is.True);
            Assert.That(loaded.level, Is.EqualTo(1));
            Assert.That(store.AllowsVersionDowngrade, Is.False);
        }

        [Test]
        public void DevelopmentAndProductionUseDifferentDefaultDirectories()
        {
            string production = configuration.GetDefaultDirectory();
            JsonUtility.FromJsonOverwrite("{\"environment\":1}", configuration);
            Assert.That(configuration.GetDefaultDirectory(), Is.EqualTo(Path.Combine(production, "Development")));
            Assert.Throws<InvalidOperationException>(() => configuration.Validate(true));
            JsonUtility.FromJsonOverwrite("{\"environment\":0}", configuration);
            Assert.DoesNotThrow(() => configuration.Validate(true));
            JsonUtility.FromJsonOverwrite("{\"environment\":2}", configuration);
            Assert.That(configuration.GetDefaultDirectory(), Is.EqualTo(Path.Combine(production, "Beta")));
            Assert.That(configuration.AllowsVersionDowngrade, Is.False);
            Assert.DoesNotThrow(() => configuration.Validate(true));
        }

        private SQLiteSaveBackend OpenBeta() => new SQLiteSaveBackend(Path.Combine(directory, "Beta"), configuration: configuration);

        private UniTask<BetaSaveImportResult> ImportBetaAsync(Func<string, string, int, bool> supportsVersion = null) =>
            BetaSaveImport.ImportAsync(supportsVersion ?? ((slot, key, version) => version == 1), directory, configuration: configuration);

        private static async UniTask AssertImportFails<TException>(Func<UniTask<BetaSaveImportResult>> operation) where TException : Exception
        {
            bool observed = false;
            try { await operation(); }
            catch (TException) { observed = true; }
            Assert.That(observed, Is.True, "Expected " + typeof(TException).Name);
        }

        [Test]
        public void BetaSaveIsNeverImportedByOpeningProduction()
        {
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 1 });
            Assert.That(store.TryLoad("slot-1", "value", out _), Is.False);
        }

        [UnityTest]
        public IEnumerator ExplicitImportPreservesSourceAndDoesNotResynchronize() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 1 });
            string betaPath = Path.Combine(directory, "Beta", "save-data.sqlite3");
            byte[] original = File.ReadAllBytes(betaPath);
            Assert.That(await ImportBetaAsync(), Is.EqualTo(BetaSaveImportResult.Imported));
            Assert.That(File.ReadAllBytes(betaPath), Is.EqualTo(original));
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "value", out var imported), Is.True);
            Assert.That(imported.Payload, Is.EqualTo(new byte[] { 1 }));
            store.Save("slot-1", "value", new byte[] { 2 });
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 3 });
            Assert.That(await ImportBetaAsync(), Is.EqualTo(BetaSaveImportResult.DestinationExists));
            Assert.That(store.TryLoad("slot-1", "value", out var production), Is.True);
            Assert.That(production.Payload, Is.EqualTo(new byte[] { 2 }));
        });

        [UnityTest]
        public IEnumerator ImportRejectsFutureEntryVersionsWithoutCreatingProductionSave() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 2 }, 2);
            await AssertImportFails<InvalidDataException>(() => ImportBetaAsync());
            Assert.That(File.Exists(store.DatabasePath), Is.False);
            Assert.That(Directory.GetFiles(directory, ".beta-import-*"), Is.Empty);
            using var source = OpenBeta();
            Assert.That(source.TryLoad("slot-1", "value", out var retained), Is.True);
            Assert.That(retained.Version, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator ImportDoesNotOverwriteEvenEmptyExistingProductionDatabase() => UniTask.ToCoroutine(async () =>
        {
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 1 });
            Assert.That(await ImportBetaAsync(), Is.EqualTo(BetaSaveImportResult.DestinationExists));
            Assert.That(store.TryLoad("slot-1", "value", out _), Is.False);
        });

        [UnityTest]
        public IEnumerator MissingBetaSaveDoesNotCreateProductionDatabase() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            Assert.That(await ImportBetaAsync(), Is.EqualTo(BetaSaveImportResult.SourceMissing));
            Assert.That(File.Exists(store.DatabasePath), Is.False);
        });

        [UnityTest]
        public IEnumerator FailedCompatibilityCheckLeavesBothSavesUnchanged() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 1 });
            await AssertImportFails<InvalidOperationException>(() => ImportBetaAsync((slot, key, version) => throw new InvalidOperationException("Simulated check failure")));
            Assert.That(File.Exists(store.DatabasePath), Is.False);
            Assert.That(Directory.GetFiles(directory, ".beta-import-*"), Is.Empty);
        });

        [UnityTest]
        public IEnumerator ImportDoesNotOverwriteProductionCreatedDuringCompatibilityCheck() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            using (var beta = OpenBeta()) beta.Save("slot-1", "value", new byte[] { 1 });
            await AssertImportFails<IOException>(() => ImportBetaAsync((slot, key, version) =>
            {
                using var production = new SQLiteSaveBackend(directory, "save-data", false);
                production.Save("slot-1", "value", new byte[] { 9 });
                return true;
            }));
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "value", out var saved), Is.True);
            Assert.That(saved.Payload, Is.EqualTo(new byte[] { 9 }));
            Assert.That(Directory.GetFiles(directory, ".beta-import-*"), Is.Empty);
        });

        [UnityTest]
        public IEnumerator ImportRejectsFutureDatabaseWithoutChangingSource() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            string betaPath;
            using (var beta = OpenBeta()) betaPath = beta.DatabasePath;
            using (var db = new SQLiteConnection(betaPath)) db.Execute("PRAGMA user_version = 99");
            byte[] original = File.ReadAllBytes(betaPath);
            await AssertImportFails<InvalidDataException>(() => ImportBetaAsync());
            Assert.That(File.Exists(store.DatabasePath), Is.False);
            Assert.That(File.ReadAllBytes(betaPath), Is.EqualTo(original));
        });

        [UnityTest]
        public IEnumerator ImportRejectsCorruptDatabaseWithoutCreatingProduction() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            string betaPath;
            using (var beta = OpenBeta()) betaPath = beta.DatabasePath;
            byte[] corrupt = Encoding.UTF8.GetBytes("not-a-database");
            File.WriteAllBytes(betaPath, corrupt);
            await AssertImportFails<SQLiteException>(() => ImportBetaAsync());
            Assert.That(File.Exists(store.DatabasePath), Is.False);
            Assert.That(File.ReadAllBytes(betaPath), Is.EqualTo(corrupt));
        });

        [UnityTest]
        public IEnumerator BetaRejectsDowngradeAndCannotImportIntoItself() => UniTask.ToCoroutine(async () =>
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":2}", configuration);
            using var beta = OpenBeta();
            beta.Save("slot-1", "value", new byte[] { 2 }, 2);
            Assert.Throws<InvalidDataException>(() => beta.Save("slot-1", "value", new byte[] { 1 }, 1));
            await AssertImportFails<InvalidOperationException>(() => ImportBetaAsync());
        });

        [UnityTest]
        public IEnumerator ImportCopiesOnlyCommittedStateWhileBetaHasAnOpenTransaction() => UniTask.ToCoroutine(async () =>
        {
            store.Dispose();
            File.Delete(store.DatabasePath);
            using var beta = OpenBeta();
            beta.Save("slot-1", "value", new byte[] { 1 });
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var writing = UniTask.RunOnThreadPool(() => beta.RunTransaction(save =>
            {
                save.Save("slot-1", "value", new byte[] { 2 });
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release beta writer.");
            }));
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await UniTask.WaitUntil(() => entered.IsSet, cancellationToken: timeout.Token);
                Assert.That(await ImportBetaAsync(), Is.EqualTo(BetaSaveImportResult.Imported));
            }
            finally { release.Set(); await writing; }
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "value", out var imported), Is.True);
            Assert.That(imported.Payload, Is.EqualTo(new byte[] { 1 }));
            Assert.That(beta.TryLoad("slot-1", "value", out var original), Is.True);
            Assert.That(original.Payload, Is.EqualTo(new byte[] { 2 }));
        });

        [Test]
        public void SlotsAndDeletionAreIsolated()
        {
            store.Save("slot-1", "value", new byte[] { 1 });
            store.Save("slot-2", "value", new byte[] { 2 });
            Assert.That(store.Remove("slot-1", "value"), Is.True);
            Assert.That(store.Remove("slot-1", "value"), Is.False);
            store.Save("slot-1", "value", new byte[] { 3 });
            store.DeleteSlot("slot-1");
            Assert.That(store.TryLoad("slot-1", "value", out _), Is.False);
            Assert.That(store.TryLoad("slot-2", "value", out var remaining), Is.True);
            Assert.That(remaining.Payload, Is.EqualTo(new byte[] { 2 }));
        }

        [Test]
        public void TransactionCommitsAllChanges()
        {
            store.RunTransaction(save =>
            {
                save.Save("slot-1", "player", new byte[] { 1 });
                save.Save("slot-1", "world", new byte[] { 2 });
            });
            store.Dispose();
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "player", out _), Is.True);
            Assert.That(store.TryLoad("slot-1", "world", out _), Is.True);
        }

        [Test]
        public void FailedTransactionRollsBackUpdatesInsertsAndDeletes()
        {
            store.Save("slot-1", "value", new byte[] { 1 });
            store.Save("slot-1", "keep", new byte[] { 7 });
            Assert.Throws<InvalidOperationException>(() => store.RunTransaction(save =>
            {
                save.Save("slot-1", "value", new byte[] { 2 });
                save.Save("slot-1", "new", new byte[] { 3 });
                save.Remove("slot-1", "keep");
                throw new InvalidOperationException("Simulated failure before commit");
            }));
            store.Dispose();
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "value", out var original), Is.True);
            Assert.That(original.Payload, Is.EqualTo(new byte[] { 1 }));
            Assert.That(store.TryLoad("slot-1", "new", out _), Is.False);
            Assert.That(store.TryLoad("slot-1", "keep", out _), Is.True);
        }

        [Test]
        public void VersionMismatchPreservesDataForExplicitMigration()
        {
            store.SaveJson("slot-1", "progress", new Progress { level = 2 }, 2);
            Assert.Throws<InvalidDataException>(() => store.TryLoadJson<Progress>("slot-1", "progress", out _));
            Assert.That(store.TryLoad("slot-1", "progress", out var original), Is.True);
            Assert.That(original.Version, Is.EqualTo(2));
            var migrated = JsonUtility.FromJson<Progress>(Encoding.UTF8.GetString(original.Payload));
            migrated.level += 10;
            store.RunTransaction(save => save.SaveJson("slot-1", "progress", migrated, 3));
            Assert.That(store.TryLoadJson<Progress>("slot-1", "progress", out var loaded, 3), Is.True);
            Assert.That(loaded.level, Is.EqualTo(12));
        }

        [Test]
        public void UpgradeRenamesAndAddsFieldsInTheSameSaveFile()
        {
            store.SaveJson("slot-1", "progress", new LegacyProgress { playerName = "旧データ", level = 8 });
            string path = store.DatabasePath;
            store.Dispose();
            store = OpenStore();
            Assert.That(store.TryLoadJson<LegacyProgress>("slot-1", "progress", out var legacy), Is.True);
            var updated = new UpdatedProgress { displayName = legacy.playerName, level = legacy.level, coins = 100 };
            store.RunTransaction(save => save.SaveJson("slot-1", "progress", updated, 2));
            store.Dispose();
            store = OpenStore();
            Assert.That(store.DatabasePath, Is.EqualTo(path));
            Assert.That(store.TryLoadJson<UpdatedProgress>("slot-1", "progress", out var loaded, 2), Is.True);
            Assert.That(loaded.displayName, Is.EqualTo("旧データ"));
            Assert.That(loaded.level, Is.EqualTo(8));
            Assert.That(loaded.coins, Is.EqualTo(100));
        }

        [Test]
        public void FailedUpgradePreservesLegacyPayloadAndVersionAfterReopen()
        {
            store.SaveJson("slot-1", "progress", new LegacyProgress { playerName = "旧データ", level = 8 });
            Assert.That(store.TryLoad("slot-1", "progress", out var original), Is.True);
            Assert.Throws<InvalidOperationException>(() => store.RunTransaction(save =>
            {
                save.SaveJson("slot-1", "progress", new UpdatedProgress { displayName = "移行途中", level = 8, coins = 100 }, 2);
                save.Save("slot-1", "added-by-update", new byte[] { 1 }, 2);
                throw new InvalidOperationException("Simulated migration failure");
            }));
            store.Dispose();
            store = OpenStore();
            Assert.That(store.TryLoad("slot-1", "progress", out var retained), Is.True);
            Assert.That(retained.Version, Is.EqualTo(original.Version));
            Assert.That(retained.Payload, Is.EqualTo(original.Payload));
            Assert.That(store.TryLoad("slot-1", "added-by-update", out _), Is.False);
            Assert.That(store.TryLoadJson<LegacyProgress>("slot-1", "progress", out var legacy), Is.True);
            Assert.That(legacy.playerName, Is.EqualTo("旧データ"));
        }

        [Test]
        public void LegacyReaderRejectsUpdatedPayloadWithoutLosingNewFields()
        {
            store.SaveJson("slot-1", "progress", new UpdatedProgress { displayName = "新版データ", level = 8, coins = 777 }, 2);
            Assert.That(store.TryLoad("slot-1", "progress", out var original), Is.True);
            store.Dispose();
            store = OpenStore();
            Assert.Throws<InvalidDataException>(() => store.TryLoadJson<LegacyProgress>("slot-1", "progress", out _, 1));
            Assert.That(store.TryLoad("slot-1", "progress", out var retained), Is.True);
            Assert.That(retained.Version, Is.EqualTo(2));
            Assert.That(retained.Payload, Is.EqualTo(original.Payload));
            Assert.That(store.TryLoadJson<UpdatedProgress>("slot-1", "progress", out var updated, 2), Is.True);
            Assert.That(updated.coins, Is.EqualTo(777));
        }

        [Test]
        public void InvalidJsonIsNotReplacedWithDefaultData()
        {
            byte[] invalid = Encoding.UTF8.GetBytes("not-json");
            store.Save("slot-1", "progress", invalid);
            Assert.Throws<InvalidDataException>(() => store.TryLoadJson<Progress>("slot-1", "progress", out _));
            Assert.That(store.TryLoad("slot-1", "progress", out var saved), Is.True);
            Assert.That(saved.Payload, Is.EqualTo(invalid));
        }

        [Test]
        public void InvalidUtf8IsReportedInsteadOfReplacingCharacters()
        {
            byte[] prefix = Encoding.UTF8.GetBytes("{\"playerName\":\"");
            byte[] suffix = Encoding.UTF8.GetBytes("\"}");
            byte[] invalid = new byte[prefix.Length + 1 + suffix.Length];
            prefix.CopyTo(invalid, 0);
            invalid[prefix.Length] = 255;
            suffix.CopyTo(invalid, prefix.Length + 1);
            store.Save("slot-1", "progress", invalid);
            Assert.Throws<InvalidDataException>(() => store.TryLoadJson<Progress>("slot-1", "progress", out _));
        }

        [Test]
        public void BackupContainsSnapshotAndCannotOverwriteExistingFile()
        {
            store.Save("slot-1", "value", new byte[] { 1 });
            store.CreateBackup("backup-1");
            store.Save("slot-1", "value", new byte[] { 2 });
            Assert.Throws<IOException>(() => store.CreateBackup("backup-1"));
            using var backup = OpenStore("backup-1");
            Assert.That(backup.TryLoad("slot-1", "value", out var saved), Is.True);
            Assert.That(saved.Payload, Is.EqualTo(new byte[] { 1 }));
        }

        [Test]
        public void CorruptDatabaseIsReportedWithoutOverwritingIt()
        {
            store.Dispose();
            byte[] invalid = Encoding.UTF8.GetBytes("not-a-database");
            File.WriteAllBytes(store.DatabasePath, invalid);
            Assert.Throws<SQLiteException>(() => { using var unused = OpenStore(); });
            Assert.That(File.ReadAllBytes(store.DatabasePath), Is.EqualTo(invalid));
        }

        [Test]
        public void FutureDatabaseVersionIsRejectedWithoutChangingVersion()
        {
            store.Dispose();
            using (var database = new SQLiteConnection(store.DatabasePath)) database.Execute("PRAGMA user_version = 99");
            Assert.Throws<InvalidDataException>(() => { using var unused = OpenStore(); });
            using var reader = new SQLiteConnection(store.DatabasePath, SQLiteOpenFlags.ReadOnly);
            Assert.That(reader.ExecuteScalar<int>("PRAGMA user_version"), Is.EqualTo(99));
        }

        [Test]
        public void DisposedStoreRejectsOperations()
        {
            store.Dispose();
            Assert.Throws<ObjectDisposedException>(() => store.Save("slot-1", "value", new byte[] { 1 }));
            Assert.Throws<ObjectDisposedException>(() => store.TryLoad("slot-1", "value", out _));
        }

        [TestCase("../outside")]
        [TestCase("")]
        [TestCase("slot/child")]
        public void UnsafeNamesAreRejected(string name)
        {
            Assert.Throws<ArgumentException>(() => store.Save(name, "value", new byte[] { 1 }));
            Assert.Throws<ArgumentException>(() => store.CreateBackup(name));
            Assert.Throws<ArgumentException>(() => { using var unused = OpenStore(name); });
        }

        [Test]
        public void InvalidArgumentsAreRejectedBeforeMutation()
        {
            Assert.Throws<ArgumentNullException>(() => store.Save("slot-1", "value", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save("slot-1", "value", new byte[] { 1 }, 0));
            Assert.Throws<ArgumentException>(() => store.Save("slot-1", " ", new byte[] { 1 }));
            Assert.Throws<ArgumentException>(() => store.SaveJson("slot-1", "string", "not-a-data-class"));
            Assert.Throws<ArgumentNullException>(() => store.SaveJson<Progress>("slot-1", "progress", null));
        }

        [Test]
        public void TransactionCannotBeDisposedOrNested()
        {
            Assert.Throws<InvalidOperationException>(() => store.RunTransaction(save =>
            {
                save.Save("slot-1", "value", new byte[] { 1 });
                save.Dispose();
            }));
            Assert.That(store.TryLoad("slot-1", "value", out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => store.RunTransaction(save => save.RunTransaction(_ => { })));
            Assert.Throws<InvalidOperationException>(() => store.RunTransaction(save => save.CreateBackup("backup")));
        }
    }
}
