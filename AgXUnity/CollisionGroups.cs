﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AgXUnity.Utils;

namespace AgXUnity
{
  /// <summary>
  /// Collision group identifier containing a name tag.
  /// </summary>
  [System.Serializable]
  public class CollisionGroupEntry
  {
    /// <summary>
    /// Flag if this component should affect all children. E.g., add this
    /// component to a game object which contains several rigid bodies as
    /// children - all shapes and bodies will inherit the collision groups.
    /// 
    /// If false, this component will check for compatible components to
    /// affect on the same level as this.
    /// </summary>
    /// <remarks>
    /// It's not possible to change this property during runtime.
    /// </remarks>
    [SerializeField]
    private bool m_propagateToChildren = false;

    /// <summary>
    /// Flag if this component should affect all children. E.g., add this
    /// component to a game object which contains several rigid bodies as
    /// children - all shapes and bodies will inherit the collision groups.
    /// 
    /// If false, this component will check for compatible components to
    /// affect on the same level as this.
    /// </summary>
    /// <remarks>
    /// It's not possible to change this property during runtime.
    /// </remarks>
    public bool PropagateToChildren
    {
      get { return m_propagateToChildren; }
      set { m_propagateToChildren = value; }
    }

    [SerializeField]
    private string m_tag = "";
    public string Tag
    {
      get { return m_tag; }
      set { m_tag = value; }
    }

    /// <summary>
    /// If <paramref name="obj"/> has method "addGroup( UInt32 )" this method
    /// converts the name tag to an UInt32 using 32 bit FNV1 hash.
    /// </summary>
    /// <param name="obj">Object to execute addGroup on.</param>
    public void AddTo( object obj )
    {
      InvokeIdMethod( "addGroup", obj );
    }

    public void RemoveFrom( object obj )
    {
      InvokeIdMethod( "removeGroup", obj );
    }

    private void InvokeIdMethod( string method, object obj )
    {
      if ( obj == null )
        return;

      var m = obj.GetType().GetMethod( method, new Type[] { typeof( UInt32 ) } );
      if ( m == null )
        throw new Exception( "Method " + method + " not found in type: " + obj.GetType().FullName );
      m.Invoke( obj, new object[] { Tag.To32BitFnv1aHash() } );
    }
  }

  /// <summary>
  /// Component holding a list of name tags for collision groups.
  /// </summary>
  [AddComponentMenu( "AgXUnity/Collisions/CollisionGroups" )]
  public class CollisionGroups : ScriptComponent
  {
    /// <summary>
    /// List of collision groups paired with property Groups.
    /// </summary>
    [SerializeField]
    private List<CollisionGroupEntry> m_groups = new List<CollisionGroupEntry>() { };

    /// <summary>
    /// Get current list of groups.
    /// </summary>
    public List<CollisionGroupEntry> Groups
    {
      get { return m_groups; }
    }

    /// <param name="tag">Name tag to check if it exist in the current set of groups.</param>
    /// <returns>True if the given name tag exists.</returns>
    public bool HasGroup( string tag )
    {
      return m_groups.Find( entry => entry.Tag == tag ) != null;
    }

    /// <summary>
    /// Add new group.
    /// </summary>
    /// <param name="tag">New group tag.</param>
    /// <param name="propagateToChildren">True if this tag should be propagated to all supported children.</param>
    /// <returns>True if the group was added - otherwise false (e.g., already exists).</returns>
    public bool AddGroup( string tag, bool propagateToChildren )
    {
      if ( HasGroup( tag ) )
        return false;

      m_groups.Add( new CollisionGroupEntry() { Tag = tag } );

      if ( State == States.INITIALIZED )
        AddGroup( m_groups.Last(), CollectData( propagateToChildren ) );

      return true;
    }

    /// <summary>
    /// Remove group.
    /// </summary>
    /// <param name="tag">Group to remove.</param>
    /// <returns>True if removed - otherwise false.</returns>
    public bool RemoveGroup( string tag )
    {
      int index = m_groups.FindIndex( entry => entry.Tag == tag );
      if ( index < 0 )
        return false;

      RemoveGroup( m_groups[ index ], CollectData( m_groups[ index ].PropagateToChildren ) );

      m_groups.RemoveAt( index );

      return true;
    }

