using System;
using System.IO;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(UnityTemplate.Tests.PlayMode.ProductionPlayerResults))]

namespace UnityTemplate.Tests.PlayMode
{
    [Preserve]
    public sealed class ProductionPlayerResults : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun) { }
        public void TestStarted(ITest test) { }
        public void TestFinished(ITestResult result) { }

        public void RunFinished(ITestResult result)
        {
            if (Application.isEditor) return;
            string path = Environment.GetEnvironmentVariable("UNITY_TEMPLATE_PLAYER_RESULTS");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, result.ToXml(true).OuterXml);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Application.Quit(1);
                return;
            }
            Application.Quit(result.FailCount == 0 && result.PassCount > 0 && result.SkipCount == 0 ? 0 : 1);
        }
    }
}
