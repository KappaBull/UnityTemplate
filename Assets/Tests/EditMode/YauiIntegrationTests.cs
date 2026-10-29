using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Yaui;

namespace UnityTemplate.Tests.EditMode
{
    public class YauiIntegrationTests
    {
        private GameObject panelObject;
        private GameObject eventSystemObject;
        private YauiPanel panel;

        [SetUp]
        public void SetUp()
        {
            panelObject = new GameObject("Test YAUI Panel");
            panel = panelObject.AddComponent<YauiPanel>();
            panel.RenderMode = PanelRenderMode.World;
            panel.ReferenceResolution = new Vector2(800, 600);
            eventSystemObject = new GameObject("Test EventSystem", typeof(EventSystem));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(panelObject);
            Object.DestroyImmediate(eventSystemObject);
        }

        [Test]
        public void FlexLayoutReflowsWhenParentSizeChanges()
        {
            var childObject = new GameObject("Percent Element");
            childObject.transform.SetParent(panel.transform, false);
            var child = childObject.AddComponent<YauiElement>();
            var layout = child.Layout;
            layout.width = Length.Percent(50);
            layout.height = Length.Percent(25);
            child.Layout = layout;

            YauiPanel.ForceUpdate();
            Assert.That(child.LayoutRect.size, Is.EqualTo(new Vector2(400, 150)));

            panel.ReferenceResolution = new Vector2(1000, 800);
            YauiPanel.ForceUpdate();
            Assert.That(child.LayoutRect.size, Is.EqualTo(new Vector2(500, 200)));
        }

        [Test]
        public void ButtonHandlesEventSystemClickAndRejectsDisabledInput()
        {
            var childObject = new GameObject("Button", typeof(YauiElement));
            childObject.transform.SetParent(panel.transform, false);
            var button = childObject.AddComponent<YauiButton>();
            var clicks = 0;
            button.OnClick.AddListener(() => clicks++);
            var pointer = new PointerEventData(eventSystemObject.GetComponent<EventSystem>())
            {
                button = PointerEventData.InputButton.Left
            };

            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(clicks, Is.EqualTo(1));

            button.Interactable = false;
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(clicks, Is.EqualTo(1));
        }

        [Test]
        public void TextGenerationProducesMeasuredLayout()
        {
            var childObject = new GameObject("Text");
            childObject.transform.SetParent(panel.transform, false);
            var text = childObject.AddComponent<YauiText>();
            text.Text = "YAUI テスト";
            text.FontSize = 32;

            YauiPanel.ForceUpdate();

            Assert.That(text.LayoutRect.width, Is.GreaterThan(0));
            Assert.That(text.LayoutRect.height, Is.GreaterThan(0));
        }
    }
}
