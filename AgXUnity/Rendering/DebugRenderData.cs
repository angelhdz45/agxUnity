﻿using UnityEngine;

namespace AgXUnity.Rendering
{
  /// <summary>
  /// Base class for debug render data managed by DebugRenderManager.
  /// </summary>
  [GenerateCustomEditor]
  public abstract class DebugRenderData : ScriptComponent
  {
    /// <summary>
    /// Loaded prefab or game object with mesh renderer etc.
    /// </summary>
    /// <remarks>
    /// This Node object should not be saved. DebugRenderManager handles
    /// creation and deletion.
    /// </remarks>
    public GameObject Node { get; set; }

    /// <summary>
    /// Typename used to load prefab from resources. In general
    /// "Resources/Debug/(ret GetTypeName)Renderer".
    /// </summary>
    /// <returns>Name of the type of the debug rendered object.</returns>
    public abstract string GetTypeName();

    /// <summary>
    /// Callback from the DebugRenderManager each editor "Update" or
    /// in "LateUpdate" when the application is running.
    /// 
    /// Synchronize the transform of the "Node" game object - create the "Node"
    /// instance if it's null.
    /// </summary>
    public abstract void Synchronize( DebugRenderManager manager );

    /// <summary>
    /// Name of the prefab in the Debug folder under Resources.
    /// </summary>
    [HideInInspector]
    public string PrefabName { get { return GetPrefabName( GetTypeName() ); } }

    /// <summary>
    /// Name of the prefab in the Debug folder under Resources.
    /// </summary>
    /// <param name="typeName">Type name.</param>
    /// <returns>Name and path to the prefab.</returns>
    public static string GetPrefabName( string typeName )
    {
      return "Debug." + typeName + "Renderer";
    }
  }
}
