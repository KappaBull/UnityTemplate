using System.Linq;
using BuildMagicEditor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using Kappa;
using Kappa.Editor;
using UnityEngine;
using BuildFailedException = UnityEditor.Build.BuildFailedException;

namespace UnityTemplate.Tests.EditMode
{
    public class BuildMagicIntegrationTests
    {
        private bool originalDevelopment;
        private string originalSettings;
        private static readonly NamedBuildTarget[] IdentifierTargets =
            { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS };
        private string[] originalIdentifiers;
        private ScriptingImplementation[] originalBackends;
        private AndroidArchitecture originalAndroidArchitectures;
        private bool originalIdentifierOverride;

        private static SerializedObject PlayerSettingsObject() =>
            new SerializedObject(Resources.FindObjectsOfTypeAll<PlayerSettings>().Single());

        [SetUp]
        public void SetUp()
        {
            originalDevelopment = EditorUserBuildSettings.development;
            originalSettings = EditorJsonUtility.ToJson(SaveEnvironmentSettings.Current);
            originalIdentifiers = IdentifierTargets.Select(PlayerSettings.GetApplicationIdentifier).ToArray();
            originalBackends = IdentifierTargets.Select(PlayerSettings.GetScriptingBackend).ToArray();
            originalAndroidArchitectures = PlayerSettings.Android.targetArchitectures;
            originalIdentifierOverride = PlayerSettingsObject().FindProperty("overrideDefaultApplicationIdentifier").boolValue;
        }

        [TearDown]
        public void TearDown()
        {
            EditorUserBuildSettings.development = originalDevelopment;
            for (int i = 0; i < IdentifierTargets.Length; i++)
            {
                PlayerSettings.SetApplicationIdentifier(IdentifierTargets[i], originalIdentifiers[i]);
                PlayerSettings.SetScriptingBackend(IdentifierTargets[i], originalBackends[i]);
            }
            PlayerSettings.Android.targetArchitectures = originalAndroidArchitectures;
            var playerSettings = PlayerSettingsObject();
            playerSettings.FindProperty("overrideDefaultApplicationIdentifier").boolValue = originalIdentifierOverride;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorJsonUtility.FromJsonOverwrite(originalSettings, SaveEnvironmentSettings.Current);
            EditorUtility.SetDirty(SaveEnvironmentSettings.Current);
            AssetDatabase.SaveAssets();
        }

        [TestCase("Development", true, SaveEnvironment.Development)]
        [TestCase("Production", false, SaveEnvironment.Production)]
        [TestCase("Beta", false, SaveEnvironment.Beta)]
        public void SchemeAppliesDevelopmentSettingAndRestoresEditorState(string name, bool expected, SaveEnvironment environment)
        {
            bool original = EditorUserBuildSettings.development;
            try
            {
                EditorUserBuildSettings.development = !expected;
                foreach (var target in IdentifierTargets)
                {
                    PlayerSettings.SetApplicationIdentifier(target, "com.example.previoustestchannel");
                    if (target != NamedBuildTarget.iOS) PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                }
                var scheme = BuildSchemeLoader.Load<BuildScheme>(name);
                Assert.That(scheme.Name, Is.EqualTo(name));
                Assert.That(scheme.PreBuildConfigurations.Count, Is.EqualTo(4));
                var provider = BuildTaskBuilderProvider.CreateDefault();
                var tasks = scheme.PreBuildConfigurations.Select(configuration =>
                {
                    var property = configuration.GatherProperty();
                    return provider.GetBuilder(configuration.TaskType, property.ValueType).Build(property.Value);
                }).OfType<IBuildTask<IPreBuildContext>>().ToArray();
                Assert.That(tasks.Length, Is.EqualTo(4));
                BuildMagicEditor.BuildPipeline.PreBuild(tasks);
                Assert.That(EditorUserBuildSettings.development, Is.EqualTo(expected));
                Assert.That(SaveEnvironmentSettings.Current.Environment,
                    Is.EqualTo(environment));
                string suffix = environment == SaveEnvironment.Production ? "" : "." + name.ToLowerInvariant();
                foreach (var target in IdentifierTargets)
                {
                    Assert.That(PlayerSettings.GetApplicationIdentifier(target),
                        Is.EqualTo("com.DefaultCompany.UnityTemplate" + suffix), target.TargetName);
                    Assert.That(PlayerSettings.GetScriptingBackend(target), Is.EqualTo(ScriptingImplementation.IL2CPP), target.TargetName);
                }
            }
            finally
            {
                EditorUserBuildSettings.development = original;
            }
        }

