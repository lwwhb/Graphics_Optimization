using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Unity.Testing.VisualEffectGraph
{
    public class OutputEventTest : MonoBehaviour
    {
        static private int s_outputEventNameId = Shader.PropertyToID("Test_Output_Event");
        static private int s_positionNameId = Shader.PropertyToID("position");
        static private int s_colorNameId = Shader.PropertyToID("color");
        static private int s_customEmissionNameId = Shader.PropertyToID("custom_emission");

        public GameObject m_ObjectReference;

        private VisualEffect m_vfx;
        private List<VFXEventAttribute> m_cachedVFXEventAttribute;
        private List<VFXEventAttribute> m_currentVFXEventAttribute;

        void Start()
        {
            m_vfx = GetComponent<VisualEffect>();

            if (m_vfx)
            {
                m_cachedVFXEventAttribute = new List<VFXEventAttribute>(3);
                m_currentVFXEventAttribute = new List<VFXEventAttribute>(3);
                for (int i = 0; i<3; ++i)
                    m_cachedVFXEventAttribute.Add(m_vfx.CreateVFXEventAttribute());
            }
        }

        void Update()
        {
            if (m_vfx == null || m_ObjectReference == null)
                return;

            m_currentVFXEventAttribute.Clear();
            m_currentVFXEventAttribute.AddRange(m_cachedVFXEventAttribute);
            m_vfx.GetOutputEventAttribute(s_outputEventNameId, m_currentVFXEventAttribute);

            foreach (var eventAttribute in m_currentVFXEventAttribute)
            {
                var newObject = GameObject.Instantiate(m_ObjectReference);
                newObject.GetComponent<Transform>().position = eventAttribute.GetVector3(s_positionNameId);
                var renderer = newObject.GetComponent<Renderer>();
                var currentMaterial = renderer.material;
                currentMaterial = new Material(currentMaterial);
                currentMaterial.SetVector(s_customEmissionNameId, eventAttribute.GetVector3(s_colorNameId));
                renderer.material = currentMaterial;
                newObject.SetActive(true);
            }
        }
    }
}
