namespace Unity.Entities
{
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using UnityEngine.Scripting;

    [Preserve]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.Editor)]
    public unsafe partial struct CompleteWorldDependencySystem : ISystem
    {
        private NativeList<JobHandle> m_dependencies;
        private JobHandle m_dependency;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_dependencies = new NativeList<JobHandle>(128, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            m_dependencies.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_dependency.Complete();

            using var e = state.WorldUnmanaged.GetImpl().m_SystemStatePtrMap.GetEnumerator();
            while (e.MoveNext())
            {
                var systemState = (SystemState*)e.Current.Value;
                this.m_dependencies.Add(systemState->m_JobHandle);
            }

            // This seems slightly faster than doing JobHandle.CompleteAll()
            m_dependency = JobHandle.CombineDependencies(m_dependencies.AsArray());
            m_dependencies.Clear();
        }
    }
}
