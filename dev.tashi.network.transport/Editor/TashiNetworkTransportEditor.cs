// https://docs.unity3d.com/Manual/editor-CustomEditors.html

using Tashi.NetworkTransport;
using UnityEditor;
using UnityEngine;

namespace Tashi.NetworkTransport
{
    [CustomEditor(typeof(TashiNetworkTransport), true)]
    [CanEditMultipleObjects]
    public class TashiNetworkTransportEditor : Editor
    {
        private SerializedProperty m_Config;
        private SerializedProperty m_BindPort;
        private SerializedProperty m_SyncInterval;

        void OnEnable()
        {
            m_Config = serializedObject.FindProperty(nameof(TashiNetworkTransport.Config));
            m_SyncInterval = m_Config.FindPropertyRelative("SyncInterval");
            m_BindPort = m_Config.FindPropertyRelative("BindPort");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_SyncInterval, new GUIContent("Sync Interval (ms): "));
            EditorGUILayout.PropertyField(m_BindPort);
            serializedObject.ApplyModifiedProperties();
        }
    }
}