using System;
using System.IO;
using Kappa.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools;
using UnityEngine.TestTools;

[assembly: TestPlayerBuildModifier(typeof(UnityTemplate.Tests.EditMode.ProductionPlayerBuildModifier))]
[assembly: PostBuildCleanup(typeof(UnityTemplate.Tests.EditMode.ProductionPlayerBuildModifier))]

namespace UnityTemplate.Tests.EditMode
{
    public sealed class ProductionPlayerBuildModifier : ITestPlayerBuildModifier, IPostBuildCleanup, IPostprocessBuildWithReport
    {
        private static bool requested;
        private static BuildReport completedBuild;
        private static string bootstrapScene;
        public int callbackOrder => int.MaxValue;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-productionPlayerTests") < 0) return options;
            requested = true;
            completedBuild = null;
            bootstrapScene = options.scenes[0];
            if (!bootstrapScene.StartsWith("Assets/InitTestScene", StringComparison.Ordinal) ||
                !Guid.TryParse(Path.GetFileNameWithoutExtension(bootstrapScene).Substring("InitTestScene".Length), out _))
                throw new BuildFailedException("Production player tests require the Test Runner's temporary bootstrap scene.");
            return UseProductionSettings(options);
        }

        internal static BuildPlayerOptions UseProductionSettings(BuildPlayerOptions options)
        {
            TemplateBuildPolicy.ApplyProductionScheme();
            // 非DevelopmentではEditor接続に依存せず、テストPlayer自身が結果を保存する。
            options.options &= ~(BuildOptions.Development | BuildOptions.AllowDebugging |
                BuildOptions.ConnectToHost | BuildOptions.AutoRunPlayer);
            return options;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (requested) completedBuild = report;
        }

        public void Cleanup()
        {
            if (!requested) return;
            EditorApplication.delayCall += () =>
            {
                bool succeeded = completedBuild != null && completedBuild.summary.result == BuildResult.Succeeded &&
                    completedBuild.summary.totalErrors == 0;
                // 分離実行ではTest Runnerの実行後処理に到達しないため、生成されたシーンだけを片付ける。
                if (File.Exists(bootstrapScene) && !AssetDatabase.DeleteAsset(bootstrapScene)) succeeded = false;
                EditorApplication.Exit(succeeded ? 0 : 1);
            };
        }
    }
}
