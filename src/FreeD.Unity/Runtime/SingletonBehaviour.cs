using UnityEngine;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>
    /// メインスレッドからだけ呼ぶ。派生クラスが Awake か OnDestroy を持つときは、override して base を呼ぶ。
    /// Instance は、なければ作るので、OnDisable や OnDestroy では呼ばない (シーンを閉じるときに、作ってしまう)。
    /// </summary>
    public abstract class SingletonBehaviour<T> : MonoBehaviour where T : SingletonBehaviour<T>
    {
        private static T instance;

        /// <summary>登録する側の OnEnable が、Awake より先に走っても使えるよう、なければ作る。作ると、Awake と OnEnable まで済ませてから返る。</summary>
        public static T Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                instance = FindExistingInstance();
                if (instance == null)
                {
                    instance = new GameObject(typeof(T).Name).AddComponent<T>();
                }

                return instance;
            }
        }

        /// <summary>重複して壊される予定のものにも、壊れるまでは OnEnable が走るので、派生クラスが守るのに使う。</summary>
        protected bool IsSingletonInstance
        {
            get
            {
                return instance == this;
            }
        }

        private static T FindExistingInstance()
        {
#if UNITY_6000_5_OR_NEWER
            var found = FindObjectsByType<T>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            var found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<T>(true);
#endif
            return found.Length > 0 ? found[0] : null;
        }

        protected virtual void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("FreeD: " + typeof(T).Name + " が複数ある。あとのものを壊す", this);
                Destroy(this);
                return;
            }

            instance = (T)this;

            // DontDestroyOnLoad は、ルートの GameObject にしか効かない。
            if (transform.parent != null)
            {
                Debug.LogWarning("FreeD: " + typeof(T).Name + " をルートの GameObject に置かないと、シーンの切り替えで壊れる", this);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
