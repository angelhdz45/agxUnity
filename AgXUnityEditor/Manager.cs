﻿using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using AgXUnity.Utils;

namespace AgXUnityEditor
{
  /// <summary>
  /// Manager object, initialized when the Unity editor is loaded, to handle
  /// all tools, behavior related etc. objects while in edit mode.
  /// </summary>
  [InitializeOnLoad]
  public static class Manager
  {
    /// <summary>
    /// The game object mouse is currently over in scene view. Hidden objects,
    /// e.g., VisualPrimitive isn't included in this.
    /// </summary>
    public static GameObject MouseOverObject { get; private set; }

    /// <summary>
    /// True if the current event is left mouse down.
    /// </summary>
    public static bool LeftMouseClick { get; private set; }

    /// <summary>
    /// True if the current event is right mouse down.
    /// </summary>
    public static bool RightMouseClick { get; private set; }

    /// <summary>
    /// True if the right mouse button is pressed (and hold).
    /// </summary>
    public static bool RightMouseDown { get; private set; }

    /// <summary>
    /// True if keyboard escape key is down.
    /// </summary>
    public static bool KeyEscapeDown { get; private set; }

    /// <summary>
    /// True if mouse + key combo is assumed to be a camera control move.
    /// </summary>
    public static bool IsCameraControl { get; private set; }

    /// <summary>
    /// Constructor called when the Unity editor is initialized.
    /// </summary>
    static Manager()
    {
      // If compatibility issues, this method will try to fix them and this manager
      // will probably be loaded again after the fix.
      if ( !VerifyCompatibility() )
        return;

      SceneView.onSceneGUIDelegate += OnSceneView;
      EditorApplication.hierarchyWindowChanged += OnHierarchyWindowChanged;
      Selection.selectionChanged += OnSelectionChanged;

      while ( VisualsParent != null && VisualsParent.transform.childCount > 0 )
        GameObject.DestroyImmediate( VisualsParent.transform.GetChild( 0 ).gameObject );

      MouseOverObject = null;

      Undo.undoRedoPerformed += UndoRedoPerformedCallback;

      // Focus on scene view for events to be properly handled. E.g.,
      // holding one key and click in scene view is not working until
      // scene view is focused since some other event is taking the
      // key event.
      RequestSceneViewFocus();

      Tools.Tool.ActivateBuiltInTools();

      CreateDefaultAssets();
    }

    /// <summary>
    /// Data that tracks certain events when we're hijacking left mouse button.
    /// </summary>
    private class HijackLeftMouseClickData
    {
      public bool AltPressed { get; set; }

      public HijackLeftMouseClickData()
      {
        AltPressed = false;
      }
    };

    /// <summary>
    /// Hijacks left mouse down from the editor and returns true when the button
    /// is released. This is the default behavior of the editor (select @ mouse up)
    /// and it's, without this method, impossible to detect mouse up events.
    /// </summary
    /// <remarks>
    /// Using this method disables the editor default selection behavior.
    /// </remarks>
    /// <returns>True when the hijacked mouse down button is released (i.e., EventType.MouseUp).</returns>
    public static bool HijackLeftMouseClick()
    {
      Event current = Event.current;
      if ( current == null ) {
        Debug.LogError( "Hijack Left Mouse Click can only be used in the GUI event loop." );
        return false;
      }

      EventType currentMouseEventType = current.GetTypeForControl( GUIUtility.GetControlID( FocusType.Passive ) );
      bool hijackMouseDown = currentMouseEventType == EventType.MouseDown &&
                             current.button == 0 &&
                            !RightMouseDown &&                                // button 1 is FPS camera movement
                            !current.alt;                                     // alt down is track ball camera movement
      if ( hijackMouseDown ) {
        m_hijackLeftMouseClickData = new HijackLeftMouseClickData();
        GUIUtility.hotControl = 0;
        Event.current.Use();
        return false;
      }

      if ( m_hijackLeftMouseClickData != null ) {
        m_hijackLeftMouseClickData.AltPressed |= current.alt;

        bool leftMouseUp = !m_hijackLeftMouseClickData.AltPressed &&
                            currentMouseEventType == EventType.MouseUp &&
                            Event.current.button == 0;

        if ( currentMouseEventType == EventType.MouseUp )
          m_hijackLeftMouseClickData = null;

        return leftMouseUp;
      }

      return false;
    }