    /// <summary>
    /// Initialize, finds supported object and executes addGroup to it.
    /// </summary>
    protected override bool Initialize()
    {
      if ( m_groups.Count == 0 )
        return base.Initialize();

      Data[] data = new Data[] { CollectData( false ), CollectData( true ) };
      foreach ( var entry in m_groups )
        AddGroup( entry, data[ Convert.ToInt32( entry.PropagateToChildren ) ] );

      return base.Initialize();
    }

    private class Data
    {
      public Collide.Shape[] Shapes = new Collide.Shape[] { };
      public Wire[] Wires = new Wire[] { };
      public Cable[] Cables = new Cable[] { };
    }

    private Data CollectData( bool propagateToChildren )
    {
      Data data = new Data(); 

      RigidBody rb        =                                                      GetComponent<RigidBody>();
      Collide.Shape shape = rb != null                  ? null                 : GetComponent<Collide.Shape>();
      Wire wire           = rb != null || shape != null ? null                 : GetComponent<Wire>();
      Cable cable         = rb != null || shape != null || wire != null ? null : GetComponent<Cable>();

      bool allPredefinedAreNull = rb == null && shape == null && wire == null && cable == null;

      if ( allPredefinedAreNull && propagateToChildren ) {
        data.Shapes = GetComponentsInChildren<Collide.Shape>();
        data.Wires  = GetComponentsInChildren<Wire>();
        data.Cables = GetComponentsInChildren<Cable>();
      }
      // A wire is by definition independent of PropagateToChildren, since
      // it's not defined to add children to a wire game object.
      else if ( wire != null ) {
        data.Wires = new Wire[] { wire };
      }
      // Same logics for Cable.
      else if ( cable != null ) {
        data.Cables = new Cable[] { cable };
      }
      // Bodies have shapes so if 'rb' != null we should collect all shape children
      // independent of 'propagate' flag.
      // If 'shape' != null and propagate is true we have the same condition as for bodies.
      else if ( rb != null || shape != null || ( rb == null && shape == null && propagateToChildren ) ) {
        data.Shapes = shape != null && !propagateToChildren ? GetComponents<Collide.Shape>() :
                      shape != null || rb != null           ? GetComponentsInChildren<Collide.Shape>() :
                                                              // Both shape and rb == null and PropagateToChildren == true.
                                                              GetComponentsInChildren<Collide.Shape>();
      }
      else {
        // These groups has no effect.
        Debug.LogWarning( "Collision groups has no effect. Are you missing a PropagateToChildren = true?", this );
      }

      return data;
    }

    private void AddGroup( CollisionGroupEntry entry, Data data )
    {
      foreach ( Collide.Shape shape in data.Shapes )
        if ( shape.GetInitialized<Collide.Shape>() != null )
          entry.AddTo( shape.NativeGeometry );

      foreach ( Wire wire in data.Wires )
        if ( wire.GetInitialized<Wire>() != null )
          entry.AddTo( wire.Native );

      foreach ( Cable cable in data.Cables )
        if ( cable.GetInitialized<Cable>() != null )
          cable.GetInitialized<Cable>().Native.addGroup( entry.Tag.To32BitFnv1aHash() );
    }

    private void RemoveGroup( CollisionGroupEntry entry, Data data )
    {
      foreach ( Collide.Shape shape in data.Shapes )
        if ( shape.GetInitialized<Collide.Shape>() != null )
          entry.RemoveFrom( shape.NativeGeometry );

      foreach ( Wire wire in data.Wires )
        if ( wire.GetInitialized<Wire>() != null )
          entry.RemoveFrom( wire.Native );

      foreach ( Cable cable in data.Cables ) {
        if ( cable.GetInitialized<Cable>() != null )
          cable.GetInitialized<Cable>().Native.removeGroup( entry.Tag.To32BitFnv1aHash() );
      }
    }
  }
}
