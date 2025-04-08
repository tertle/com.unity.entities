using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Unity.Properties;
using Unity.Entities.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    internal class SystemComponents : ITabContent
    {
        public string TabName { get; } = L10n.Tr("Dependencies");

        [CreateProperty]
        private readonly SystemComponentData m_SystemComponents;
        private bool m_IsVisible;

        public void OnTabVisibilityChanged(bool isVisible)
        {
            this.m_IsVisible = isVisible;
        }

        public SystemComponents(SystemComponentData systemComponents)
        {
            // m_Entities = entities;
            this.m_SystemComponents = systemComponents;
        }

        [UsedImplicitly]
        private class SystemComponentsInspector : PropertyInspector<SystemComponents>
        {
            private static readonly string k_SystemDependenciesSection = L10n.Tr("Scheduling Constraints");

            private readonly Cooldown m_Cooldown = new(TimeSpan.FromMilliseconds(Constants.Inspector.CoolDownTime));
            private readonly List<QueryWithEntitiesView> m_Views = new();
            private VisualElement m_SectionContainer;

            public override VisualElement Build()
            {
                var root = new VisualElement();
                Resources.Templates.DotsEditorCommon.AddStyles(root);
                this.m_SectionContainer = new VisualElement();
                root.Add(this.m_SectionContainer);

                this.m_SectionContainer.Add(this.BuildDependencyView());

                this.Update();
                return root;
            }

            private VisualElement BuildDependencyView()
            {
                var readList = this.Target.m_SystemComponents.GetComponentReadViewDataList();
                var writeList = this.Target.m_SystemComponents.GetComponentWriteViewDataList();

                var sectionElement = new VisualElement();

                var readSection = new FoldoutWithoutActionButton
                {
                    HeaderName = { text = $"Read Dependencies" },
                    MatchingCount = { text = readList.Count.ToString() }
                };
                readSection.Q<Toggle>().AddToClassList(UssClasses.FoldoutWithoutActionButton.ToggleNoBorder);
                var writeSection = new FoldoutWithoutActionButton
                {
                    HeaderName = { text = $"Write Dependencies" },
                    MatchingCount = { text = writeList.Count.ToString() }
                };
                writeSection.Q<Toggle>().AddToClassList(UssClasses.FoldoutWithoutActionButton.ToggleNoBorder);
                sectionElement.Add(readSection);
                sectionElement.Add(writeSection);

                foreach (var comp in readList)
                {
                    readSection.Add(new ComponentView(comp));
                }
                foreach (var comp in writeList)
                {
                    writeSection.Add(new ComponentView(comp));
                }
                return sectionElement;
            }

            public override void Update()
            {
                if (!this.Target.m_IsVisible || !this.m_Cooldown.Update(DateTime.UtcNow))
                {
                    return;
                }

                foreach (var view in this.m_Views)
                {
                    view.Update();
                }
            }
        }
    }
}
