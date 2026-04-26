using UnityEngine;
using TMPro;

namespace Survivors
{
    public class EnemyCountUIController : MonoBehaviour
    {
        public static EnemyCountUIController Instance;

        [SerializeField] private TextMeshProUGUI _enemyCountText;

        private void Awake()
        {
            Instance = this;
        }

        public void UpdateEnemyCount(int count)
        {
            if (_enemyCountText != null)
            {
                
                _enemyCountText.text = $"EnemyCounts: {count:N0}"; 
            }
        }
    }
}