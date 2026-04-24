using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

namespace Survivors
{
    public class SpawnBossButtonController : MonoBehaviour
    {
        [SerializeField] private Button _spawnButton;

        private EntityManager _entityManager;

        private void Start()
        {
            // 获取 ECS 管理器
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            if (_spawnButton != null)
            {
                _spawnButton.onClick.AddListener(OnSpawnButtonClick);
            }
        }

        private void OnSpawnButtonClick()
        {
            // 点击按钮时，在 ECS 世界中创建一个带有 SpawnBossRequest 组件的实体
            // 系统检测到这个实体后就会执行生成逻辑
            Entity requestEntity = _entityManager.CreateEntity(typeof(SpawnBossRequest));
            
            Debug.Log("已发送生成 BOSS 请求！");
        }

        private void OnDestroy()
        {
            if (_spawnButton != null)
            {
                _spawnButton.onClick.RemoveListener(OnSpawnButtonClick);
            }
        }
    }
}