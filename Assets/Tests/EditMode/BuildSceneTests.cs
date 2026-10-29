using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace UnityTemplate.Tests.EditMode
{
    public class BuildSceneTests
    {
        [Test]
        public void AndroidUsesOnlyVulkanForYaui()
        {
            Assert.That(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android), Is.False);
            Assert.That(PlayerSettings.GetGraphicsAPIs(BuildTarget.Android),
                Is.EqualTo(new[] { GraphicsDeviceType.Vulkan }));
        }

        [Test]
        public void IosGraphicsIncludesMetalForYaui()
        {
            Assert.That(PlayerSettings.GetGraphicsAPIs(BuildTarget.iOS), Does.Contain(GraphicsDeviceType.Metal));
        }

        [Test]
        public void EnabledBuildScenesExist()
        {
            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();

            Assert.That(enabledScenes, Is.Not.Empty, "At least one enabled build scene is required.");
            foreach (var scene in enabledScenes)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path),
                    Is.Not.Null,
                    $"Enabled build scene does not exist: {scene.path}");
            }
        }

        [Test]
        public void EnabledBuildScenesHaveNoMissingScripts()
        {
            foreach (var entry in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                var scene = SceneManager.GetSceneByPath(entry.path);
                bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
                if (!alreadyLoaded) scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                                Is.Zero, $"Missing script in {entry.path}: {transform.name}");
                }
                finally
                {
                    if (!alreadyLoaded) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
