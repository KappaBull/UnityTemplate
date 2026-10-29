using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTemplate.Tests.PlayMode
{
    public class PlayerLoopTests
    {
        [UnityTest]
        public IEnumerator PlayerLoopAdvances()
        {
            var initialFrame = Time.frameCount;

            yield return null;

            Assert.That(Time.frameCount, Is.GreaterThan(initialFrame));
        }
    }
}
