using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kappa;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTemplate.Tests.PlayMode
{
    public class AsyncLocalSavingTests
    {
        private string directory;
        private SaveEnvironmentSettings configuration;
        private LocalSaveStore store;

        [Serializable]
        public class Progress { public int level; public string name; }

        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async () =>
        {
            directory = Path.Combine(Path.GetTempPath(), "unity-async-save-tests-" + Guid.NewGuid().ToString("N"));
            configuration = ScriptableObject.CreateInstance<SaveEnvironmentSettings>();
            store = await LocalSaveStore.OpenAsync(directory, configuration: configuration);
        });

        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            if (store != null) await store.DisposeAsync();
            UnityEngine.Object.DestroyImmediate(configuration);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        });

        private static async UniTask WaitForWorker(ManualResetEventSlim entered)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await UniTask.WaitUntil(() => entered.IsSet, cancellationToken: timeout.Token);
        }

        private static void WaitForRelease(ManualResetEventSlim release)
        {
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release the worker.");
        }

        [UnityTest]
        public IEnumerator JsonSaveSurvivesReopenAndResumesOnMainThread() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveJsonAsync("slot-1", "progress", new Progress { level = 12, name = "非同期保存" });
            Assert.That(PlayerLoopHelper.IsMainThread, Is.True);
            await store.DisposeAsync();
            store = await LocalSaveStore.OpenAsync(directory, configuration: configuration);
            var loaded = await store.TryLoadJsonAsync<Progress>("slot-1", "progress");
            Assert.That(loaded.Found, Is.True);
            Assert.That(loaded.Data.level, Is.EqualTo(12));
            Assert.That(loaded.Data.name, Is.EqualTo("非同期保存"));
            Assert.That(PlayerLoopHelper.IsMainThread, Is.True);
        });

        [UnityTest]
        public IEnumerator FileWorkDoesNotBlockUnityFrames() => UniTask.ToCoroutine(async () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int workerThread = Thread.CurrentThread.ManagedThreadId;
            int mainThread = workerThread;
            LocalSaveStore.Transaction captured = null;
            var pending = store.RunTransactionAsync(save =>
            {
                captured = save;
                workerThread = Thread.CurrentThread.ManagedThreadId;
                entered.Set();
                WaitForRelease(release);
                save.Save("slot-1", "value", new byte[] { 1 });
            });
            try
            {
                await WaitForWorker(entered);
                int frame = Time.frameCount;
                await UniTask.NextFrame();
                await UniTask.NextFrame();
                Assert.That(Time.frameCount, Is.GreaterThan(frame));
                Assert.That(pending.Status.IsCompleted(), Is.False);
                Assert.That(workerThread, Is.Not.EqualTo(mainThread));
                Assert.Throws<InvalidOperationException>(() => captured.Save("slot-1", "wrong-thread", new byte[] { 9 }));
            }
            finally { release.Set(); await pending; }
        });

        [UnityTest]
        public IEnumerator UnawaitedRequestsKeepSubmissionOrder() => UniTask.ToCoroutine(async () =>
        {
            var first = store.SaveAsync("slot-1", "value", new byte[] { 1 });
            var second = store.SaveAsync("slot-1", "value", new byte[] { 2 });
            var third = store.SaveAsync("slot-1", "value", new byte[] { 3 });
            await first;
            await second;
            await third;
            var saved = await store.TryLoadAsync("slot-1", "value");
            Assert.That(saved.Data.Payload, Is.EqualTo(new byte[] { 3 }));
        });

        [UnityTest]
        public IEnumerator QueuedCancellationSkipsOnlyThatWrite() => UniTask.ToCoroutine(async () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource();
            var blocker = store.RunTransactionAsync(_ => { entered.Set(); WaitForRelease(release); });
            var canceled = store.SaveAsync("slot-1", "canceled", new byte[] { 2 }, cancellationToken: cancellation.Token);
            var following = store.SaveAsync("slot-1", "following", new byte[] { 3 });
            try { await WaitForWorker(entered); cancellation.Cancel(); }
            finally { release.Set(); await blocker; }
            Assert.That(await canceled.SuppressCancellationThrow(), Is.True);
            await following;
            Assert.That((await store.TryLoadAsync("slot-1", "canceled")).Found, Is.False);
            Assert.That((await store.TryLoadAsync("slot-1", "following")).Found, Is.True);
        });

        [UnityTest]
        public IEnumerator CancellationAfterWriteStartsReportsCommittedSuccess() => UniTask.ToCoroutine(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await store.RunTransactionAsync(save =>
            {
                save.Save("slot-1", "value", new byte[] { 7 });
                cancellation.Cancel();
            }, cancellation.Token);
            var saved = await store.TryLoadAsync("slot-1", "value");
            Assert.That(saved.Data.Payload, Is.EqualTo(new byte[] { 7 }));
        });

        [UnityTest]
        public IEnumerator FailedTransactionRollsBackAndQueueContinues() => UniTask.ToCoroutine(async () =>
        {
            var failed = store.RunTransactionAsync(save =>
            {
                save.Save("slot-1", "rolled-back", new byte[] { 1 });
                throw new InvalidOperationException("Simulated failure");
            });
            var following = store.SaveAsync("slot-1", "following", new byte[] { 2 });
            bool observed = false;
            try { await failed; }
            catch (InvalidOperationException) { observed = true; }
            Assert.That(observed, Is.True);
            Assert.That(PlayerLoopHelper.IsMainThread, Is.True);
            await following;
            Assert.That((await store.TryLoadAsync("slot-1", "rolled-back")).Found, Is.False);
            Assert.That((await store.TryLoadAsync("slot-1", "following")).Found, Is.True);
        });

        [UnityTest]
        public IEnumerator ClosingDrainsQueueAndRejectsNewRequests() => UniTask.ToCoroutine(async () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var blocker = store.RunTransactionAsync(_ => { entered.Set(); WaitForRelease(release); });
            var saved = store.SaveAsync("slot-1", "value", new byte[] { 8 });
            var closed = store.DisposeAsync();
            try
            {
                await WaitForWorker(entered);
                Assert.That(closed.Status.IsCompleted(), Is.False);
                Assert.Throws<ObjectDisposedException>(() => store.SaveAsync("slot-1", "late", new byte[] { 9 }));
            }
            finally { release.Set(); await blocker; }
            await closed;
            await saved;
            await store.DisposeAsync();
            store = await LocalSaveStore.OpenAsync(directory, configuration: configuration);
            Assert.That((await store.TryLoadAsync("slot-1", "value")).Data.Payload, Is.EqualTo(new byte[] { 8 }));
        });

        [UnityTest]
        public IEnumerator BinarySaveCapturesCallerBufferBeforeQueuing() => UniTask.ToCoroutine(async () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var blocker = store.RunTransactionAsync(_ => { entered.Set(); WaitForRelease(release); });
            byte[] bytes = { 1, 2 };
            var saved = store.SaveAsync("slot-1", "value", bytes);
            try { await WaitForWorker(entered); bytes[0] = 9; }
            finally { release.Set(); await blocker; }
            await saved;
            Assert.That((await store.TryLoadAsync("slot-1", "value")).Data.Payload, Is.EqualTo(new byte[] { 1, 2 }));
        });

        [UnityTest]
        public IEnumerator PreCanceledOpenDoesNotCreateADatabase() => UniTask.ToCoroutine(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool observed = false;
            try { await LocalSaveStore.OpenAsync(directory, "canceled", configuration, cancellation.Token); }
            catch (OperationCanceledException) { observed = true; }
            Assert.That(observed, Is.True);
            Assert.That(File.Exists(Path.Combine(directory, "canceled.sqlite3")), Is.False);
        });

        [UnityTest]
        public IEnumerator AsyncDowngradeFailurePreservesNewerDataAndQueue() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveJsonAsync("slot-1", "progress", new Progress { level = 2 }, 2);
            bool rejected = false;
            try { await store.SaveJsonAsync("slot-1", "progress", new Progress { level = 1 }, 1); }
            catch (InvalidDataException) { rejected = true; }
            Assert.That(rejected, Is.True);
            Assert.That((await store.TryLoadJsonAsync<Progress>("slot-1", "progress", 2)).Data.level, Is.EqualTo(2));
            await store.RunTransactionAsync(save =>
            {
                Assert.That(save.TryLoadJson<Progress>("slot-1", "progress", out var progress, 2), Is.True);
                progress.level = 3;
                save.SaveJson("slot-1", "progress", progress, 3);
            });
            Assert.That((await store.TryLoadJsonAsync<Progress>("slot-1", "progress", 3)).Data.level, Is.EqualTo(3));
        });

        [UnityTest]
        public IEnumerator TransactionCannotBeUsedAfterCallbackEnds() => UniTask.ToCoroutine(async () =>
        {
            LocalSaveStore.Transaction captured = null;
            await store.RunTransactionAsync(save => captured = save);
            Assert.Throws<InvalidOperationException>(() => captured.Save("slot-1", "late", new byte[] { 1 }));
        });

        [UnityTest]
        public IEnumerator BackupAndDeletionUseTheSameQueue() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync("slot-1", "value", new byte[] { 1 });
            await store.CreateBackupAsync("snapshot");
            Assert.That(await store.RemoveAsync("slot-1", "value"), Is.True);
            var backup = await LocalSaveStore.OpenAsync(directory, "snapshot", configuration);
            try { Assert.That((await backup.TryLoadAsync("slot-1", "value")).Found, Is.True); }
            finally { await backup.DisposeAsync(); }
            await store.SaveAsync("slot-2", "value", new byte[] { 2 });
            await store.DeleteSlotAsync("slot-2");
            Assert.That((await store.TryLoadAsync("slot-2", "value")).Found, Is.False);
        });

        [UnityTest]
        public IEnumerator BetaImportRunsCompatibilityCheckOffMainThread() => UniTask.ToCoroutine(async () =>
        {
            await store.DisposeAsync();
            File.Delete(store.DatabasePath);
            var beta = await LocalSaveStore.OpenAsync(Path.Combine(directory, "Beta"), configuration: configuration);
            try { await beta.SaveJsonAsync("slot-1", "progress", new Progress { level = 5 }); }
            finally { await beta.DisposeAsync(); }
            int workerThread = 0;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var result = await BetaSaveImport.ImportAsync((slot, key, version) =>
            {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                return key == "progress" && version == 1;
            }, directory, configuration: configuration);
            Assert.That(result, Is.EqualTo(BetaSaveImportResult.Imported));
            Assert.That(workerThread, Is.Not.EqualTo(mainThread));
            Assert.That(PlayerLoopHelper.IsMainThread, Is.True);
            store = await LocalSaveStore.OpenAsync(directory, configuration: configuration);
            Assert.That((await store.TryLoadJsonAsync<Progress>("slot-1", "progress")).Data.level, Is.EqualTo(5));
        });
    }
}
