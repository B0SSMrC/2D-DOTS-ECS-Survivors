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

                // --- 普通敌人生成逻辑 ---
                spawnState.ValueRW.SpawnTimer -= SystemAPI.Time.DeltaTime;
                while(spawnState.ValueRO.SpawnTimer <= 0f) 
                {
                    spawnState.ValueRW.SpawnTimer = spawnData.SpawnInterval;
                    var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
                    var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
                    var newEnemy = ecb.Instantiate(spawnData.EnemyPrefab);
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
