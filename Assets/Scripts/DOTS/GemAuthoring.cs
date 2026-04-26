using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;
namespace Survivors
{
    public struct GemTag : IComponentData {}

    public struct GemFlySpeed : IComponentData
    {
        public float Value;
    }
    public class GemAuthoring : MonoBehaviour
    {
        public float FlySpeed = 5f;
        private class Baker : Baker<GemAuthoring>
        {
            public override void Bake(GemAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<GemTag>(entity);
                AddComponent<DestroyEntityFlag>(entity);
                SetComponentEnabled<DestroyEntityFlag>(entity, false);

                AddComponent(entity, new GemFlySpeed { Value = authoring.FlySpeed });
                AddComponent(entity, new LifeTimeData { Value = 10f });
            }
        }
    }

    public partial struct CollectGemSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var newCollectJob = new CollectGemJob
            {
                GemLookup = SystemAPI.GetComponentLookup<GemTag>(true),
                GemsCollectedLookup = SystemAPI.GetComponentLookup<GemsCollectedCount>(),
                DestroyEntityLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>(),
                UpdateGemUILookup = SystemAPI.GetComponentLookup<UpdateGemUIFlag>()
            };

            var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
            state.Dependency = newCollectJob.Schedule(simulationSingleton, state.Dependency);
        }
    }

    [BurstCompile]
    public struct CollectGemJob : ITriggerEventsJob
    {
        [ReadOnly] public ComponentLookup<GemTag> GemLookup;
        public ComponentLookup<GemsCollectedCount> GemsCollectedLookup;
        public ComponentLookup<DestroyEntityFlag> DestroyEntityLookup;
        public ComponentLookup<UpdateGemUIFlag> UpdateGemUILookup;

        public void Execute(TriggerEvent triggerEvent)
        {
            Entity playerEntity;
            Entity gemEntity;

            if(GemLookup.HasComponent(triggerEvent.EntityA) && GemsCollectedLookup.HasComponent(triggerEvent.EntityB))
            {
                playerEntity = triggerEvent.EntityB;
                gemEntity = triggerEvent.EntityA;
            }
            else if(GemLookup.HasComponent(triggerEvent.EntityB) && GemsCollectedLookup.HasComponent(triggerEvent.EntityA))
            {
                playerEntity = triggerEvent.EntityA;
                gemEntity = triggerEvent.EntityB;
            }
            else
            {
                return;
            }

            var gemsCollected = GemsCollectedLookup[playerEntity];
            gemsCollected.Value += 1;
            GemsCollectedLookup[playerEntity] = gemsCollected;

            UpdateGemUILookup.SetComponentEnabled(playerEntity, true);

            DestroyEntityLookup.SetComponentEnabled(gemEntity, true);
        }
    }

    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct GemFlySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // 确保场景中有玩家才运行
            state.RequireForUpdate<PlayerTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 获取玩家的位置和拾取范围
            var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
            var pickupRange = SystemAPI.GetComponent<PlayerPickupRange>(playerEntity).Value;
            
            float deltaTime = SystemAPI.Time.DeltaTime;

            // 遍历所有带有 GemTag 和 GemFlySpeed 的宝石
            foreach (var (transform, flySpeed) in SystemAPI.Query<RefRW<LocalTransform>, GemFlySpeed>().WithAll<GemTag>())
            {
                float distanceSq = math.distancesq(transform.ValueRO.Position.xy, playerTransform.Position.xy);

                // 如果在吸引范围内
                if (distanceSq < pickupRange * pickupRange)
                {
                    // 计算飞向玩家的方向
                    float3 direction = math.normalize(playerTransform.Position - transform.ValueRO.Position);
                    
                    // 更新宝石位置
                    transform.ValueRW.Position += direction * flySpeed.Value * deltaTime;
                }
            }
        }
    }
}
