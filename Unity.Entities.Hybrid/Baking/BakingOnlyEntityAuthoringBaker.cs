using Unity.Burst;
using Unity.Collections;

namespace Unity.Entities.Hybrid.Baking
{
    internal class BakingOnlyEntityAuthoringBaker : Baker<BakingOnlyEntityAuthoring>
    {

        [TemporaryBakingType]
        public struct BakingOnlyChildren : IBufferElementData
        {
            public Entity entity;
        }

        public override void Bake(BakingOnlyEntityAuthoring authoring)
        {
            // We don't need any transform components to make the entity bake only
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<BakingOnlyEntity>(entity);
            var childrenBuffer = AddBuffer<BakingOnlyChildren>(entity);

            foreach (var childGameObject in GetChildren(true))
            {
                // We don't need any transform components to make the child bake only
                var child = GetEntity(childGameObject, TransformUsageFlags.Dynamic);
                childrenBuffer.Add(new BakingOnlyChildren() {entity = child});
            }
        }
    }

    [BurstCompile]
    partial struct AddBakingOnlyEntityJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ConcurrentCommands;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex,
            in DynamicBuffer<BakingOnlyEntityAuthoringBaker.BakingOnlyChildren> childrenBuffer)
        {
            foreach (var child in childrenBuffer)
            {
                ConcurrentCommands.AddComponent<BakingOnlyEntity>(chunkIndex, child.entity);
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    partial class BakingOnlyEntityAuthoringBakingSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var job = new AddBakingOnlyEntityJob
            {
                ConcurrentCommands = ecb.AsParallelWriter(),
            };
            Dependency = job.ScheduleParallel(Dependency);

            CompleteDependency();
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}
