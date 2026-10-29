using System;
using System.Linq;
using BuildMagicEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using BuildFailedException = UnityEditor.Build.BuildFailedException;

namespace Kappa.Editor
{
    public sealed class TemplateBuildPolicy : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Validate(report.summary.options, SaveEnvironmentSettings.Current);
            ValidateScriptingBackend(report.summary.platform);
        }

        public static void ValidateScriptingBackend(BuildTarget target)
        {
            var group = UnityEditor.BuildPipeline.GetBuildTargetGroup(target);
            if (group != BuildTargetGroup.Standalone && group != BuildTargetGroup.Android && group != BuildTargetGroup.iOS) return;
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.FromBuildTargetGroup(group)) != ScriptingImplementation.IL2CPP)
                throw new BuildFailedException("Player builds require IL2CPP; Mono fallback is not allowed.");
        }

        public static void Validate(BuildOptions options, SaveEnvironmentSettings settings)
        {
            if (settings == null) throw new BuildFailedException("Save environment settings are missing.");
            try { settings.Validate((options & BuildOptions.Development) == 0); }
            catch (InvalidOperationException exception) { throw new BuildFailedException(exception.Message); }
        }

        public static void ApplyProductionScheme(string name = "Production")
        {
            if (name != "Production") throw new BuildFailedException("CI only accepts the Production build scheme.");
            var scheme = BuildSchemeLoader.Load<BuildScheme>(name);
            if (!string.IsNullOrEmpty(scheme.BaseSchemeName))
                throw new BuildFailedException("CI Production scheme must contain its settings directly, without inheritance.");
            var provider = BuildTaskBuilderProvider.CreateDefault();
            var configurations = scheme.PreBuildConfigurations;
            var tasks = configurations.Select(configuration =>
            {
                var property = configuration.GatherProperty();
                return provider.GetBuilder(configuration.TaskType, property.ValueType).Build(property.Value);
            }).OfType<IBuildTask<IPreBuildContext>>().ToArray();
            BuildMagicEditor.BuildPipeline.PreBuild(tasks);
            if (EditorUserBuildSettings.development) throw new BuildFailedException("Production scheme must disable Development Build.");
            Validate(BuildOptions.None, SaveEnvironmentSettings.Current);
            if (SaveEnvironmentSettings.Current.Environment != SaveEnvironment.Production)
                throw new BuildFailedException("CI Production scheme must use Production saving, not Beta saving.");
        }
    }
}
