using UnityEngine;
using TMPro;
using UnityEngine.Profiling;

namespace Survivors
{
    public class PerformanceStatsController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _statsText;

        [Header("Settings")]
        [SerializeField] private float _updateInterval = 0.3f; // 每0.3秒刷新一次，防止数字跳动太快看不清

        private float _timeAccumulator = 0f;
        private int _frameCount = 0;

        private void Update()
        {
            _timeAccumulator += Time.unscaledDeltaTime;
            _frameCount++;

            if (_timeAccumulator >= _updateInterval)
            {
                UpdateStatsDisplay();
                _timeAccumulator = 0f;
                _frameCount = 0;
            }
        }

        private void UpdateStatsDisplay()
        {
            if (_statsText == null) return;

            // 计算 FPS 和 帧耗时
            float fps = _frameCount / _updateInterval;
            float frameTimeMs = (_updateInterval / _frameCount) * 1000f;

            // 获取内存分配 (转为 MB)
            long totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
            long totalReservedMemory = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);

            // 格式化输出文本
            _statsText.text = 
                $"<color=#00FF00><b>FPS:</b> {fps:F1}</color>\n" +
                $"<b>Frame Time:</b> {frameTimeMs:F2} ms\n" +
                $"<b>Memory (Alloc):</b> {totalAllocatedMemory} MB\n" +
                $"<b>Memory (Reserved):</b> {totalReservedMemory} MB";
        }
    }
}