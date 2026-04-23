using UnityEngine;
using Unity.Entities;
using TMPro;


namespace Survivors
{
    public class DebugTweakerController : MonoBehaviour
    {
        [Header("Player Cooldown Setup")]
        [SerializeField] private TMP_InputField _cooldownInputField;
        [SerializeField] private TextMeshProUGUI _cooldownValueText; // 可选：用于在旁边显示确认后的值

        [Header("Enemy Spawn Interval Setup")]
        [SerializeField] private TMP_InputField _spawnIntervalInputField;
        [SerializeField] private TextMeshProUGUI _spawnIntervalValueText; 

        private EntityManager _entityManager;
        private EntityQuery _playerAttackDataQuery;
        private EntityQuery _enemySpawnDataQuery;

        private void Start()
        {
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            // 创建查询器
            _playerAttackDataQuery = _entityManager.CreateEntityQuery(typeof(PlayerTag), typeof(PlayerAttackData));
            _enemySpawnDataQuery = _entityManager.CreateEntityQuery(typeof(EnemySpawnData));

            // 监听输入框的 OnEndEdit 事件 (当用户按下回车键，或者鼠标点到其他地方时触发)
            _cooldownInputField.onEndEdit.AddListener(OnCooldownInputSubmitted);
            _spawnIntervalInputField.onEndEdit.AddListener(OnSpawnIntervalInputSubmitted);

            // 面板打开时，先去 ECS 世界读取当前最新的真实数据，填充到输入框里
            SyncDataToUI();
        }

        private void OnDestroy()
        {
            _cooldownInputField.onEndEdit.RemoveListener(OnCooldownInputSubmitted);
            _spawnIntervalInputField.onEndEdit.RemoveListener(OnSpawnIntervalInputSubmitted);
        }

        private void OnEnable()
        {
            // 每次按 Tab 键唤出面板时，刷新一次显示的数据
            if (_entityManager != default) 
            {
                SyncDataToUI();
            }
        }

        // --- 核心逻辑 ---

        private void SyncDataToUI()
        {
            if (_playerAttackDataQuery.HasSingleton<PlayerAttackData>())
            {
                var attackData = _playerAttackDataQuery.GetSingleton<PlayerAttackData>();
                _cooldownInputField.text = attackData.CooldownTime.ToString("F5"); // 保留6位小数
                if (_cooldownValueText != null) _cooldownValueText.text = $"CooldownTime: {attackData.CooldownTime:F3}s";
            }

            if (_enemySpawnDataQuery.HasSingleton<EnemySpawnData>())
            {
                var spawnData = _enemySpawnDataQuery.GetSingleton<EnemySpawnData>();
                _spawnIntervalInputField.text = spawnData.SpawnInterval.ToString("F5");
                if (_spawnIntervalValueText != null) _spawnIntervalValueText.text = $"SpawnInterval: {spawnData.SpawnInterval:F3}s";
            }
        }

        private void OnCooldownInputSubmitted(string inputString)
        {
            // 尝试将字符串解析为浮点数
            if (float.TryParse(inputString, out float newValue))
            {
                // 安全限制：最小冷却时间不能低于 0.000001秒，否则一帧内发射太多子弹会导致内存爆炸
                newValue = Mathf.Max(0.000001f, newValue);
                
                // 写回 ECS 世界
                if (_playerAttackDataQuery.HasSingleton<PlayerAttackData>())
                {
                    var attackData = _playerAttackDataQuery.GetSingleton<PlayerAttackData>();
                    attackData.CooldownTime = newValue;
                    _playerAttackDataQuery.SetSingleton(attackData);
                }

                // 刷新 UI 显示
                SyncDataToUI(); 
            }
            else
            {
                // 如果用户乱输入了字母，恢复原来的真实数值
                SyncDataToUI();
            }
        }

        private void OnSpawnIntervalInputSubmitted(string inputString)
        {
            if (float.TryParse(inputString, out float newValue))
            {
                // 安全限制：生成间隔不能太小，防止游戏卡死
                newValue = Mathf.Max(0.000001f, newValue);

                // 写回 ECS 世界
                if (_enemySpawnDataQuery.HasSingleton<EnemySpawnData>())
                {
                    var spawnData = _enemySpawnDataQuery.GetSingleton<EnemySpawnData>();
                    spawnData.SpawnInterval = newValue;
                    _enemySpawnDataQuery.SetSingleton(spawnData);
                }

                SyncDataToUI();
            }
            else
            {
                SyncDataToUI();
            }
        }
    }
}