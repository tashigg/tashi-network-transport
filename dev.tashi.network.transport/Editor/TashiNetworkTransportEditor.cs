// https://docs.unity3d.com/Manual/editor-CustomEditors.html

using Tashi.ConsensusEngine;
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
        private SerializedProperty m_NetworkMode;
        private SerializedProperty m_TashiRelayBaseUrl;
        private SerializedProperty m_TashiRelayApiKey;
        private bool m_ShowApiKey;

        void OnEnable()
        {
            m_Config = serializedObject.FindProperty(nameof(TashiNetworkTransport.Config));
            m_SyncInterval = m_Config.FindPropertyRelative("SyncInterval");
            m_BindPort = m_Config.FindPropertyRelative("BindPort");
            m_NetworkMode = m_Config.FindPropertyRelative("NetworkMode");
            m_TashiRelayBaseUrl = m_Config.FindPropertyRelative("TashiRelayBaseUrl");
            m_TashiRelayApiKey = m_Config.FindPropertyRelative("TashiRelayApiKey");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_SyncInterval, new GUIContent("Sync Interval (ms): "));
            EditorGUILayout.PropertyField(m_BindPort);
            m_NetworkMode.enumValueIndex = (int)(TashiNetworkMode)EditorGUILayout.EnumPopup((TashiNetworkMode)m_NetworkMode.enumValueIndex);
            if (m_NetworkMode.enumValueIndex == (int)TashiNetworkMode.TashiRelay)
            {
                EditorGUILayout.PropertyField(m_TashiRelayBaseUrl);
                m_ShowApiKey = EditorGUILayout.Toggle("Show API Key", m_ShowApiKey);
                if (m_ShowApiKey)
                {
                    EditorGUILayout.PropertyField(m_TashiRelayApiKey, new GUIContent("API Key"));
                }
                else
                {
                    m_TashiRelayApiKey.stringValue = EditorGUILayout.PasswordField("API Key", m_TashiRelayApiKey.stringValue);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
