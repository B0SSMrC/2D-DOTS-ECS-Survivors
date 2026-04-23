using UnityEngine;
using Unity.Entities;

namespace Survivors
{
    public class DebugWindowManager : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("把包含性能文字和滑动条的整个 Debug 面板拖到这里")]
        [SerializeField] private GameObject _debugPanel;

        private bool _isPanelActive = false;

        private void Start()
        {
            // 游戏开始时，默认隐藏调试面板
            if (_debugPanel != null)
            {
                _debugPanel.SetActive(false);
            }
        }

        private void Update()
        {
            // 监听 Tab 键
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleDebugPanel();
            }
        }

        private void ToggleDebugPanel()
        {
            if (_debugPanel == null) return;

            // 切换状态
            _isPanelActive = !_isPanelActive;
            _debugPanel.SetActive(_isPanelActive);

            // 暂停或恢复游戏 (暂停 ECS 世界的运算)
            SetEcsEnabled(!_isPanelActive);
        }

        // 核心：利用开启/关闭 ECS 的系统组来实现真正的“物理和逻辑暂停”
        private void SetEcsEnabled(bool shouldEnable)
        {
            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld == null) return;

            // 暂停/恢复 初始化阶段（生成敌人、子弹等）
            var initializationSystemGroup = defaultWorld.GetExistingSystemManaged<InitializationSystemGroup>();
            if (initializationSystemGroup != null)
            {
                initializationSystemGroup.Enabled = shouldEnable;
            }

            // 暂停/恢复 模拟阶段（移动、碰撞计算等）
            var simulationSystemGroup = defaultWorld.GetExistingSystemManaged<SimulationSystemGroup>();
            if (simulationSystemGroup != null)
            {
                simulationSystemGroup.Enabled = shouldEnable;
            }
        }
    }
}