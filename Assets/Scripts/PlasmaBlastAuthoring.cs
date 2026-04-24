using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Survivors
{
    public struct PlasmaBlastData : IComponentData
    {
        public float MoveSpeed;
        public int AttackDamage;
    }
    public class PlasmaBlastAuthoring : MonoBehaviour
    {
        public float MoveSpeed;
        public int AttackDamage;

        private class Baker : Baker<PlasmaBlastAuthoring>
        {
            public override void Bake(PlasmaBlastAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new PlasmaBlastData
                {
                    MoveSpeed = authoring.MoveSpeed,
                    AttackDamage = authoring.AttackDamage
                });
                AddComponent<DestroyEntityFlag>(entity);
                SetComponentEnabled<DestroyEntityFlag>(entity, false);
                AddComponent(entity, new LifeTimeData { Value = 5f });
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlasmaBlastSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();
            var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var deltaTime = SystemAPI.Time.DeltaTime;

            var enemyLookup = SystemAPI.GetComponentLookup<EnemyTag>(true);
            var damageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>();

            // 创建一个碰撞过滤器，去扫描空间中的任何物体
            var collisionFilter = new CollisionFilter
            {
                BelongsTo = uint.MaxValue,
                CollidesWith = uint.MaxValue,
                GroupIndex = 0
            };

            foreach (var (transform, blastData, entity) in SystemAPI.Query<RefRW<LocalTransform>, PlasmaBlastData>().WithEntityAccess())
            {
                var currentPos = transform.ValueRO.Position;
                var moveStep = transform.ValueRO.Right() * blastData.MoveSpeed * deltaTime;
                var nextPos = currentPos + moveStep;

                // 连续碰撞检测 (CCD)
                // 画一个“虚拟的方块”，它的范围涵盖了子弹【这一帧的起点】到【下一帧的终点】
                // 这样无论子弹一帧飞多快，中间的敌人绝对逃不掉！
                var bulletRadius = new float3(0.4f, 0.4f, 0.5f); // 给判定框稍微加一点厚度
                var aabb = new Aabb
                {
                    Min = math.min(currentPos, nextPos) - bulletRadius,
                    Max = math.max(currentPos, nextPos) + bulletRadius
                };

                var overlapInput = new OverlapAabbInput
                {
                    Aabb = aabb,
                    Filter = collisionFilter
                };

                // 向底层的物理空间发起数学查询
                var hits = new NativeList<int>(state.WorldUpdateAllocator);
                bool isDestroyed = false;

                if (physicsWorld.OverlapAabb(overlapInput, ref hits))
                {
                    foreach (var hit in hits)
                    {
                        var hitEntity = physicsWorld.Bodies[hit].Entity;

                        // 确认我们扫到的是敌人
                        if (enemyLookup.HasComponent(hitEntity))
                        {
                            if (damageBufferLookup.HasBuffer(hitEntity))
                            {
                                // 给敌人加上伤害
                                damageBufferLookup[hitEntity].Add(new DamageThisFrame { Value = blastData.AttackDamage });
                            }

                            // 💥 击中敌人后，标记销毁子弹并跳出循环
                            isDestroyed = true;
                            ecb.DestroyEntity(entity);
                            break; 
                            
                            // 如果想无限穿透
                            // 把上面两行（isDestroyed = true; 和 break;）注释掉即可！
                        }
                    }
                }

                // 如果子弹没有撞到任何敌人，才真正向前移动它
                if (!isDestroyed)
                {
                    transform.ValueRW.Position = nextPos;
                }
            }
        }
    }

    // public partial struct MovePlasmaBlastSystem : ISystem
    // {
    //     public void OnUpdate(ref SystemState state)
    //     {
    //         foreach(var (velocity, transform, data) in SystemAPI.Query<RefRW<PhysicsVelocity>, RefRO<LocalTransform>, PlasmaBlastData>())
    //         {

    //             velocity.ValueRW.Linear = transform.ValueRO.Right() * data.MoveSpeed;
    //         }
    //     }
    // }

    // [UpdateInGroup(typeof(PhysicsSystemGroup))]
    // [UpdateAfter(typeof(PhysicsSimulationGroup))]
    // [UpdateBefore(typeof(AfterPhysicsSystemGroup))]
    // public partial struct PlayerBlastAttackSystem : ISystem
    // {
    //     public void OnCreate(ref SystemState state)
    //     {
    //         state.RequireForUpdate<SimulationSingleton>();
    //     }
    //     public void OnUpdate(ref SystemState state)
    //     {
    //         var attackJob = new PlasmaBlastAttackJob
    //         {
    //             PlasmaBlastLookup = SystemAPI.GetComponentLookup<PlasmaBlastData>(true),
    //             EnemyLookup = SystemAPI.GetComponentLookup<EnemyTag>(true),
    //             DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
    //             DestroyEntityLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>()
    //         };

    //         var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
    //         state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);
    //     }
    // }

    // public struct PlasmaBlastAttackJob : ITriggerEventsJob
    // {
    //     [ReadOnly] public ComponentLookup<PlasmaBlastData> PlasmaBlastLookup;
    //     [ReadOnly] public ComponentLookup<EnemyTag> EnemyLookup;
    //     public BufferLookup<DamageThisFrame> DamageBufferLookup;
    //     public ComponentLookup<DestroyEntityFlag> DestroyEntityLookup;
    //     public void Execute(TriggerEvent triggerEvent)
    //     {
    //         Entity plasmaBlastEntity;
    //         Entity enemyEntity;
    //         if(PlasmaBlastLookup.HasComponent(triggerEvent.EntityA) 
    //         && EnemyLookup.HasComponent(triggerEvent.EntityB))
    //         {
    //             plasmaBlastEntity = triggerEvent.EntityA;
    //             enemyEntity = triggerEvent.EntityB;
    //         }else if(PlasmaBlastLookup.HasComponent(triggerEvent.EntityB) 
    //         && EnemyLookup.HasComponent(triggerEvent.EntityA))
    //         {
    //             plasmaBlastEntity = triggerEvent.EntityB;
    //             enemyEntity = triggerEvent.EntityA;
    //         }
    //         else
    //         {
    //             return;
    //         }

    //         var AttackDamage = PlasmaBlastLookup[plasmaBlastEntity].AttackDamage;
    //         var enemyDamageBuffer = DamageBufferLookup[enemyEntity];
    //         enemyDamageBuffer.Add(new DamageThisFrame { Value = AttackDamage});

    //         DestroyEntityLookup.SetComponentEnabled(plasmaBlastEntity, true);
    //     }
    // }



}
