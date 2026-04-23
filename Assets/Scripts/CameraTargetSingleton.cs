using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Survivors
{
    public class CameraTargetSingleton : MonoBehaviour
    {
        public static CameraTargetSingleton Instance;

        public void Awake()
        {
            if(Instance != null)
            {
                Debug.LogWarning("multiple instance detected. destroying new instance", Instance);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }
    }
}
