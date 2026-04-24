using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Physics;
using TMG.Survivors;
using UnityEngine.UI;
using Unity.Burst;
using Unity.Collections;



namespace Survivors
{

    public struct InitializeCharacterFlag : IComponentData, IEnableableComponent
    {
        
    }

    public struct PlayerTag : IComponentData {}

    public struct CameraTarget : IComponentData
    {
        public UnityObjectRef<Transform> CameraTransform;
    }

    public struct InitializeCameraTargetTag : IComponentData {}

    [MaterialProperty("_AnimationIndex")]
    public struct AnimationIndexOverride : IComponentData
    {
        public float Value;
    }

    public enum PlayerAnimationIndex : byte
    {
        Movement = 0,
        Idle = 1,

        None = byte.MaxValue
    }

    public struct PlayerAttackData : IComponentData
    {
        public Entity AttackPrefab;
        public float CooldownTime;
        public float3 DetectionSize;
        public CollisionFilter collisionFilter;
        public int MaxTargetCount;
    }

    public struct PlayerCooldownExpirationTimestamp : IComponentData
    {
        public double Value;
    }

    public struct GemsCollectedCount : IComponentData
    {
        public int Value;
    }

    public struct UpdateGemUIFlag : IComponentData, IEnableableComponent {}

    public struct PlayerWorldUI : ICleanupComponentData
    {
        public UnityObjectRef<Transform> CanvasTransform;
        public UnityObjectRef<Slider> HealthBarSlider;
    }

    public struct PlayerWorldUIPrefab : IComponentData
    {
        public UnityObjectRef<GameObject> Value;
    }

    public struct PlayerPickupRange : IComponentData
    {
        public float Value;
    }

    public class PlayerAuthoring : MonoBehaviour
    {
        public GameObject AttackPrefab;
        public float CooldownTime;
        public float DetectionSize;
        public GameObject WorldUIPrefab;

        public int MaxTargetCount = 3;

        public float PickupRange;
        private class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<InitializeCharacterFlag>(entity);
                AddComponent<PlayerTag>(entity);
                AddComponent<InitializeCameraTargetTag>(entity);
                AddComponent<CameraTarget>(entity);
                AddComponent<AnimationIndexOverride>(entity);

                var enemyLayer = LayerMask.NameToLayer("Enemy");
                var enemyLayerMask = (uint)math.pow(2, enemyLayer);
                var attackCollisionFilter = new CollisionFilter
                {
                    BelongsTo = uint.MaxValue,
                    CollidesWith = enemyLayerMask
                };

                AddComponent(entity, new PlayerAttackData
                {
                    AttackPrefab = GetEntity(authoring.AttackPrefab, TransformUsageFlags.Dynamic),
                    CooldownTime = authoring.CooldownTime,
                    DetectionSize = new float3(authoring.DetectionSize),
                    collisionFilter = attackCollisionFilter,
                    MaxTargetCount = authoring.MaxTargetCount
                });
                AddComponent<PlayerCooldownExpirationTimestamp>(entity);
                AddComponent<GemsCollectedCount>(entity);
                AddComponent<UpdateGemUIFlag>(entity);
                AddComponent(entity, new PlayerWorldUIPrefab
                {
                    Value = authoring.WorldUIPrefab
                });
                AddComponent(entity, new PlayerPickupRange { Value = authoring.PickupRange });
            }
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct CameraInitializationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InitializeCameraTargetTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if(CameraTargetSingleton.Instance == null) return;
            var cameraTargetTransform = CameraTargetSingleton.Instance.transform;

            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach(var (cameraTarget, entity) in SystemAPI.Query<RefRW<CameraTarget>>().WithAll<InitializeCameraTargetTag, PlayerTag>()
            .WithEntityAccess())
            {
                cameraTarget.ValueRW.CameraTransform = cameraTargetTransform;
                ecb.RemoveComponent<InitializeCameraTargetTag>(entity);
            }

