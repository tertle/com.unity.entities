using Unity.Entities.UI;
using Unity.Properties;
using Unity.Scenes;
using Unity.Serialization;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    [DOTSEditorPreferencesSetting("BovineLabs")]
    public class CustomPreferences : ISetting
    {
        [CreateProperty, DontSerialize]
        public bool PlayModeClosedSubSceneBaking
        {
            get => CustomBakingSettings.PlayModeClosedSubSceneBaking;
            set => CustomBakingSettings.PlayModeClosedSubSceneBaking = value;
        }

        public void OnSettingChanged(PropertyPath path)
        {
        }

        public string[] GetSearchKeywords()
        {
            return ISetting.GetSearchKeywordsFromType(GetType());
        }

        class Inspector : PropertyInspector<CustomPreferences>
        {
            public override VisualElement Build()
            {
                var root = new VisualElement();

                var stopPlayModeClosedSubSceneBaking = new VisualElement();

                DoDefaultGui(stopPlayModeClosedSubSceneBaking, nameof(PlayModeClosedSubSceneBaking));

                root.Add(stopPlayModeClosedSubSceneBaking);

                return root;
            }
        }
    }
}
