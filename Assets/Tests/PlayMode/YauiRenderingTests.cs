using System.Collections;
using System.IO;
using Environment = System.Environment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Yaui;

namespace UnityTemplate.Tests.PlayMode
{
    [Category("ProductionPlayer")]
    public class YauiRenderingTests
    {
        [UnityTest]
        public IEnumerator WorldPanelDrawsConfiguredColorToRenderTexture()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "YAUI pixel validation requires a graphics device; do not run this suite with -nographics.");

            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Not.Null,
                "YAUI requires an active Universal Render Pipeline.");
            var cameraObject = new GameObject("Test YAUI Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
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
                // エラーシェーダーのマゼンタを正常な描画として受け入れない。
                panel.GetComponent<YauiElement>().BackgroundColor = Color.green;
                yield return null;
                YauiPanel.ForceUpdate();
                Assert.That(panel.DrawCallCount, Is.GreaterThan(0));
                // バッチ実行の自動描画に依存せず、通常のURP経路へ描画を要求する。
                var request = new RenderPipeline.StandardRequest { destination = target };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True);
                RenderPipeline.SubmitRenderRequest(camera, request);
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
                string report = Environment.GetEnvironmentVariable("UNITY_TEMPLATE_PLAYER_RESULTS");
                if (!string.IsNullOrEmpty(report))
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report), "yaui-render.png"), capture.EncodeToPNG());

                Assert.That(center.r, Is.LessThan(0.2f));
                Assert.That(center.g, Is.GreaterThan(0.8f));
                Assert.That(center.b, Is.LessThan(0.2f));
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
