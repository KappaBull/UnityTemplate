using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kappa
{
    public sealed class LocalSaveStore
    {
        private readonly SQLiteSaveBackend backend;
        private UniTask tail = UniTask.CompletedTask;
        private bool closing;
        private UniTask closeTask;

        private LocalSaveStore(SQLiteSaveBackend backend) => this.backend = backend;

        public string DatabasePath => backend.DatabasePath;

        public static async UniTask<LocalSaveStore> OpenAsync(string directory = null, string databaseName = "save-data",
            SaveEnvironmentSettings configuration = null, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            string resolvedDirectory = SQLiteSaveBackend.ResolveDirectory(directory, configuration);
            bool allowsDowngrade = SQLiteSaveBackend.ResolveDowngrade(configuration);
            var opened = await UniTask.RunOnThreadPool(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new SQLiteSaveBackend(resolvedDirectory, databaseName, allowsDowngrade);
            });
            return new LocalSaveStore(opened);
        }

        public UniTask SaveAsync(string slot, string key, byte[] payload, int version = 1, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            byte[] snapshot = (byte[])payload.Clone();
            return Enqueue(save => { save.Save(slot, key, snapshot, version); return true; }, cancellationToken).AsUniTask();
        }

        public UniTask SaveJsonAsync<T>(string slot, string key, T data, int version = 1, CancellationToken cancellationToken = default) where T : class
        {
            EnsureMainThread();
            if (closing) throw new ObjectDisposedException(nameof(LocalSaveStore));
            // 呼び出し元のデータをワーカーと共有せず、保存要求時点の内容を確定する。
            byte[] snapshot = SaveJsonCodec.Encode(data);
            return Enqueue(save => { save.Save(slot, key, snapshot, version); return true; }, cancellationToken).AsUniTask();
        }

        public UniTask<(bool Found, SaveData Data)> TryLoadAsync(string slot, string key, CancellationToken cancellationToken = default) =>
            Enqueue(save =>
            {
                bool found = save.TryLoad(slot, key, out var data);
                return (found, found ? new SaveData(data.Version, data.Payload) : null);
            }, cancellationToken);

        public UniTask<(bool Found, T Data)> TryLoadJsonAsync<T>(string slot, string key, int expectedVersion = 1,
            CancellationToken cancellationToken = default) where T : class =>
            Enqueue(save => (save.TryLoadJson<T>(slot, key, out var data, expectedVersion), data), cancellationToken);

        public UniTask<bool> RemoveAsync(string slot, string key, CancellationToken cancellationToken = default) =>
            Enqueue(save => save.Remove(slot, key), cancellationToken);

        public UniTask DeleteSlotAsync(string slot, CancellationToken cancellationToken = default) =>
            Enqueue(save => { save.DeleteSlot(slot); return true; }, cancellationToken).AsUniTask();

        public UniTask<string> CreateBackupAsync(string name, CancellationToken cancellationToken = default) =>
            Enqueue(save => save.CreateBackup(name), cancellationToken);

        public UniTask RunTransactionAsync(Action<Transaction> update, CancellationToken cancellationToken = default)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            // async voidは最初のawaitで戻るため、未完了の更新を確定させる前に拒否する。
            foreach (var callback in update.GetInvocationList())
                if (callback.Method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                    throw new ArgumentException("Transaction callbacks must be synchronous. Await other work before starting the transaction.", nameof(update));
            return Enqueue(save =>
            {
                var transaction = new Transaction(save);
                try { save.RunTransaction(_ => update(transaction)); }
                finally { transaction.Invalidate(); }
                return true;
            }, cancellationToken).AsUniTask();
        }

        // 受付済みの処理を最後まで待ち、新しい要求を拒否してから接続を閉じる。
        public UniTask DisposeAsync()
        {
            EnsureMainThread();
            if (closing) return closeTask;
            var pending = Enqueue(save => { save.Dispose(); return true; }, CancellationToken.None);
            closing = true;
            closeTask = pending.AsUniTask().Preserve();
            return closeTask;
        }

        private UniTask<T> Enqueue<T>(Func<SQLiteSaveBackend, T> operation, CancellationToken cancellationToken)
        {
            EnsureMainThread();
            if (closing) throw new ObjectDisposedException(nameof(LocalSaveStore));
            var previous = tail;
            var completed = new UniTaskCompletionSource();
            tail = completed.Task;
            return Execute(previous, completed, operation, cancellationToken);
        }

        private async UniTask<T> Execute<T>(UniTask previous, UniTaskCompletionSource completed,
            Func<SQLiteSaveBackend, T> operation, CancellationToken cancellationToken)
        {
            try
            {
                await previous;
                return await UniTask.RunOnThreadPool(() =>
                {
                    // 開始前だけ取り消す。確定済みの書き込みをキャンセルとして報告しない。
                    cancellationToken.ThrowIfCancellationRequested();
                    return operation(backend);
                }, configureAwait: false);
            }
            finally
            {
                completed.TrySetResult();
                await UniTask.SwitchToMainThread();
            }
        }

        internal static void EnsureMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("Call save APIs from Unity's main thread.");
        }

        public sealed class SaveData
        {
            public int Version { get; }
            public byte[] Payload { get; }
            internal SaveData(int version, byte[] payload) { Version = version; Payload = payload; }
        }

        // トランザクション内だけ有効な同期操作。外へ保持せず、Unity APIやasyncコールバックを使用しない。
        public sealed class Transaction
        {
            private SQLiteSaveBackend active;
            private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
            internal Transaction(SQLiteSaveBackend backend) => active = backend;
            private SQLiteSaveBackend Current
            {
                get
                {
                    if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                        throw new InvalidOperationException("Use transaction operations only inside their worker callback.");
                    return active ?? throw new InvalidOperationException("Transaction callback has ended.");
                }
            }
            internal void Invalidate() => active = null;
            public void Save(string slot, string key, byte[] payload, int version = 1) => Current.Save(slot, key, payload, version);
            public void SaveJson<T>(string slot, string key, T data, int version = 1) where T : class => Current.SaveJson(slot, key, data, version);
            public bool TryLoad(string slot, string key, out SaveData data)
            {
                bool found = Current.TryLoad(slot, key, out var saved);
                data = found ? new SaveData(saved.Version, saved.Payload) : null;
                return found;
            }
            public bool TryLoadJson<T>(string slot, string key, out T data, int expectedVersion = 1) where T : class =>
                Current.TryLoadJson(slot, key, out data, expectedVersion);
            public bool Remove(string slot, string key) => Current.Remove(slot, key);
            public void DeleteSlot(string slot) => Current.DeleteSlot(slot);
        }
    }
}
