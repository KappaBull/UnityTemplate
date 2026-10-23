using BuildMagicEditor;
using UnityEditor;

namespace Kappa.Editor
{
    [GenerateBuildTaskAccessories("Template: Save Environment", PropertyName = "Template.SaveEnvironment")]
    public sealed class ConfigureSaveEnvironmentTask : BuildTaskBase<IPreBuildContext>
    {
        private readonly SaveEnvironment environment;

        public ConfigureSaveEnvironmentTask(SaveEnvironment environment) => this.environment = environment;

        public override void Run(IPreBuildContext context)
        {
            var settings = SaveEnvironmentSettings.Current;
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("environment").intValue = (int)environment;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.Validate(false);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
