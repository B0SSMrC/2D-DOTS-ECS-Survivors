using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Burst;
using Unity.Rendering;
using Unity.Transforms;




namespace Survivors
{
    public struct CharacterMoveDirection : IComponentData
    {
        public float2 Value;
    }

    public struct CharacterMoveSpeed : IComponentData
    {
        public float Value;
    }

    [MaterialProperty("_FacingDirection")]
    public struct FacingDirectionOverride : IComponentData
    {
        public float Value;
    }

    public struct CharacterMaxHitPoints : IComponentData
    {
        public int Value;
    }

    public struct CharacterCurrentHitPoints : IComponentData
    {
        public int Value;
    }

    public struct DamageThisFrame : IBufferElementData
    {
        public int Value;
    }

    public class CharacterAuthoring : MonoBehaviour
    {
        public float MoveSpeed;
        public int HitPoints;
        private class Baker : Baker<CharacterAuthoring>
        {
            public override void Bake(CharacterAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<CharacterMoveDirection>(entity);
                AddComponent(entity, new CharacterMoveSpeed
                {
                    Value = authoring.MoveSpeed
                });
                AddComponent(entity, new FacingDirectionOverride
                {
                    Value = 1
                });
                AddComponent(entity, new CharacterMaxHitPoints
                {
                    Value = authoring.HitPoints
                });
                AddComponent(entity, new CharacterCurrentHitPoints
                {
                    Value = authoring.HitPoints
                });
                AddBuffer<DamageThisFrame>(entity);
                AddComponent<DestroyEntityFlag>(entity);
                SetComponentEnabled<DestroyEntityFlag>(entity, false);
            }
        }
    
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct CharacterInitializeSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach(var (mass, shouldInitialize) in SystemAPI.Query<RefRW<PhysicsMass>, EnabledRefRW<InitializeCharacterFlag>>())
            {
                mass.ValueRW.InverseInertia = float3.zero;
                shouldInitialize.ValueRW = false;
            }
        }
    }

    public partial struct CharacterMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            foreach(var (velocity, facingDirection, direction, speed, transform, entity) in SystemAPI.Query<
                RefRW<PhysicsVelocity>, 
                RefRW<FacingDirectionOverride>, 
                CharacterMoveDirection, 
                CharacterMoveSpeed, 
                RefRW<LocalTransform>>().WithEntityAccess())
            {
                var moveStep2d = direction.Value * speed.Value;
                
                // 给物理引擎线速度赋值时，明确 Z 轴速度为 0
                velocity.ValueRW.Linear = new float3(moveStep2d.x, moveStep2d.y, 0f);
                
                // 强行把所有角色的 Z 轴坐标归 0
                transform.ValueRW.Position.z = 0f;

                // 处理人物翻转
                if(math.abs(moveStep2d.x) > 0.15f)
                {
                    facingDirection.ValueRW.Value = math.sign(moveStep2d.x);
                }

                //设置玩家动画
                if (SystemAPI.HasComponent<PlayerTag>(entity))
                {
                    var animationOverride = SystemAPI.GetComponentRW<AnimationIndexOverride>(entity);
                    var animationType = math.lengthsq(moveStep2d) > float.Epsilon ? PlayerAnimationIndex.Movement : PlayerAnimationIndex.Idle;
                    animationOverride.ValueRW.Value = (float) animationType;
                }
            }
        }
    }

    public partial struct GlobalTimeUpdateSystem : ISystem
    {
        private static int _globalTimeShaderPropertyID;

        public void OnCreate(ref SystemState state)
        {
            _globalTimeShaderPropertyID = Shader.PropertyToID("_GlobalTime");

        }

        public void OnUpdate(ref SystemState state)
        {
            Shader.SetGlobalFloat(_globalTimeShaderPropertyID,(float)SystemAPI.Time.ElapsedTime);
        }
    }


    public partial struct ProcessDamageThisFrame : ISystem
    {   
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach(var (hitPoints, damageThisFrame, entity) in SystemAPI
            .Query<RefRW<CharacterCurrentHitPoints>, DynamicBuffer<DamageThisFrame>>()
            .WithPresent<DestroyEntityFlag>().WithEntityAccess())
            {
                if(damageThisFrame.IsEmpty) continue;
                foreach(var damage in damageThisFrame)
                {
                    hitPoints.ValueRW.Value -= damage.Value;
                }

                damageThisFrame.Clear();

                if(hitPoints.ValueRO.Value <= 0)
                {
                    SystemAPI.SetComponentEnabled<DestroyEntityFlag>(entity, true);
                }
            }
        }
    }
}