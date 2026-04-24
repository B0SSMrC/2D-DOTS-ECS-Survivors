using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace Survivors
{
    public class BossUIController : MonoBehaviour
    {
        public static BossUIController Instance;

        [Header("UI Components")]
        [SerializeField] private GameObject _rootPanel; // 整个血条面板
        [SerializeField] private Image _topBarImage;
        [SerializeField] private Image _bottomBarImage;
        [SerializeField] private TextMeshProUGUI _bossNameText;
        [SerializeField] private TextMeshProUGUI _layerCountText;

        [Header("Settings")]
        [SerializeField] private float _hpPerLayer = 1000f;
        [SerializeField] private List<Color> _layerColors = new List<Color> { Color.red, Color.yellow, Color.green, Color.cyan, Color.blue, Color.magenta };

        private void Awake()
        {
            Instance = this;
            SetVisibility(false); // 初始状态隐藏
        }

        // 新增：专门控制面板显示或隐藏的方法
        public void SetVisibility(bool visible)
        {
            if (_rootPanel.activeSelf != visible)
            {
                _rootPanel.SetActive(visible);
            }
        }

        public void UpdateBossUI(string name, int currentHP, int maxHP)
        {
            // 移除之前的 SetActive(true)，将显示控制交给 System
            _bossNameText.text = name;

            int currentLayer = Mathf.CeilToInt(currentHP / _hpPerLayer);
            _layerCountText.text = $"x{currentLayer}";

            if (currentLayer <= 0)
            {
                _topBarImage.fillAmount = 0;
                return;
            }

            float hpInCurrentLayer = currentHP - ((currentLayer - 1) * _hpPerLayer);
            float fillAmount = hpInCurrentLayer / _hpPerLayer;

            Color topColor = _layerColors[(currentLayer - 1) % _layerColors.Count];
            Color bottomColor = currentLayer > 1 ? _layerColors[(currentLayer - 2) % _layerColors.Count] : Color.black;

            _topBarImage.fillAmount = fillAmount;
            _topBarImage.color = topColor;
            _bottomBarImage.color = bottomColor;
        }
    }
}