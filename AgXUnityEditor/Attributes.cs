﻿using System;

namespace AgXUnityEditor
{
  [AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
  public class CustomTool : Attribute
  {
    public Type Type = null;

    public CustomTool( Type type )
    {
      Type = type;
    }
  }
}