    /// <summary>
    /// Call this method to reset key escape flag, i.e, KeyEscapeDown == false after the call.
    /// </summary>
    public static void UseKeyEscapeDown()
    {
      KeyEscapeDown = false;
    }

    public static bool IsKeyEscapeDown( Event current )
    {
      return current != null && current.isKey && current.keyCode == KeyCode.Escape && current.type == EventType.KeyUp;
    }

    /// <summary>
    /// Request focus of the scene view window. E.g., when a button is pressed
    /// in the inspector tab and objects in the scene view should respond.
    /// </summary>
    public static void RequestSceneViewFocus()
    {
      m_requestSceneViewFocus = true;
    }

    /// <summary>
    /// Routes current object to the desired object when e.g., selected.
    /// This method uses OnSelectionProxy to find the desired object.
    /// </summary>
    /// <returns>Input object if the object doesn't contains an OnSelectionProxy route.</returns>
    public static UnityEngine.Object RouteObject( UnityEngine.Object obj )
    {
      GameObject gameObject = obj as GameObject;
      var proxy = gameObject != null ? gameObject.GetComponent<OnSelectionProxy>() : null;
      // If proxy target is null we're ignoring it.
      var result = proxy != null &&
                  !GetSelectedInHierarchyData( proxy ).Bool &&
                   proxy.Target != null ?
                     proxy.Target :
                     obj;
      return result;
    }

    /// <summary>
    /// Routes given object to the game object of an AgXUnity.Collide.Shape if
    /// the connection is given using OnSelectionProxy.
    /// </summary>
    /// <returns>Shape game object if found - otherwise null.</returns>
    public static GameObject RouteToShape( UnityEngine.Object obj )
    {
      GameObject gameObject = obj as GameObject;
      OnSelectionProxy selectionProxy = null;
      if ( gameObject == null || ( selectionProxy = gameObject.GetComponent<OnSelectionProxy>() ) == null )
        return null;

      if ( selectionProxy.Target != null && selectionProxy.Target.GetComponent<AgXUnity.Collide.Shape>() != null )
        return selectionProxy.Target;

      return null;
    }

    /// <summary>
    /// Get or create default shape visuals material.
    /// </summary>
    /// <returns>Material asset.</returns>
    public static Material GetOrCreateShapeVisualDefaultMaterial()
    {
      return GetOrCreateAsset<Material>( AgXUnity.Rendering.ShapeVisual.DefaultMaterialPath,
                                         () => AgXUnity.Rendering.ShapeVisual.CreateDefaultMaterial() );
    }

    public static void OnVisualPrimitiveNodeCreate( Utils.VisualPrimitive primitive )
    {
      if ( primitive == null || primitive.Node == null )
        return;

      // TODO: Fix so that "MouseOver" works for newly created primitives.

      if ( primitive.Node.transform.parent != VisualsParent )
        VisualsParent.AddChild( primitive.Node );

      m_visualPrimitives.Add( primitive );
    }

    public static void OnVisualPrimitiveNodeDestruct( Utils.VisualPrimitive primitive )
    {
      if ( primitive == null || primitive.Node == null )
        return;

      primitive.Node.transform.parent = null;
      m_visualPrimitives.Remove( primitive );

      GameObject.DestroyImmediate( primitive.Node );
    }

    private static string m_currentSceneName = string.Empty;
    private static bool m_requestSceneViewFocus = false;
    private static HijackLeftMouseClickData m_hijackLeftMouseClickData = null;

    private static string m_visualParentName = "Manager".To32BitFnv1aHash().ToString();
    private static GameObject m_visualsParent = null;
    private static HashSet<Utils.VisualPrimitive> m_visualPrimitives = new HashSet<Utils.VisualPrimitive>();

