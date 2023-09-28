using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS =
    Unity.Entities.SourceGenerators.Test.CSharpSourceGeneratorVerifier<
        Unity.Entities.SourceGen.SystemGenerator.SystemGenerator>;

namespace Unity.Entities.SourceGenerators
{
    [TestClass]
    public class SystemGeneratorVerifyTests
    {
        [TestMethod]
        public async Task EntitiesForEachNonCapturing()
        {
            const string testSource = @"
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;

struct Translation : IComponentData { public float Value; }
struct TagComponent1 : IComponentData {}
struct TagComponent2 : IComponentData {}

partial class EntitiesForEachNonCapturing : SystemBase
{
    protected override void OnUpdate()
    {
        Entities.ForEach((ref Translation translation, ref TagComponent1 tag1, in TagComponent2 tag2) => { translation.Value += 5; }).Run();
    }
}";

            await VerifyCS.VerifySourceGeneratorAsync(testSource, nameof(EntitiesForEachNonCapturing), "Test0__System_19875963020.g.cs");
        }

        [TestMethod]
        public async Task EntitiesForEachCachesBufferTypeHandle()
        {
            const string testSource = @"
using Unity.Entities;

struct BufferData : IBufferElementData { public float Value; }

partial class EntitiesForEachDynamicBuffer : SystemBase
{
    protected override void OnUpdate()
    {
        Entities.ForEach((DynamicBuffer<BufferData> buf) => { }).Run();
    }
}";
            await VerifyCS.VerifySourceGeneratorAsync(testSource, nameof(EntitiesForEachCachesBufferTypeHandle), "Test0__System_19875963020.g.cs");
        }
    }
}
