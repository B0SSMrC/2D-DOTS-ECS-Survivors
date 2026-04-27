using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Survivors
{
    public struct SpawnBossRequest : IComponentData { }
    public struct EnemySpawnData : IComponentData
    {
        public Entity EnemyPrefab;
        public Entity BossPrefab;
        public float SpawnInterval;
        public float SpawnDistance;
    }

    public struct EnemySpawnState : IComponentData
    {
        public float SpawnTimer;
        public Random Random;

    }
    public class EnemySpawnerAuthoring : MonoBehaviour
    {
        public GameObject EnemyPrefab;
        public GameObject BossPrefab;
        public float SpawnInterval;
        public float SpawnDistance;
        public uint RandomSeed;
        
        private class Baker : Baker<EnemySpawnerAuthoring>
        {
            public override void Bake(EnemySpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new EnemySpawnData
                {
                    EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                    BossPrefab = GetEntity(authoring.BossPrefab, TransformUsageFlags.Dynamic),
                    SpawnInterval = authoring.SpawnInterval,
                    SpawnDistance = authoring.SpawnDistance
                });
                AddComponent(entity, new EnemySpawnState
                {
                    SpawnTimer = 0f,
                    Random = Random.CreateFromIndex(authoring.RandomSeed),
                });
            }
        }
    }

    public partial struct EnemySpawnSystem : ISystem
    {

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

            

            var spawnRequestQuery = SystemAPI.QueryBuilder().WithAll<SpawnBossRequest>().Build();
            if (!spawnRequestQuery.IsEmpty)
            {
                var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
                var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
                foreach (var (spawnState, spawnData) in SystemAPI.Query<RefRW<EnemySpawnState>, EnemySpawnData>())
                {
                    // 生成 Boss
                    var newBoss = ecb.Instantiate(spawnData.BossPrefab);
                    var bossTransform = SystemAPI.GetComponent<LocalTransform>(spawnData.BossPrefab);
                    bossTransform.Position = playerPosition + new float3(0, spawnData.SpawnDistance, 0);
                    ecb.SetComponent(newBoss, bossTransform);
                }

                ecb.DestroyEntity(spawnRequestQuery, EntityQueryCaptureMode.AtPlayback);
            }
            foreach(var (spawnState, spawnData, entity) in SystemAPI.Query<RefRW<EnemySpawnState>, EnemySpawnData>().WithEntityAccess())
            {
                float safeInterval = math.max(0.00001f, spawnData.SpawnInterval);
                // --- 普通敌人生成逻辑 ---
                spawnState.ValueRW.SpawnTimer -= SystemAPI.Time.DeltaTime;
                if (spawnState.ValueRO.SpawnTimer <= 0f)
                {
                    // 2. 用除法直接算出这段“透支”的时间里，应该生成多少个敌人
                    // excessTime 是累积溢出的时间（绝对值）
                    float excessTime = math.abs(spawnState.ValueRO.SpawnTimer);
                    
                    // 生成数量 = 1（触发当前这次的） + 透支时间里能塞下的额外数量
                    int spawnCount = 1 + (int)(excessTime / safeInterval);

                    // 3. 将消耗掉的时间补回，保留多余的小数
                    spawnState.ValueRW.SpawnTimer += spawnCount * safeInterval;

                    var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
                    var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

                    for (int i = 0; i < spawnCount; i++)
                    {
                        var newEnemy = ecb.Instantiate(spawnData.EnemyPrefab);
                        
                        // 每个敌人的生成角度依然需要重新随机
                        var spawnAngle = spawnState.ValueRW.Random.NextFloat(0f, math.TAU);
                        var spawnPoint = new float3
                        {
                            x = math.sin(spawnAngle),
                            y = math.cos(spawnAngle),
                            z = 0f
                        };
                        spawnPoint *= spawnData.SpawnDistance;
                        spawnPoint += playerPosition;

                        ecb.SetComponent(newEnemy, LocalTransform.FromPosition(spawnPoint));
                    }
                }
            }
        }
    }
}
