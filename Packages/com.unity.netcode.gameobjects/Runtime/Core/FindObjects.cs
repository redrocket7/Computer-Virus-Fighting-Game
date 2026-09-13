using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.Netcode
{
    /// <summary>
    /// Helper class to handle FindObjectsByType on Unity 6000.5+
    /// (FindObjectsSortMode / GetInstanceID are obsolete-as-error).
    /// </summary>
    internal static class FindObjects
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] ByType<T>(bool includeInactive = false, bool orderByIdentifier = false) where T : Object
        {
            var inactive = includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude;
            // Unity 6000.5: use unsorted FindObjectsByType only.
            // orderByIdentifier is ignored; NGO only used it for determinism in edge cases.
            return Object.FindObjectsByType<T>(inactive);
        }

        public static IEnumerable<T> FromSceneByType<T>(Scene scene, bool includeInactive) where T : UnityEngine.Component
        {
            return new ObjectsInSceneEnumerator<T>(scene, includeInactive);
        }

        private struct ObjectsInSceneEnumerator<T> : IEnumerable<T>, IEnumerator<T> where T : UnityEngine.Component
        {
            private readonly UnityEngine.GameObject[] m_RootObjects;
            private int m_RootIndex;
            private T[] m_CurrentChildObjects;
            private int m_CurrentChildIndex;
            private readonly bool m_IncludeInactive;

            internal ObjectsInSceneEnumerator(Scene scene, bool includeInactive)
            {
                m_IncludeInactive = includeInactive;
                m_RootObjects = scene.IsValid() ? scene.GetRootGameObjects() : System.Array.Empty<UnityEngine.GameObject>();
                m_RootIndex = 0;
                m_CurrentChildObjects = null;
                m_CurrentChildIndex = 0;
                Current = null;
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                while (m_CurrentChildObjects == null && m_RootIndex < m_RootObjects.Length)
                {
                    m_CurrentChildObjects = m_RootObjects[m_RootIndex].GetComponentsInChildren<T>(m_IncludeInactive);
                    m_RootIndex++;

                    if (m_CurrentChildObjects.Length == 0)
                        m_CurrentChildObjects = null;
                }

                if (m_CurrentChildObjects != null && m_CurrentChildIndex < m_CurrentChildObjects.Length)
                {
                    Current = m_CurrentChildObjects[m_CurrentChildIndex];
                    m_CurrentChildIndex++;

                    if (m_CurrentChildIndex >= m_CurrentChildObjects.Length)
                    {
                        m_CurrentChildIndex = 0;
                        m_CurrentChildObjects = null;
                    }
                    return true;
                }

                Current = null;
                return false;
            }

            public void Reset()
            {
                m_RootIndex = 0;
                m_CurrentChildObjects = null;
                m_CurrentChildIndex = 0;
                Current = null;
            }

            object IEnumerator.Current => Current;
            public T Current { get; private set; }
            public IEnumerator<T> GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
    }
}
