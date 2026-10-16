using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Yaui;

namespace UnityTemplate.Tests.PlayMode
{
    public class YauiRenderingTests
    {
        [UnityTest]
        public IEnumerator WorldPanelDrawsConfiguredColorToRenderTexture()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("YAUI pixel validation requires a graphics device; run locally without -nographics.");

            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Not.Null,
                "YAUI requires an active Universal Render Pipeline.");
            var cameraObject = new GameObject("Test YAUI Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            cameraObject.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 128, 24);
            camera.targetTexture = target;
            var panelObject = new GameObject("Test YAUI World Panel");
            Texture2D capture = null;
            try
            {
                var panel = panelObject.AddComponent<YauiPanel>();
                panel.RenderMode = PanelRenderMode.World;
                panel.ReferenceResolution = new Vector2(1000, 1000);
                panel.WorldScale = 0.001f;
                panel.EventCamera = camera;
                panel.GetComponent<YauiElement>().BackgroundColor = Color.magenta;
                YauiPanel.ForceUpdate();

                yield return null;
                yield return null;
                capture = new Texture2D(128, 128, TextureFormat.RGB24, false);
                var previousTarget = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                    capture.Apply();
                }
                finally
                {
                    RenderTexture.active = previousTarget;
                }
                var center = capture.GetPixel(capture.width / 2, capture.height / 2);

                Assert.That(center.r, Is.GreaterThan(0.8f));
                Assert.That(center.g, Is.LessThan(0.2f));
                Assert.That(center.b, Is.GreaterThan(0.8f));
            }
            finally
            {
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
