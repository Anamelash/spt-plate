using System;
using UnityEngine;

/// <summary>
/// Stub of the EFT PreviewPivot component (global namespace, no asmdef — compiled
/// into the project's Assembly-CSharp). When the bundle is loaded in game, Unity
/// binds the serialized component to the real EFT class by (assembly, namespace,
/// class). Without it the icon generator crashes with an NRE: GClass926.RenderModel
/// reads PreviewPivot.Icon.overrideIcon with no null check. Fields match the game 1:1.
/// </summary>
public class PreviewPivot : MonoBehaviour
{
    public Vector3 pivotPosition;
    public Quaternion pivotRotation = Quaternion.identity;
    public Vector3 scale = Vector3.one;
    public Vector3 SpawnPosition;
    public IconSettings Icon = new IconSettings();

    [Serializable]
    public class IconSettings
    {
        public Vector3 position;
        public bool hasOffset;
        public Quaternion rotation = Quaternion.identity;
        public float boundsScale = 1f;
        public float perspective;
        public bool orthographic = true;
        public float orthographicSize;
        public Sprite overrideIcon;
    }
}
