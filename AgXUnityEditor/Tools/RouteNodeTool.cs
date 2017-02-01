﻿using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using AgXUnity;

namespace AgXUnityEditor.Tools
{
  public class RouteNodeTool : Tool
  {
    private Func<RouteNode> m_getSelected = null;
    private Action<RouteNode> m_setSelected = null;
    private Predicate<RouteNode> m_hasNode = null;
    private Func<float> m_radius = null;

    public ScriptComponent Parent { get; private set; }

    public RouteNode Node { get; private set; }

    public FrameTool FrameTool
    {
      get { return GetChild<FrameTool>(); }
    }

    public Utils.VisualPrimitiveSphere Visual { get { return GetOrCreateVisualPrimitive<Utils.VisualPrimitiveSphere>( "cableRouteNode" ); } }

    public bool Selected
    {
      get { return m_getSelected() == Node; }
      set { m_setSelected( value ? Node : null ); }
    }

    public RouteNodeTool( RouteNode node,
                          ScriptComponent parent,
                          Func<RouteNode> getSelected,
                          Action<RouteNode> setSelected,
                          Predicate<RouteNode> hasNode,
                          Func<float> radius )
    {
      Node = node;
      Parent = parent;
      AddChild( new FrameTool( node.Frame ) { OnChangeDirtyTarget = Parent, TransformHandleActive = false } );

      m_getSelected = getSelected;
      m_setSelected = setSelected;
      m_hasNode = hasNode;
      m_radius = radius ?? new Func<float>( () => { return 0.05f; } );

      Visual.Color = Color.yellow;
      Visual.MouseOverColor = new Color( 0.1f, 0.96f, 0.15f, 1.0f );
      Visual.OnMouseClick += OnClick;
    }

    public override void OnSceneViewGUI( SceneView sceneView )
    {
      if ( Parent == null || Node == null || !m_hasNode( Node ) ) {
        PerformRemoveFromParent();
        return;
      }

      float radius = 3f * m_radius();
      Visual.Visible = !EditorApplication.isPlaying;
      Visual.Color = Selected ? Visual.MouseOverColor : Color.yellow;
      Visual.SetTransform( Node.Frame.Position, Node.Frame.Rotation, radius, true, 1.2f * m_radius(), Mathf.Max( 1.5f * m_radius(), 0.25f ) );
    }

    private void OnClick( AgXUnity.Utils.Raycast.Hit hit, Utils.VisualPrimitive primitive )
    {
      Selected = true;
    }
  }
}