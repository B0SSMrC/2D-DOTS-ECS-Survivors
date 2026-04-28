using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

namespace Survivors
{

    public struct EnemyTag : IComponentData {}

    public struct BossTag : IComponentData {}

    public struct EnemyAttackData : IComponentData
    {
        public int HitPoints;
        public float CooldownTime;
    }

    public struct EnemyCooldownExpirationTimestamp : IComponentData, IEnableableComponent
    {
        public double Value;
    }

    public struct GemPrefab : IComponentData
    {
        public Entity Value;
    }

    [RequireComponent(typeof(CharacterAuthoring))]
    public class EnemyAuthoring : MonoBehaviour
    {
        public bool IsBoss;
        public int AttackDamage;
        public float CooldownTime;
        public GameObject GemPrefab;
        private class Baker : Baker<EnemyAuthoring>
        {
            public override void Bake(EnemyAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<InitializeCharacterFlag>(entity);
                AddComponent<EnemyTag>(entity);
                if (authoring.IsBoss)
                {
                    AddComponent<BossTag>(entity);
                }
                AddComponent(entity, new EnemyAttackData
                {
                    HitPoints = authoring.AttackDamage,
                    CooldownTime = authoring.CooldownTime
                });
                AddComponent<EnemyCooldownExpirationTimestamp>(entity);
                SetComponentEnabled<EnemyCooldownExpirationTimestamp>(entity, false);
                AddComponent(entity, new GemPrefab
                {
                    Value = GetEntity(authoring.GemPrefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct EnemyMoveToPlayerSystem : ISystem
    {
        // 声明一个原生多值哈希表，用于存储网格内所有敌人的位置
        private NativeParallelMultiHashMap<int2, float2> _spatialHash;
        
        // --- 核心调优参数 ---
        // 网格大小：建议比敌人的直径稍微大一点
        private const float CELL_SIZE = 0.8f; 
        // 排斥半径的平方：在这个距离内敌人才会互相推挤 (假设排斥半径是 1.0)
        private const float SEPARATION_RADIUS_SQ = 0.4f; 
        // 排斥权重：值越大，敌人之间排斥力越强，阵型越散；值越小，越容易重叠
        private const float SEPARATION_WEIGHT = 1.5f; 

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            // 初始化空间哈希表，预估场上最多 10000 个敌人 (容量会自动扩容，但预设能减少开销)
            _spatialHash = new NativeParallelMultiHashMap<int2, float2>(10000, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            // 必须在系统销毁时释放非托管内存，防止内存泄漏
            if (_spatialHash.IsCreated)
            {
                _spatialHash.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();
            var enemyQuery = SystemAPI.QueryBuilder().WithAll<EnemyTag, LocalTransform, CharacterMoveDirection>().Build();
            int enemyCount = enemyQuery.CalculateEntityCount();

            if (enemyCount == 0) return;

            // 1. 每帧清空哈希表，并确保容量足够
            _spatialHash.Clear();
            if (_spatialHash.Capacity < enemyCount)
            {
                _spatialHash.Capacity = enemyCount;
            }

            // 2. 派发 Job 1：把所有敌人的位置塞进对应的虚拟网格中
            var populateJob = new PopulateSpatialHashJob
            {
                SpatialHash = _spatialHash.AsParallelWriter(),
                CellSize = CELL_SIZE
            };
            var populateHandle = populateJob.ScheduleParallel(enemyQuery, state.Dependency);

            // 获取玩家位置
            var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position.xy;

            // 3. 派发 Job 2：计算玩家引力 + 周围邻居的排斥力，得出最终移动方向
            var moveAndSeparateJob = new EnemyMoveAndSeparateJob
            {
                PlayerPosition = playerPosition,
                SpatialHash = _spatialHash, // 只读模式传递
                CellSize = CELL_SIZE,
                SeparationRadiusSq = SEPARATION_RADIUS_SQ,
                SeparationWeight = SEPARATION_WEIGHT
            };

            // 确保 Job 2 在 Job 1 (populateHandle) 完成后才执行
            state.Dependency = moveAndSeparateJob.ScheduleParallel(enemyQuery, populateHandle);
        }
    }


    // =========================================================
    // Job 1: 将敌人的位置填入空间哈希网格 (多线程安全并发写入)
    // =========================================================
    [BurstCompile]
    [WithAll(typeof(EnemyTag))]
    public partial struct PopulateSpatialHashJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int2, float2>.ParallelWriter SpatialHash;
        public float CellSize;

        private void Execute(in LocalTransform transform)
        {
            float2 pos = transform.Position.xy;
            // 计算当前坐标属于哪个网格 ID (例如坐标 3.2, 将落入第 3.2/0.8 号网格)
            int2 cell = new int2(math.floor(pos / CellSize));
            
            SpatialHash.Add(cell, pos);
        }
    }

    // =========================================================
    // Job 2: 结合向玩家移动的渴望 与 周围同类的排斥力
    // =========================================================
    [BurstCompile]
    [WithAll(typeof(EnemyTag))]
    public partial struct EnemyMoveAndSeparateJob : IJobEntity
    {
        public float2 PlayerPosition;
        [ReadOnly] public NativeParallelMultiHashMap<int2, float2> SpatialHash;
        public float CellSize;
        public float SeparationRadiusSq;
        public float SeparationWeight;

        private void Execute(ref CharacterMoveDirection direction, in LocalTransform transform)
        {
            float2 myPos = transform.Position.xy;

            // 1. 基础动力：计算向玩家移动的方向矢量
            float2 vectorToPlayer = PlayerPosition - myPos;
            float2 baseMoveDir = math.normalize(vectorToPlayer);

            // 2. 排斥动力：计算周围敌人的推力
            float2 separationForce = float2.zero;
            int2 centerCell = new int2(math.floor(myPos / CellSize));
            float separationRadius = math.sqrt(SeparationRadiusSq);

            // 遍历自己所在的网格，以及周围一圈共 9 个网格
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    int2 neighborCell = centerCell + new int2(x, y);

                    // 尝试获取这个网格里的第一个敌人位置
                    if (SpatialHash.TryGetFirstValue(neighborCell, out float2 otherPos, out var iterator))
                    {
                        do
                        {
                            float2 diff = myPos - otherPos;
                            float distSq = math.lengthsq(diff);

                            // 忽略自己(距离为0)，且只对排斥半径内的同类产生反应
                            if (distSq > 0.0001f && distSq < SeparationRadiusSq)
                            {
                                float dist = math.sqrt(distSq);
                                // 距离越近，排斥力越大 (线性衰减)
                                float falloff = 1.0f - (dist / separationRadius);
                                // (diff / dist) 是推开的方向
                                separationForce += (diff / dist) * falloff; 
                            }
                            
                        // 循环获取这个网格里的下一个敌人
                        } while (SpatialHash.TryGetNextValue(out otherPos, ref iterator));
                    }
                }
            }

            // 3. 力量融合：最终移动方向 = 玩家方向 + (排斥力 * 权重)
            float2 finalDir = baseMoveDir + (separationForce * SeparationWeight);

            // 防呆保护：如果合力微乎其微(比如被完全包围卡死)，就强行往玩家方向挤
            if (math.lengthsq(finalDir) > 0.001f)
            {
                direction.Value = math.normalize(finalDir);
            }
            else
            {
                direction.Value = baseMoveDir;
            }
        }
    }

    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsSimulationGroup))]
    [UpdateBefore(typeof(AfterPhysicsSystemGroup))]
    public partial struct EnemyAttackSystem : ISystem
    {
        

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var elapsedTime = SystemAPI.Time.ElapsedTime;
            foreach(var (expirationTimestamp, cooldownEnabled) in SystemAPI
            .Query<EnemyCooldownExpirationTimestamp, EnabledRefRW<EnemyCooldownExpirationTimestamp>>())
            {
                if(expirationTimestamp.Value > elapsedTime) continue;
                cooldownEnabled.ValueRW = false;
            }