        [Test]
        public void PlayerBuildRejectsMonoInsteadOfSilentlyFallingBack()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Assert.Throws<BuildFailedException>(() => TemplateBuildPolicy.ValidateScriptingBackend(BuildTarget.StandaloneWindows64));
            Assert.Throws<BuildFailedException>(() => TemplateBuildPolicy.ValidateScriptingBackend(BuildTarget.StandaloneOSX));
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.Mono2x);
            Assert.Throws<BuildFailedException>(() => TemplateBuildPolicy.ValidateScriptingBackend(BuildTarget.Android));
            TemplateBuildPolicy.ApplyProductionScheme();
            foreach (var target in new[] { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneOSX, BuildTarget.Android, BuildTarget.iOS })
                Assert.DoesNotThrow(() => TemplateBuildPolicy.ValidateScriptingBackend(target));
        }

        [Test]
        public void ProductionBuildRejectsDevelopmentSavingEvenInEditor()
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":1}", SaveEnvironmentSettings.Current);
            Assert.Throws<UnityEditor.Build.BuildFailedException>(() =>
                TemplateBuildPolicy.Validate(BuildOptions.None, SaveEnvironmentSettings.Current));
            Assert.DoesNotThrow(() => TemplateBuildPolicy.Validate(BuildOptions.Development, SaveEnvironmentSettings.Current));
        }

        [Test]
        public void CiReappliesProductionSettingsBeforeCreatingBuildOptions()
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":1}", SaveEnvironmentSettings.Current);
            EditorUserBuildSettings.development = true;
            var options = BuildCI.CreateProductionBuildOptions();
            Assert.That(SaveEnvironmentSettings.Current.Environment, Is.EqualTo(SaveEnvironment.Production));
            Assert.That(EditorUserBuildSettings.development, Is.False);
            Assert.That(options.options & BuildOptions.Development, Is.EqualTo(BuildOptions.None));
            Assert.That(options.scenes, Is.Not.Empty);
            Assert.DoesNotThrow(() => TemplateBuildPolicy.Validate(options.options, SaveEnvironmentSettings.Current));
        }

        [Test]
        public void CiRejectsDevelopmentScheme()
        {
            Assert.Throws<UnityEditor.Build.BuildFailedException>(() => BuildCI.CreateProductionBuildOptions("Development"));
        }

        [Test]
        public void ProductionTestPlayerIncludesRunnerWithoutEditorConnectionOrDevelopmentFlags()
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":1}", SaveEnvironmentSettings.Current);
            var options = ProductionPlayerBuildModifier.UseProductionSettings(new BuildPlayerOptions
            {
                options = BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectToHost |
                    BuildOptions.IncludeTestAssemblies | BuildOptions.StrictMode | BuildOptions.AutoRunPlayer
            });
            Assert.That(options.options & (BuildOptions.Development | BuildOptions.AllowDebugging), Is.EqualTo(BuildOptions.None));
            Assert.That(options.options & BuildOptions.IncludeTestAssemblies, Is.EqualTo(BuildOptions.IncludeTestAssemblies));
            Assert.That(options.options & BuildOptions.ConnectToHost, Is.EqualTo(BuildOptions.None));
            Assert.That(options.options & BuildOptions.AutoRunPlayer, Is.EqualTo(BuildOptions.None));
            Assert.That(SaveEnvironmentSettings.Current.Environment, Is.EqualTo(SaveEnvironment.Production));
        }

        [Test]
        public void BetaUsesReleaseProtectionWithoutRequiringDevelopmentBuild()
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":2}", SaveEnvironmentSettings.Current);
            Assert.That(SaveEnvironmentSettings.Current.AllowsVersionDowngrade, Is.False);
            Assert.DoesNotThrow(() => TemplateBuildPolicy.Validate(BuildOptions.None, SaveEnvironmentSettings.Current));
            Assert.Throws<UnityEditor.Build.BuildFailedException>(() => BuildCI.CreateProductionBuildOptions("Beta"));
        }
    }
}
