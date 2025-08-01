// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Collections.Immutable;
using System.Text;
using bottlenoselabs.Common.Tools;
using c2ffi.Data;
using c2ffi.Data.Nodes;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace C2CS.GenerateCSharpCode.Generators;

[UsedImplicitly]
public sealed class GeneratorFunctionPointer(ILogger<GeneratorFunctionPointer> logger)
    : BaseGenerator<CFunctionPointer>(logger)
{
    public override string GenerateCode(CodeGeneratorContext context, string nameCSharp, CFunctionPointer node)
    {
        var nameMapper = context.NameMapper;
        var code = context.Input.IsEnabledFunctionPointers ?
            GenerateCodeFunctionPointer(nameMapper, nameCSharp, node) : GenerateCodeDelegate(nameMapper, nameCSharp, node);
        return code;
    }

#pragma warning disable SA1202
    internal static string GenerateCodeFunctionPointer(
        NameMapper nameMapper, string nameCSharp, CFunctionPointer node)
#pragma warning restore SA1202
    {
        var functionPointerTypeNameCSharp = GetFunctionPointerTypeNameCSharp(nameMapper, node);

        var code = $$"""
                     [StructLayout(LayoutKind.Sequential)]
                     public partial struct {{nameCSharp}}
                     {
                     	public {{functionPointerTypeNameCSharp}} Pointer;

                     	public {{nameCSharp}}({{functionPointerTypeNameCSharp}} pointer)
                        {
                            Pointer = pointer;
                        }
                     }
                     """;
        return code;
    }

    private string GenerateCodeDelegate(
        NameMapper nameMapper, string name, CFunctionPointer node)
    {
        var parameterTypesString = GenerateCodeParameters(nameMapper, node.Parameters);
        var returnTypeString = nameMapper.GetTypeNameCSharp(node.ReturnType);

        var code = $$"""
                     [StructLayout(LayoutKind.Sequential)]
                     public partial struct {{name}}
                     {
                         [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
                         public unsafe delegate {{returnTypeString}} @delegate({{parameterTypesString}});

                         public IntPtr Pointer;

                         public {{name}}(@delegate d)
                         {
                             Pointer = Marshal.GetFunctionPointerForDelegate(d);
                         }
                     }
                     """;
        return code;
    }

    private static string GetFunctionPointerTypeNameCSharp(
        NameMapper nameMapper,
        CFunctionPointer node)
    {
        var parameterTypesString = GenerateCodeParameters(nameMapper, node.Parameters, false);
        var returnTypeString = nameMapper.GetTypeNameCSharp(node.ReturnType);
        var parameterTypesAndReturnTypeString = string.IsNullOrEmpty(parameterTypesString)
            ? returnTypeString
            : $"{parameterTypesString}, {returnTypeString}";

#pragma warning disable IDE0072
        // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
        var callingConvention = node.CallingConvention switch
#pragma warning restore IDE0072
        {
            CFunctionCallingConvention.Cdecl => "Cdecl",
            CFunctionCallingConvention.FastCall => "Fastcall",
            CFunctionCallingConvention.StdCall => "Stdcall",
            _ => throw new ToolException($"Unknown calling convention for function pointer '{node.Name}'.")
        };

        return $"delegate* unmanaged[{callingConvention}] <{parameterTypesAndReturnTypeString}>";
    }

    private static string GenerateCodeParameters(
        NameMapper nameMapper, ImmutableArray<CFunctionPointerParameter> parameters, bool includeNames = true)
    {
        var stringBuilder = new StringBuilder();

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            var parameterTypeNameCSharp = nameMapper.GetTypeNameCSharp(parameter.Type);
            _ = stringBuilder.Append(parameterTypeNameCSharp);

            if (includeNames)
            {
                var paramName = !string.IsNullOrEmpty(parameter.Name)
                    ? parameter.Name
                    : $"unnamed{i}";
                _ = stringBuilder.Append(' ');
                _ = stringBuilder.Append(paramName);
            }

            var isJoinedWithComma = parameters.Length > 1 && i != parameters.Length - 1;
            if (isJoinedWithComma)
            {
                _ = stringBuilder.Append(',');
            }
        }

        return stringBuilder.ToString();
    }
}
