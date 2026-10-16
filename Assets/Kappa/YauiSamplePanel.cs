using UnityEngine;
using Yaui;

namespace Kappa
{
    public sealed class YauiSamplePanel : MonoBehaviour
    {
        [SerializeField] private YauiButton button;
        [SerializeField] private YauiText label;
        private int clicks;

        private void OnEnable()
        {
            button.OnClick.AddListener(OnClick);
        }

        private void OnDisable()
        {
            button.OnClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            clicks++;
            label.Text = $"Clicked {clicks} times";
        }
    }
}
