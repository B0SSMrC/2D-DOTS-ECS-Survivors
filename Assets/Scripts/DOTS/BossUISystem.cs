using Unity.Entities;
using Unity.Collections;

namespace Survivors
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct BossUISystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // 1. 创建一个查询，寻找当前场景中所有的 BOSS
            var bossQuery = SystemAPI.QueryBuilder()
                .WithAll<BossTag, CharacterCurrentHitPoints, CharacterMaxHitPoints>()
                .Build();

            // 2. 如果没有找到任何 BOSS 实体
            if (bossQuery.IsEmpty)
            {
                if (BossUIController.Instance != null)
                {
                    BossUIController.Instance.SetVisibility(false);
                }
                return; // 直接返回，不再执行后续更新逻辑
            }

            // 3. 如果找到了 BOSS，确保 UI 开启并更新数据
            // 获取第一个 BOSS 实体的数据
            var bossEntity = bossQuery.GetSingletonEntity();
            var hp = SystemAPI.GetComponent<CharacterCurrentHitPoints>(bossEntity);
            var maxHp = SystemAPI.GetComponent<CharacterMaxHitPoints>(bossEntity);

            if (BossUIController.Instance != null)
            {
                BossUIController.Instance.SetVisibility(true);
                BossUIController.Instance.UpdateBossUI("ReaperAlien", hp.Value, maxHp.Value);
            }
        }
    }
}