    public static GameObject VisualsParent
    {
      get
      {
        if ( m_visualsParent == null ) {
          m_visualsParent = GameObject.Find( m_visualParentName ) ?? new GameObject( m_visualParentName );
          m_visualsParent.hideFlags = HideFlags.HideAndDontSave;
        }

        return m_visualsParent;
      }
    }

    /// <summary>
    /// Callback when undo or redo has been performed. There's a significant
    /// delay to e.g., Inspector update when this happens so we're explicitly
    /// telling Unity to update selected object (if ScriptComponent).
    /// </summary>
    private static void UndoRedoPerformedCallback()
    {
      if ( Selection.activeGameObject == null )
        return;

      var scriptComponents = Selection.activeGameObject.GetComponents<AgXUnity.ScriptComponent>();
      foreach ( var scriptComponent in scriptComponents )
        EditorUtility.SetDirty( scriptComponent );

      if ( scriptComponents.Length > 0 )
        SceneView.RepaintAll();
    }

    private static void OnSceneView( SceneView sceneView )
    {
      if ( m_requestSceneViewFocus ) {
        sceneView.Focus();
        m_requestSceneViewFocus = false;
      }

      Event current = Event.current;
      LeftMouseClick = !current.control && !current.shift && !current.alt && current.type == EventType.MouseDown && current.button == 0;
      KeyEscapeDown = IsKeyEscapeDown( current );
      RightMouseClick = current.type == EventType.MouseDown && current.button == 1;

      if ( RightMouseClick )
        RightMouseDown = true;
      if ( current.type == EventType.MouseUp && current.button == 1 )
        RightMouseDown = false;

      IsCameraControl = current.alt || RightMouseDown;

      foreach ( var primitive in m_visualPrimitives )
        primitive.OnSceneView( sceneView );

      UpdateMouseOverPrimitives( current );

      Tools.Tool.HandleOnSceneViewGUI( sceneView );

      HandleWindowsGUI( sceneView );

      LeftMouseClick = false;

      if ( EditorData.Instance.SecondsSinceLastGC > 5.0 * 60 )
        EditorData.Instance.GC();

      SceneView.RepaintAll();
    }

    private static void UpdateMouseOverPrimitives( Event current )
    {
      // Can't perform picking during repaint event.
      if ( current == null || !( current.isMouse || current.isKey ) )
        return;

      // Update mouse over before we reveal the VisualPrimitives.
      // NOTE: We're putting our "visual primitives" in the ignore list.
      if ( current.isMouse ) {
        List<GameObject> ignoreList = new List<GameObject>();
        foreach ( var primitive in m_visualPrimitives ) {
          if ( !primitive.Visible )
            continue;

          MeshFilter[] primitiveFilters = primitive.Node.GetComponentsInChildren<MeshFilter>();
          ignoreList.AddRange( primitiveFilters.Select( pf => { return pf.gameObject; } ) );
        }

        // If the mouse is hovering a scene view window - MouseOverObject should be null.
        if ( SceneViewWindow.GetMouseOverWindow( current.mousePosition ) != null )
          MouseOverObject = null;
        else
          MouseOverObject = RouteObject( HandleUtility.PickGameObject( current.mousePosition,
                                                                       false,
                                                                       ignoreList.ToArray() ) ) as GameObject;
      }

      // Early exit if we haven't any active visual primitives.
      if ( m_visualPrimitives.Count == 0 )
        return;

      var primitiveHitList = new[] { new { Primitive = (Utils.VisualPrimitive)null, RaycastResult = Raycast.Hit.Invalid } }.ToList();
      primitiveHitList.Clear();

      Ray mouseRay = HandleUtility.GUIPointToWorldRay( current.mousePosition );
      foreach ( var primitive in m_visualPrimitives ) {
        primitive.MouseOver = false;

        if ( !primitive.Pickable )
          continue;

        Raycast.Hit hit = Raycast.Test( primitive.Node, mouseRay, 500f, true );
        if ( hit.Triangle.Valid )
          primitiveHitList.Add( new { Primitive = primitive, RaycastResult = hit } );
      }

      if ( primitiveHitList.Count == 0 )
        return;

      var bestResult = primitiveHitList[ 0 ];
      for ( int i = 1; i < primitiveHitList.Count; ++i )
        if ( primitiveHitList[ i ].RaycastResult.Triangle.Distance < bestResult.RaycastResult.Triangle.Distance )
          bestResult = primitiveHitList[ i ];

      bestResult.Primitive.MouseOver = true;
      if ( HijackLeftMouseClick() )
        bestResult.Primitive.OnMouseClick( bestResult.RaycastResult, bestResult.Primitive );
    }

