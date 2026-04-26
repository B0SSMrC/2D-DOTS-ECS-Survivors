using Unity.Entities;

namespace Survivors
{
    // 放在表现层系统组，在逻辑计算完毕后更新 UI
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct EnemyCountSystem : ISystem
    {
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            // 创建一个查询，寻找所有带有 EnemyTag 的实体
            _enemyQuery = SystemAPI.QueryBuilder().WithAll<EnemyTag>().Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (EnemyCountUIController.Instance != null)
            {
                
                // 它不会去遍历每一个敌人，而是直接把匹配的内存块 (Chunk) 里的计数相加
                int currentEnemyCount = _enemyQuery.CalculateEntityCount();
                
                EnemyCountUIController.Instance.UpdateEnemyCount(currentEnemyCount);
            }
        }
    }
}