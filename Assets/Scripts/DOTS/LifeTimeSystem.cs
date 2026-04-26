using Unity.Burst;
using Unity.Entities;

namespace Survivors
{
    // 定义生命周期数据组件
    public struct LifeTimeData : IComponentData
    {
        public float Value;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DestroyEntitySystem))] // 必须在销毁系统之前执行
    public partial struct LifeTimeSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            // 遍历所有带有 LifeTimeData 且尚未被标记销毁的实体
            foreach (var (lifeTime, entity) in SystemAPI.Query<RefRW<LifeTimeData>>()
                         .WithNone<DestroyEntityFlag>() // 如果已经被标记销毁了就跳过
                         .WithEntityAccess())
            {
                lifeTime.ValueRW.Value -= deltaTime;

                // 如果寿命耗尽，则打上我们之前写好的“销毁标签”
                if (lifeTime.ValueRO.Value <= 0)
                {
                    SystemAPI.SetComponentEnabled<DestroyEntityFlag>(entity, true);
                }
            }
        }
    }
}