namespace Unity.Entities.Editor
{
    using System.Collections.Generic;

    internal class SystemComponentData
    {
        private readonly World m_World;
        private SystemProxy m_SystemProxy;
        private readonly List<ComponentViewData> m_componentReadViewData;
        private readonly List<ComponentViewData> m_componentWriteViewData;

        public SystemComponentData(World world, SystemProxy systemProxy)
        {
            this.m_World = world;
            this.m_SystemProxy = systemProxy;
            this.m_componentReadViewData = new List<ComponentViewData>();
            this.m_componentWriteViewData = new List<ComponentViewData>();
        }

        public List<ComponentViewData> GetComponentReadViewDataList()
        {
            this.m_componentReadViewData.Clear();

            if (this.m_World == null || !this.m_World.IsCreated || !this.m_SystemProxy.Valid)
            {
                return this.m_componentReadViewData;
            }

            foreach (var comp in this.m_SystemProxy.GetJobDependencyForReadingSystems())
            {
                this.m_componentReadViewData.Add(comp);
            }

            return this.m_componentReadViewData;
        }

        public List<ComponentViewData> GetComponentWriteViewDataList()
        {
            this.m_componentWriteViewData.Clear();

            if (this.m_World == null || !this.m_World.IsCreated || !this.m_SystemProxy.Valid)
            {
                return this.m_componentWriteViewData;
            }

            foreach (var comp in this.m_SystemProxy.GetJobDependencyForWritingSystems())
            {
                this.m_componentWriteViewData.Add(comp);
            }

            return this.m_componentWriteViewData;
        }
    }
}
