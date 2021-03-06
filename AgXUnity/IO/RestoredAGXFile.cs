﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgXUnity.IO
{
  /// <summary>
  /// Asset types grouped together.
  /// </summary>
  public enum AssetType
  {
    Material,
    RenderMesh,
    CollisionMesh,
    ShapeMaterial,
    ContactMaterial,
    FrictionModel,
    CableProperties,
    NumTypes,
    Unknown
  }

  [Serializable]
  public class GroupPair
  {
    public string First  = string.Empty;
    public string Second = string.Empty;
  }

  public class RestoredAGXFile : ScriptComponent
  {
    [SerializeField]
    private List<GroupPair> m_disabledGroups = new List<GroupPair>();

    [HideInInspector]
    public GroupPair[] DisabledGroups { get { return m_disabledGroups.ToArray(); } }

    public void AddDisabledPair( string group1, string group2 )
    {
      if ( m_disabledGroups.FindIndex( pair => ( pair.First == group1 && pair.Second == group2 ) || ( pair.Second == group1 && pair.First == group2 ) ) >= 0 )
        return;

      m_disabledGroups.Add( new GroupPair() { First = group1, Second = group2 } );
    }
  }
}