            var attackJob = new EnemyAttackJob
            {
                PlayerLookup = SystemAPI.GetComponentLookup<PlayerTag>(true),
                AttackDataLookup = SystemAPI.GetComponentLookup<EnemyAttackData>(true),
                CooldownLookup = SystemAPI.GetComponentLookup<EnemyCooldownExpirationTimestamp>(),
                DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
                ElapsedTime = elapsedTime
            };

            var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
            state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);
        }
    }

    [BurstCompile]
    public struct EnemyAttackJob : ICollisionEventsJob
    {
        [ReadOnly] public ComponentLookup<PlayerTag> PlayerLookup;
        [ReadOnly] public ComponentLookup<EnemyAttackData> AttackDataLookup;
        public ComponentLookup<EnemyCooldownExpirationTimestamp> CooldownLookup;
        public BufferLookup<DamageThisFrame> DamageBufferLookup;
        public double ElapsedTime;
        public void Execute(CollisionEvent collisionEvent)
        {
            Entity playerEntity;
            Entity enemyEntity;

            if(PlayerLookup.HasComponent(collisionEvent.EntityA) && AttackDataLookup.HasComponent(collisionEvent.EntityB))
            {
                playerEntity = collisionEvent.EntityA;
                enemyEntity = collisionEvent.EntityB;
            }
            else if(PlayerLookup.HasComponent(collisionEvent.EntityB) && AttackDataLookup.HasComponent(collisionEvent.EntityA))
            {
                playerEntity = collisionEvent.EntityB;
                enemyEntity = collisionEvent.EntityA;
            }
            else
            {
                return;
            }

            if(CooldownLookup.IsComponentEnabled(enemyEntity)) return;

            var attackData = AttackDataLookup[enemyEntity];
            CooldownLookup[enemyEntity] = new EnemyCooldownExpirationTimestamp{Value = ElapsedTime + attackData.CooldownTime};
            CooldownLookup.SetComponentEnabled(enemyEntity, true);

            var playerDamageBuffer = DamageBufferLookup[playerEntity];
            playerDamageBuffer.Add(new DamageThisFrame
            {
                Value  = attackData.HitPoints
            });
        }
    }
}
