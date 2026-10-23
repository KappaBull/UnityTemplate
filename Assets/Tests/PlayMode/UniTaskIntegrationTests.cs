using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTemplate.Tests.PlayMode
{
    public class UniTaskIntegrationTests
    {
        [UnityTest]
        public IEnumerator NextFrameResumesOnUnityMainThread()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var initialFrame = Time.frameCount;
                var initialThread = Thread.CurrentThread.ManagedThreadId;

                await UniTask.NextFrame();

                Assert.That(Time.frameCount, Is.GreaterThan(initialFrame));
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(initialThread));
                Assert.That(PlayerLoopHelper.IsMainThread, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator PendingFrameDelayObservesCancellation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var pending = UniTask.DelayFrame(100, cancellationToken: cancellation.Token);
                    cancellation.Cancel();

                    var wasCanceled = await pending.SuppressCancellationThrow();

                    Assert.That(wasCanceled, Is.True);
                }
            });
        }
    }
}