    private static void OnHierarchyWindowChanged()
    {
      var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
      if ( scene != null && scene.name != m_currentSceneName ) {
        EditorData.Instance.GC();

        m_currentSceneName = scene.name;

        // - Verifies so that our shapes doesn't have multiple debug rendering components.
        // - Verifies version of OnSelectionProxy and patches it if Target == null.
        AgXUnity.Collide.Shape[] shapes = UnityEngine.Object.FindObjectsOfType<AgXUnity.Collide.Shape>();
        foreach ( var shape in shapes ) {
          OnSelectionProxy selectionProxy = shape.GetComponent<OnSelectionProxy>();
          if ( selectionProxy != null && selectionProxy.Target == null )
            selectionProxy.Component = shape;

          AgXUnity.Rendering.ShapeDebugRenderData[] data = shape.GetComponents<AgXUnity.Rendering.ShapeDebugRenderData>();
          if ( data.Length > 1 ) {
            Debug.Log( "Shape has several ShapeDebugRenderData. Removing/resetting.", shape );
            foreach ( var instance in data )
              Component.DestroyImmediate( instance );
            data = null;
          }
        }

        // We're back to ScriptComponent version of MassProperties.
        AgXUnity.RigidBody[] bodies = UnityEngine.Object.FindObjectsOfType<AgXUnity.RigidBody>();
        foreach ( var rb in bodies ) {
          if ( !rb.PatchMassPropertiesAsComponent() )
            continue;

          Debug.Log( "Updated RigidBody: " + rb.name + " to new MassProperties version.", rb );

          UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
        }

        // Patching constraints where we removed ElementaryConstraint as component.
        // Now we're back and I hope the components will still show up as null now
        // that AgXUnity.ElementaryConstraint is a component again.
        {
          AgXUnity.Constraint[] constraints = UnityEngine.Object.FindObjectsOfType<AgXUnity.Constraint>();
          foreach ( var constraint in constraints ) {
            bool isOldVersion = ( from component in constraint.GetComponents<Component>() where component == null select component ).ToArray().Length > 0 &&
                                constraint.ElementaryConstraints.Length == 0;
            if ( !isOldVersion )
              continue;

            if ( EditorUtility.DisplayDialog( "Update \"" + constraint.name + "\" (type: " + constraint.Type + ") to the new version?",
                                              "The game object will be deleted and a new will be created with the same Reference/Connected setup. All data such as compliance, damping, motor speed etc. will be lost.",
                                              "Update", "Ignore" ) ) {
              AgXUnity.Constraint newConstraint = AgXUnity.Constraint.Create( constraint.Type );

              newConstraint.AttachmentPair.ReferenceObject = constraint.AttachmentPair.ReferenceObject;
              newConstraint.AttachmentPair.ReferenceFrame.LocalPosition = constraint.AttachmentPair.ReferenceFrame.LocalPosition;
              newConstraint.AttachmentPair.ReferenceFrame.LocalRotation = constraint.AttachmentPair.ReferenceFrame.LocalRotation;

              newConstraint.AttachmentPair.Synchronized = constraint.AttachmentPair.Synchronized;

              newConstraint.AttachmentPair.ConnectedObject = constraint.AttachmentPair.ConnectedObject;
              newConstraint.AttachmentPair.ConnectedFrame.LocalPosition = constraint.AttachmentPair.ConnectedFrame.LocalPosition;
              newConstraint.AttachmentPair.ConnectedFrame.LocalRotation = constraint.AttachmentPair.ConnectedFrame.LocalRotation;

              newConstraint.name = constraint.name;

              GameObject.DestroyImmediate( constraint.gameObject );

              Debug.Log( "Constraint: " + newConstraint.name + " updated.", newConstraint );

              UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
            }
          }
        }

        // Patching constraints where ElementaryConstraint is a ScriptAsset.
        {
          AgXUnity.Constraint[] constraints = UnityEngine.Object.FindObjectsOfType<AgXUnity.Constraint>();
          foreach ( var constraint in constraints ) {
            bool isOldVersion = constraint.ElementaryConstraints.Length > 0 && constraint.GetComponents<AgXUnity.ElementaryConstraint>().Length == 0;
            if ( !isOldVersion )
              continue;

            // Updating to new attachment pair.
            var attachmentPair = constraint.AttachmentPair;

            // Updating to where ElementaryConstraints are components.
            constraint.TransformToComponentVersion();

            Debug.Log( "Constraint: " + constraint.name + " updated to new version.", constraint );

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
          }
        }

        // Patching old constraints to new versions.
        {
          AgXUnity.Constraint[] constraints = UnityEngine.Object.FindObjectsOfType<AgXUnity.Constraint>();
          foreach ( var constraint in constraints ) {
            if ( !constraint.VerifyImplementation() )
              continue;

            Debug.Log( "Constraint: " + constraint.name + " successfully updated to new version.", constraint );

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
          }
        }

        // Patching OnSelectionProxy (Target == null) in the wire rendering SegmentSpawner.
        {
          AgXUnity.Wire[] wires = UnityEngine.Object.FindObjectsOfType<AgXUnity.Wire>();
          foreach ( var wire in wires ) {
            AgXUnity.Rendering.SegmentSpawner ss = wire.GetComponent<AgXUnity.Rendering.WireRenderer>().SegmentSpawner;
            if ( ss == null )
              continue;

            var segments = ss.Segments;
            foreach ( var segment in segments ) {
              OnSelectionProxy selectionProxy = segment.GetComponent<OnSelectionProxy>();
              if ( selectionProxy != null && selectionProxy.Target == null )
                selectionProxy.Component = wire;
            }
          }
        }

        // Patching Wire to use Route as component and RouteNode as Frame.
        {
          AgXUnity.Wire[] wires = UnityEngine.Object.FindObjectsOfType<AgXUnity.Wire>();
          foreach ( var wire in wires ) {
            if ( wire.GetComponent<AgXUnity.WireRoute>() != null )
              continue;

            var routeData = wire.GetComponent<AgXUnity.Legacy.WireRouteData>();
            var route = wire.gameObject.AddComponent<AgXUnity.WireRoute>();
            if ( routeData != null && routeData.Restore() ) {
              Debug.Log( "Successfully restored " + route.NumNodes + " from local data.", wire );
              AgXUnity.ScriptComponent.DestroyImmediate( routeData );
            }
            else
              Debug.LogWarning( "Wire: " + wire.name + " is not possible to load and has to be re-routed.", wire );

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
          }
        }

        // Patching Cable to use Route as component and RouteNode as Frame.
        {
          AgXUnity.Cable[] cables = UnityEngine.Object.FindObjectsOfType<AgXUnity.Cable>();
          foreach ( var cable in cables ) {
            if ( cable.GetComponent<AgXUnity.CableRoute>() != null )
              continue;

            var routeData = cable.GetComponent<AgXUnity.Legacy.CableRouteData>();
            var route = cable.gameObject.AddComponent<AgXUnity.CableRoute>();
            if ( routeData != null && routeData.Restore() ) {
              Debug.Log( "Successfully restored " + route.NumNodes + " from local data.", cable );
              AgXUnity.ScriptComponent.DestroyImmediate( routeData );
            }
            else
              Debug.LogWarning( "Cable: " + cable.name + " is not possible to load and has to be re-routed.", cable );

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
          }
        }

        // Verifying shape visuals material.
        {
          AgXUnity.Rendering.ShapeVisual[] shapeVisuals = UnityEngine.Object.FindObjectsOfType<AgXUnity.Rendering.ShapeVisual>();
          foreach ( var shapeVisual in shapeVisuals ) {
            var renderers = shapeVisual.GetComponentsInChildren<MeshRenderer>();
            foreach ( var renderer in renderers ) {
              if ( renderer.sharedMaterial == null ) {
                renderer.sharedMaterial = GetOrCreateShapeVisualDefaultMaterial();

                Debug.Log( "Shape visual with null material. Assigning default.", shapeVisual );

                if ( !EditorApplication.isPlaying )
                  UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
              }
            }
          }
        }

        // Patching Collide.Mesh single source object to source object list.
        {
          AgXUnity.Collide.Mesh[] meshes = UnityEngine.Object.FindObjectsOfType<AgXUnity.Collide.Mesh>();
          foreach ( var mesh in meshes ) {
            if ( !mesh.PatchSingleSourceToSourceList() )
              continue;

            Debug.Log( "Patch: Moved mesh source to source list.", mesh );

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( scene );
          }
        }
      }
      else if ( Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<AgXUnity.IO.RestoredAGXFile>() != null ) {
        AssetPostprocessorHandler.OnPrefabAddedToScene( Selection.activeGameObject );
      }
    }

