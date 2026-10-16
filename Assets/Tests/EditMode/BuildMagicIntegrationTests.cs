using System.Linq;
using BuildMagicEditor;
using NUnit.Framework;
using UnityEditor;
using Kappa;
using Kappa.Editor;
using UnityEngine;

namespace UnityTemplate.Tests.EditMode
{
    public class BuildMagicIntegrationTests
    {
        private bool originalDevelopment;
        private string originalSettings;
        private string originalIdentifier;
        private bool originalIdentifierOverride;

        private static SerializedObject PlayerSettingsObject() =>
            new SerializedObject(Resources.FindObjectsOfTypeAll<PlayerSettings>().Single());

        [SetUp]
        public void SetUp()
        {
            originalDevelopment = EditorUserBuildSettings.development;
            originalSettings = EditorJsonUtility.ToJson(SaveEnvironmentSettings.Current);
            originalIdentifier = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone);
            originalIdentifierOverride = PlayerSettingsObject().FindProperty("overrideDefaultApplicationIdentifier").boolValue;
        }

        [TearDown]
        public void TearDown()
        {
            EditorUserBuildSettings.development = originalDevelopment;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, originalIdentifier);
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
                var scheme = BuildSchemeLoader.Load<BuildScheme>(name);
                Assert.That(scheme.Name, Is.EqualTo(name));
                Assert.That(scheme.PreBuildConfigurations.Count, Is.EqualTo(3));
                var provider = BuildTaskBuilderProvider.CreateDefault();
                var tasks = scheme.PreBuildConfigurations.Select(configuration =>
                {
                    var property = configuration.GatherProperty();
                    return provider.GetBuilder(configuration.TaskType, property.ValueType).Build(property.Value);
                }).OfType<IBuildTask<IPreBuildContext>>().ToArray();
                Assert.That(tasks.Length, Is.EqualTo(3));
                BuildMagicEditor.BuildPipeline.PreBuild(tasks);
                Assert.That(EditorUserBuildSettings.development, Is.EqualTo(expected));
                Assert.That(SaveEnvironmentSettings.Current.Environment,
                    Is.EqualTo(environment));
                string suffix = environment == SaveEnvironment.Production ? "" : "." + name.ToLowerInvariant();
                Assert.That(PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone),
                    Is.EqualTo("com.DefaultCompany.UnityTemplate" + suffix));
            }
            finally
            {
                EditorUserBuildSettings.development = original;
            }
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
        public void BetaUsesReleaseProtectionWithoutRequiringDevelopmentBuild()
        {
            JsonUtility.FromJsonOverwrite("{\"environment\":2}", SaveEnvironmentSettings.Current);
            Assert.That(SaveEnvironmentSettings.Current.AllowsVersionDowngrade, Is.False);
            Assert.DoesNotThrow(() => TemplateBuildPolicy.Validate(BuildOptions.None, SaveEnvironmentSettings.Current));
            Assert.Throws<UnityEditor.Build.BuildFailedException>(() => BuildCI.CreateProductionBuildOptions("Beta"));
        }
    }
}