            ecb.Playback(state.EntityManager);
        }
    }

    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct MoveCameraSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach(var (transform, cameraTarget) in SystemAPI.Query<LocalToWorld, CameraTarget>()
            .WithAll<PlayerTag>().WithNone<InitializeCameraTargetTag>())
            {
                cameraTarget.CameraTransform.Value.position = transform.Position;
            }
        }
    }

    public partial class PlayerInputSystem : SystemBase
    {
        private SurvivorsInput _input;

        protected override void OnCreate()
        {
            _input = new SurvivorsInput();
            _input.Enable();
        }
        protected override void OnUpdate()
        {
            var currentInput = (float2)_input.Player.Move.ReadValue<Vector2>();
            foreach(var direction in SystemAPI.Query<RefRW<CharacterMoveDirection>>().WithAll<PlayerTag>())
            {
                direction.ValueRW.Value = currentInput;
            }
        }
    }

    public partial struct PlayerAttackSystem : ISystem
    {
        // 👇 定义一个内部结构体，用于记录敌人信息并支持基于距离的排序 (Burst 完全兼容)
        private struct TargetInfo : System.IComparable<TargetInfo>
        {
            public float DistanceSq;
            public float3 Position;

            public int CompareTo(TargetInfo other)
            {
                return DistanceSq.CompareTo(other.DistanceSq);
            }
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        // 建议加上 BurstCompile 提升性能
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var elapsedTime = SystemAPI.Time.ElapsedTime;

            var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);
            var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            
            foreach(var (expirationTimestamp, attackData, transform) in SystemAPI
            .Query<RefRW<PlayerCooldownExpirationTimestamp>, PlayerAttackData, LocalTransform>())
            {
                if(expirationTimestamp.ValueRO.Value > elapsedTime) continue;

                var spawnPosition = transform.Position;
                var minDetectPosition = spawnPosition - attackData.DetectionSize;
                var maxDetectPosition = spawnPosition + attackData.DetectionSize;

                var aabbInput = new OverlapAabbInput
                {
                    Aabb = new Aabb { Min = minDetectPosition, Max = maxDetectPosition },
                    Filter = attackData.collisionFilter
                };

                var overlapHits = new NativeList<int>(state.WorldUpdateAllocator);
                if(!physicsWorldSingleton.OverlapAabb(aabbInput, ref overlapHits))
                {
                    continue; // 没扫描到任何敌人，跳过
                }

                // 👇 核心重构：记录所有扫描到的敌人距离并排序
                // 使用 WorldUpdateAllocator 申请临时内存，Unity 会在这一帧结束时自动帮我们回收，绝不会内存泄漏
                var targets = new NativeList<TargetInfo>(overlapHits.Length, state.WorldUpdateAllocator);
                
                foreach(var overlapHit in overlapHits)
                {
                    var curEnemyPosition = physicsWorldSingleton.Bodies[overlapHit].WorldFromBody.pos;
                    targets.Add(new TargetInfo 
                    { 
                        DistanceSq = math.distancesq(spawnPosition.xy, curEnemyPosition.xy), 
                        Position = curEnemyPosition 
                    });
                }

                // 按距离从小到大排序 (DOTS 原生高性能排序)
                targets.Sort();

                // 确定最终要发射的子弹数量（不能超过设定上限，也不能超过实际找到的敌人总数）
                int fireCount = math.min(attackData.MaxTargetCount, targets.Length);

                // 循环向最近的 N 个敌人发射子弹
                for(int i = 0; i < fireCount; i++)
                {
                    var targetPos = targets[i].Position;
                    var vectorToEnemy = targetPos - spawnPosition;
                    var angleToClosestEnemy = math.atan2(vectorToEnemy.y, vectorToEnemy.x);
                    var spawnOrientation = quaternion.Euler(0f, 0f, angleToClosestEnemy);

                    var newAttack = ecb.Instantiate(attackData.AttackPrefab);
                    ecb.SetComponent(newAttack, LocalTransform.FromPositionRotation(spawnPosition, spawnOrientation));
                }

                expirationTimestamp.ValueRW.Value = elapsedTime + attackData.CooldownTime;
            }
        }
    }

    public partial struct UpdateGemUISystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach(var (gemsCount, shouldUpdateUI) in SystemAPI.Query<GemsCollectedCount, EnabledRefRW<UpdateGemUIFlag>>())
            {
                GameUIController.Instance.UpdateGemsCollectedText(gemsCount.Value);
                shouldUpdateUI.ValueRW = false;
            }
        }
    }

    public partial struct PlayerWorldUISystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach(var (uiPrefab, entity) in SystemAPI.Query<PlayerWorldUIPrefab>()
            .WithNone<PlayerWorldUI>().WithEntityAccess())
            {
                var newWorldUI = Object.Instantiate(uiPrefab.Value.Value);
                ecb.AddComponent(entity, new PlayerWorldUI
                {
                    CanvasTransform = newWorldUI.transform,
                    HealthBarSlider = newWorldUI.GetComponentInChildren<Slider>()
                });
            }

            foreach(var (transform, worldUI, currentHitPoints, maxHitPoints) in SystemAPI
            .Query<LocalToWorld, PlayerWorldUI, CharacterCurrentHitPoints, CharacterMaxHitPoints>())
            {
                worldUI.CanvasTransform.Value.position = transform.Position;
                var healthValue = (float)currentHitPoints.Value / maxHitPoints.Value;
                worldUI.HealthBarSlider.Value.value = healthValue;
            }

            foreach(var (worldUI, entity) in SystemAPI.Query<PlayerWorldUI>()
            .WithNone<LocalToWorld>().WithEntityAccess())
            {
                if(worldUI.CanvasTransform.Value != null)
                {
                    Object.Destroy(worldUI.CanvasTransform.Value.gameObject);
                }

                ecb.RemoveComponent<PlayerWorldUI>(entity);
            }

            ecb.Playback(state.EntityManager);
        }
    }
}