    /// <summary>
    /// Previous selection used to reset used EditorDataEntry entries.
    /// </summary>
    private static UnityEngine.Object[] m_previousSelection = new UnityEngine.Object[] { };

    /// <summary>
    /// Editor data entry for "SelectedInHierarchy" property.
    /// </summary>
    /// <param name="proxy">OnSelectionProxy instance. Invalid if null.</param>
    /// <returns>EditorDataEntry for given <paramref name="proxy"/>.</returns>
    private static EditorDataEntry GetSelectedInHierarchyData( OnSelectionProxy proxy )
    {
      return EditorData.Instance.GetData( proxy, "SelectedInHierarchy" );
    }

    /// <summary>
    /// Callback when selection has been changed in the editor. Mainly used to
    /// catch when the user selects an OnSelectionProxy route in the hierarchy
    /// tab, i.e., such that it shouldn't be routed when clicking in hierarchy.
    /// </summary>
    private static void OnSelectionChanged()
    {
      bool mouseOverHierarchy = EditorWindow.mouseOverWindow != null &&
                                EditorWindow.mouseOverWindow.GetType().FullName == "UnityEditor.SceneHierarchyWindow";

      // Assigns and saves 'state' in editor data for game object with OnSelectionProxy.
      // If OnSelectionProxy is present the given state is returned.
      Func<GameObject, bool, bool> setOnSelectionProxyState = ( go, state ) =>
      {
        var proxy = go != null ? go.GetComponent<OnSelectionProxy>() : null;
        if ( proxy != null )
          return GetSelectedInHierarchyData( proxy ).Bool = state;
        return false;
      };

      // Reset previously selected as "not selected in hierarchy".
      foreach ( var prevSelected in m_previousSelection ) {
        // Could be deleted - only valid to check if null.
        if ( prevSelected == null )
          continue;

        setOnSelectionProxyState( prevSelected as GameObject, false );
      }

      bool toolsHidden = false;
      // If newly selected object(s) are selected in the hierarchy window we shouldn't
      // route it in this.RouteObject.
      foreach ( var selected in Selection.objects )
        toolsHidden = setOnSelectionProxyState( selected as GameObject, mouseOverHierarchy ) || toolsHidden;

      // Hides transform tool when e.g., DebugRenderManager is selected.
      if ( !toolsHidden &&
           Selection.activeGameObject != null &&
           ( Selection.activeGameObject.transform.hideFlags & HideFlags.NotEditable ) != 0 )
        toolsHidden = true;

      UnityEditor.Tools.hidden = toolsHidden;

      m_previousSelection = Selection.objects;
    }

