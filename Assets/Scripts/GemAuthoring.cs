using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace Survivors
{
    public struct GemTag : IComponentData {}
    public class GemAuthoring : MonoBehaviour
    {
        private class Baker : Baker<GemAuthoring>
        {
            public override void Bake(GemAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<GemTag>(entity);
                AddComponent<DestroyEntityFlag>(entity);
                SetComponentEnabled<DestroyEntityFlag>(entity, false);


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
}
