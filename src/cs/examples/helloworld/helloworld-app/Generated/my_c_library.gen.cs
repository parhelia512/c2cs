
// <auto-generated>
//  This code was generated by the following tool on 2023-07-09 12:47:37 GMT-04:00:
//      https://github.com/bottlenoselabs/c2cs (v1.0.0.0)
//
//  Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
// </auto-generated>
// ReSharper disable All

#region Template
#nullable enable
#pragma warning disable CS1591
#pragma warning disable CS8981
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using static bottlenoselabs.C2CS.Runtime;
#endregion

namespace my_c_library_namespace;

public static unsafe partial class my_c_library
{
    private const string LibraryName = "my_c_library";

    #region API

    [CNode(Kind = "Function")]
    [DllImport(LibraryName, EntryPoint = "hw_hello_world", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hw_hello_world();

    [CNode(Kind = "Function")]
    [DllImport(LibraryName, EntryPoint = "hw_invoke_callback", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hw_invoke_callback(FnPtr_CString_Void f, CString s);

    [CNode(Kind = "Function")]
    [DllImport(LibraryName, EntryPoint = "hw_pass_enum", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hw_pass_enum(hw_my_enum_week_day e);

    [CNode(Kind = "Function")]
    [DllImport(LibraryName, EntryPoint = "hw_pass_integers_by_reference", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hw_pass_integers_by_reference(ushort* a, int* b, ulong* c);

    [CNode(Kind = "Function")]
    [DllImport(LibraryName, EntryPoint = "hw_pass_integers_by_value", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hw_pass_integers_by_value(ushort a, int b, ulong c);

    [CNode(Kind = "Function")]
    [DllImport(LibraryName, EntryPoint = "hw_pass_string", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hw_pass_string(CString s);

    #endregion

    #region Types

    [CNode(Kind = "FunctionPointer")]
    [StructLayout(LayoutKind.Sequential)]
    public struct FnPtr_CString_Void
    {
        public delegate* unmanaged<CString, void> Pointer;
    }

    [CNode(Kind = "Enum")]
    public enum hw_my_enum_week_day : int
    {
        HW_MY_ENUM_WEEK_DAY_UNKNOWN = 0,
        HW_MY_ENUM_WEEK_DAY_MONDAY = 1,
        HW_MY_ENUM_WEEK_DAY_TUESDAY = 2,
        HW_MY_ENUM_WEEK_DAY_WEDNESDAY = 3,
        HW_MY_ENUM_WEEK_DAY_THURSDAY = 4,
        HW_MY_ENUM_WEEK_DAY_FRIDAY = 5,
        _HW_MY_ENUM_WEEK_DAY_FORCE_U32 = 2147483647
    }

    #endregion
}