    private static void HandleWindowsGUI( SceneView sceneView )
    {
      SceneViewWindow.OnSceneView( sceneView );
    }

    private static bool Equals( byte[] a, byte[] b )
    {
      if ( a.Length != b.Length )
        return false;
      for ( long i = 0; i < a.LongLength; ++i )
        if ( a[ i ] != b[ i ] )
          return false;
      return true;
    }

    private static bool VerifyCompatibility()
    {
      Func<FileInfo, byte[]> generateMd5 = ( fi ) =>
      {
        using ( var stream = fi.OpenRead() ) {
          return System.Security.Cryptography.MD5.Create().ComputeHash( stream );
        }
      };

      // Initializes AGX and adds installed directory from registry to path if
      // AGX doesn't already exist in path.
      var nativeHandler = AgXUnity.NativeHandler.Instance;

      string localDllFilename = Application.dataPath + @"/AgXUnity/Plugins/agxDotNet.dll";
      FileInfo currDll = new FileInfo( localDllFilename );
      FileInfo installedDll = null;

      // Search for installed AGX with agxDotNet.dll in path.
      foreach ( var envPath in Environment.GetEnvironmentVariable( "PATH", EnvironmentVariableTarget.Process ).Split( ';' ) ) {
        installedDll = new FileInfo( envPath + @"/agxDotNet.dll" );
        if ( installedDll.Exists )
          break;
      }

      // Wasn't able to find any installed agxDotNet.dll - it's up to Unity to handle this...
      if ( !installedDll.Exists )
        return true;

      if ( !currDll.Exists || !generateMd5( currDll ).SequenceEqual( generateMd5( installedDll ) ) ) {
        Debug.Log( "<color=green>New version of agxDotNet.dll located in: " + installedDll.Directory + ". Copying it to current project.</color>" );
        installedDll.CopyTo( localDllFilename, true );
        return false;
      }

      return true;
    }

