using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Survivors
{
    public struct EnemySpawnData : IComponentData
    {
        public Entity EnemyPrefab;
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
                    SpawnInterval = authoring.SpawnInterval,
                    SpawnDistance = authoring.SpawnDistance
                });
                AddComponent(entity, new EnemySpawnState
                {
                    SpawnTimer = 0f,
                    Random = Random.CreateFromIndex(authoring.RandomSeed)
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

            var playEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
            var playerPosition = SystemAPI.GetComponent<LocalTransform>(playEntity).Position;
            foreach(var (spawnState, spawnData) in SystemAPI.Query<RefRW<EnemySpawnState>, EnemySpawnData>())
            {
                spawnState.ValueRW.SpawnTimer -= SystemAPI.Time.DeltaTime;
                if(spawnState.ValueRO.SpawnTimer > 0f) continue;
                spawnState.ValueRW.SpawnTimer = spawnData.SpawnInterval;

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