    private static T GetOrCreateAsset<T>( string assetPath, Func<T> createFunc = null )
      where T : UnityEngine.Object
    {
      var obj = AssetDatabase.LoadAssetAtPath<T>( assetPath );
      if ( obj == null ) {
        if ( createFunc != null )
          obj = createFunc();
        else if ( typeof( AgXUnity.ScriptAsset ).IsAssignableFrom( typeof( T ) ) )
          obj = AgXUnity.ScriptAsset.Create( typeof( T ) ) as T;
        else if ( typeof( ScriptableObject ).IsAssignableFrom( typeof( T ) ) )
          obj = ScriptableObject.CreateInstance( typeof( T ) ) as T;

        if ( obj == null )
          throw new Exception( "Unable to create asset at path: " + assetPath );

        AssetDatabase.CreateAsset( obj, assetPath );
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
      }

      return obj;
    }

    private static void CreateDefaultAssets()
    {
      // Generate/synchronize custom editors.
      if ( !Directory.Exists( Utils.CustomEditorGenerator.Path ) )
        Directory.CreateDirectory( Utils.CustomEditorGenerator.Path );
      Utils.CustomEditorGenerator.Synchronize();

      // Shape visual material.
      GetOrCreateShapeVisualDefaultMaterial();

      // Merge split thresholds.
      if ( !Directory.Exists( AgXUnity.MergeSplitThresholds.AssetDirectory ) )
        Directory.CreateDirectory( AgXUnity.MergeSplitThresholds.AssetDirectory );
      GetOrCreateAsset<AgXUnity.GeometryContactMergeSplitThresholds>( AgXUnity.GeometryContactMergeSplitThresholds.AssetPath );
      GetOrCreateAsset<AgXUnity.ConstraintMergeSplitThresholds>( AgXUnity.ConstraintMergeSplitThresholds.AssetPath );
    }
  }
}
