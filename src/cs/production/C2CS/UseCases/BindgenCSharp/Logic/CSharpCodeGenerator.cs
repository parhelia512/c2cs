// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace C2CS.UseCases.BindgenCSharp
{
	public class CSharpCodeGenerator
	{
		private readonly string _className;
		private readonly string _libraryName;
		private readonly ImmutableArray<string> _usingNamespaces;

		public CSharpCodeGenerator(string className, string libraryName, ImmutableArray<string> usingNamespaces)
		{
			_className = className;
			_libraryName = libraryName;
			_usingNamespaces = usingNamespaces;
		}

		public string EmitCode(CSharpAbstractSyntaxTree abstractSyntaxTree)
		{
			var builder = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

			EmitFunctionExterns(builder, abstractSyntaxTree.FunctionExterns);
			EmitFunctionPointers(builder, abstractSyntaxTree.FunctionPointers);
			EmitStructs(builder, abstractSyntaxTree.Structs);
			EmitOpaqueDataTypes(builder, abstractSyntaxTree.OpaqueDataTypes);
			EmitTypedefs(builder, abstractSyntaxTree.Typedefs);
			EmitEnums(builder, abstractSyntaxTree.Enums);
			EmitPseudoEnums(builder, abstractSyntaxTree.PseudoEnums);

			var membersToAdd = builder.ToArray();
			var compilationUnit = EmitCompilationUnit(
				_className,
				_libraryName,
				_usingNamespaces,
				membersToAdd);
			return compilationUnit.ToFullString();
		}

		private static CompilationUnitSyntax EmitCompilationUnit(
			string className,
			string libraryName,
			ImmutableArray<string> usingNamespaces,
			MemberDeclarationSyntax[] members)
		{
			var code = $@"
//-------------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by the following tool:
//        https://github.com/lithiumtoast/c2cs (v{Assembly.GetEntryAssembly()!.GetName().Version})
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ReSharper disable All
//-------------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
{string.Join("\n", usingNamespaces.Select(x => $"using {x};"))}
using C2CS;

#nullable enable
#pragma warning disable 1591

public static unsafe partial class {className}
{{
    private const string LibraryName = ""{libraryName}"";
}}
";

			var syntaxTree = ParseSyntaxTree(code);
			var compilationUnit = syntaxTree.GetCompilationUnitRoot();
			var @class = (ClassDeclarationSyntax)compilationUnit.Members[0];

			var newClass = @class.AddMembers(members);
			var newCompilationUnit = compilationUnit.ReplaceNode(@class, newClass);

			var workspace = new AdhocWorkspace();
			var newCompilationUnitFormatted = (CompilationUnitSyntax)Formatter.Format(newCompilationUnit, workspace);

			return newCompilationUnitFormatted;
		}

		private void EmitFunctionExterns(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpFunction> functionExterns)
		{
			foreach (var functionExtern in functionExterns)
			{
				// https://github.com/lithiumtoast/c2cs/issues/15
				var shouldIgnore = false;
				foreach (var cSharpFunctionExternParameter in functionExtern.Parameters)
				{
					if (cSharpFunctionExternParameter.Type.Name == "va_list")
					{
						shouldIgnore = true;
						break;
					}
				}

				if (shouldIgnore)
				{
					continue;
				}

				var member = EmitFunctionExtern(functionExtern);
				builder.Add(member);
			}
		}

		private MethodDeclarationSyntax EmitFunctionExtern(CSharpFunction function)
		{
			var parameterStrings = function.Parameters.Select(
				x => $@"{x.Type.Name} {x.Name}");
			var parameters = string.Join(',', parameterStrings);

			var code = $@"
{function.CodeLocationComment}
[DllImport(LibraryName)]
public static extern {function.ReturnType.Name} {function.Name}({parameters});
";

			var member = ParseMemberCode<MethodDeclarationSyntax>(code);
			return member;
		}

		private void EmitFunctionPointers(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpFunctionPointer> functionPointers)
		{
			foreach (var functionPointer in functionPointers)
			{
				var member = EmitFunctionPointer(functionPointer);
				builder.Add(member);
			}
		}

		private StructDeclarationSyntax EmitFunctionPointer(
			CSharpFunctionPointer functionPointer, bool isNested = false)
		{
			var parameterStrings = functionPointer.Parameters
				.Select(x => $"{x.Type}")
				.Append($"{functionPointer.ReturnType.Name}");
			var parameters = string.Join(',', parameterStrings);
			var functionPointerName = functionPointer.Name;

			var code = $@"
{functionPointer.CodeLocationComment}
[StructLayout(LayoutKind.Sequential)]
public struct {functionPointerName}
{{
	public delegate* unmanaged <{parameters}> Pointer;
}}
";

			if (isNested)
			{
				code = code.Trim();
			}

			var member = ParseMemberCode<StructDeclarationSyntax>(code);
			return member;
		}

		private void EmitStructs(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpStruct> structs)
		{
			foreach (var @struct in structs)
			{
				var member = EmitStruct(@struct);
				builder.Add(member);
			}
		}

		private StructDeclarationSyntax EmitStruct(CSharpStruct @struct, bool isNested = false)
		{
			var memberSyntaxes = EmitStructMembers(
				@struct.Name, @struct.Fields, @struct.NestedStructs);
			var memberStrings = memberSyntaxes.Select(x => x.ToFullString());
			var members = string.Join("\n\n", memberStrings);

			var code = $@"
{@struct.CodeLocationComment}
[StructLayout(LayoutKind.Explicit, Size = {@struct.Type.SizeOf}, Pack = {@struct.Type.AlignOf})]
public struct {@struct.Name}
{{
	{members}
}}
";

			if (isNested)
			{
				code = code.Trim();
			}

			var member = ParseMemberCode<StructDeclarationSyntax>(code);
			return member;
		}

		private MemberDeclarationSyntax[] EmitStructMembers(
			string structName,
			ImmutableArray<CSharpStructField> fields,
			ImmutableArray<CSharpStruct> nestedStructs)
		{
			var builder = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

			foreach (var field in fields)
			{
				if (!field.Type.IsArray)
				{
					var fieldMember = EmitStructField(field);
					builder.Add(fieldMember);
				}
				else
				{
					var fieldMember = EmitStructFieldFixedBuffer(field);
					builder.Add(fieldMember);

					var methodMember = EmitStructFieldFixedBufferProperty(
						structName, field);
					builder.Add(methodMember);
				}
			}

			foreach (var nestedStruct in nestedStructs)
			{
				var syntax = EmitStruct(nestedStruct, true);
				builder.Add(syntax);
			}

			var structMembers = builder.ToArray();
			return structMembers;
		}

		private static FieldDeclarationSyntax EmitStructField(CSharpStructField field)
		{
			var code = $@"
[FieldOffset({field.Offset})] // size = {field.Type.SizeOf}, padding = {field.Padding}
public {field.Type.Name} {field.Name};
".Trim();

			var member = ParseMemberCode<FieldDeclarationSyntax>(code);
			return member;
		}

		private static FieldDeclarationSyntax EmitStructFieldFixedBuffer(
			CSharpStructField field)
		{
			string typeName;

			if (field.IsWrapped)
			{
				typeName = field.Type.AlignOf switch
				{
					1 => "byte",
					2 => "ushort",
					4 => "uint",
					8 => "ulong",
					_ => throw new InvalidOperationException()
				};
			}
			else
			{
				typeName = field.Type.Name;
			}

			var code = $@"
[FieldOffset({field.Offset})] // size = {field.Type.SizeOf}, padding = {field.Padding}
public fixed {typeName} _{field.Name}[{field.Type.SizeOf}/{field.Type.AlignOf}]; // {field.Type.OriginalName}
".Trim();

			return ParseMemberCode<FieldDeclarationSyntax>(code);
		}

		private static PropertyDeclarationSyntax EmitStructFieldFixedBufferProperty(
			string structName,
			CSharpStructField field)
		{
			string code;

			if (field.Type.Name == "CString8U")
			{
				code = $@"
public string {field.Name}
{{
	get
	{{
		fixed ({structName}*@this = &this)
		{{
			var pointer = &@this->_{field.Name}[0];
            var cString = new CString8U(pointer);
            return Runtime.String8U(cString);
		}}
	}}
}}
".Trim();
			}
			else if (field.Type.Name == "CString16U")
			{
				code = $@"
public string {field.Name}
{{
	get
	{{
		fixed ({structName}*@this = &this)
		{{
			var pointer = &@this->_{field.Name}[0];
            var cString = new CString16U(pointer);
            return Runtime.String16U(cString);
		}}
	}}
}}
".Trim();
			}
			else
			{
				var elementType = field.Type.Name[..^1];
				if (elementType.EndsWith('*'))
				{
					elementType = "IntPtr";
				}

				code = $@"
public Span<{elementType}> {field.Name}
{{
	get
	{{
		fixed ({structName}*@this = &this)
		{{
			var pointer = &@this->_{field.Name}[0];
			var span = new Span<{elementType}>(pointer, {field.Type.ArraySize});
			return span;
		}}
	}}
}}
".Trim();
			}

			return ParseMemberCode<PropertyDeclarationSyntax>(code);
		}

		private static void EmitOpaqueDataTypes(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpOpaqueType> opaqueDataTypes)
		{
			foreach (var opaqueDataType in opaqueDataTypes)
			{
				var member = EmitOpaqueStruct(opaqueDataType);
				builder.Add(member);
			}
		}

		private static StructDeclarationSyntax EmitOpaqueStruct(CSharpOpaqueType opaqueType)
		{
			var code = $@"
{opaqueType.CodeLocationComment}
[StructLayout(LayoutKind.Sequential)]
public struct {opaqueType.Name}
{{
}}
";

			return ParseMemberCode<StructDeclarationSyntax>(code);
		}

		private static void EmitTypedefs(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpTypedef> typedefs)
		{
			foreach (var typedef in typedefs)
			{
				var member = EmitTypedef(typedef);
				builder.Add(member);
			}
		}

		private static StructDeclarationSyntax EmitTypedef(CSharpTypedef typedef)
		{
			var code = $@"
{typedef.CodeLocationComment}
[StructLayout(LayoutKind.Explicit, Size = {typedef.UnderlyingType.SizeOf}, Pack = {typedef.UnderlyingType.AlignOf})]
public struct {typedef.Name}
{{
	[FieldOffset(0)] // size = {typedef.UnderlyingType.SizeOf}, padding = 0
    public {typedef.UnderlyingType.Name} Data;

	public static implicit operator {typedef.UnderlyingType.Name}({typedef.Name} data) => data.Data;
	public static implicit operator {typedef.Name}({typedef.UnderlyingType.Name} data) => new() {{Data = data}};
}}
";

			var member = ParseMemberCode<StructDeclarationSyntax>(code);
			return member;
		}

		private static void EmitEnums(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpEnum> enums)
		{
			foreach (var @enum in enums)
			{
				var member = EmitEnum(@enum);
				builder.Add(member);
			}
		}

		private static EnumDeclarationSyntax EmitEnum(CSharpEnum @enum)
		{
			var values = EmitEnumValues(@enum.IntegerType.Name, @enum.Values);
			var valuesString = values.Select(x => x.ToFullString());
			var members = string.Join(",\n", valuesString);

			var code = $@"
{@enum.CodeLocationComment}
public enum {@enum.Name} : {@enum.IntegerType}
    {{
        {members}
    }}
";

			var member = ParseMemberCode<EnumDeclarationSyntax>(code);
			return member;
		}

		private static EnumMemberDeclarationSyntax[] EmitEnumValues(
			string enumTypeName, ImmutableArray<CSharpEnumValue> values)
		{
			var builder = ImmutableArray.CreateBuilder<EnumMemberDeclarationSyntax>(values.Length);

			foreach (var value in values)
			{
				var enumEqualsValue = EmitEnumEqualsValue(value.Value, enumTypeName);
				var member = EnumMemberDeclaration(value.Name)
					.WithEqualsValue(enumEqualsValue);

				builder.Add(member);
			}

			return builder.ToArray();
		}

		private static EqualsValueClauseSyntax EmitEnumEqualsValue(long value, string enumTypeName)
		{
			var literalToken = enumTypeName switch
			{
				"int" => Literal((int)value),
				"uint" => Literal((uint)value),
				_ => throw new NotImplementedException($"The enum type is not yet supported: {enumTypeName}.")
			};

			return EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, literalToken));
		}

		private static void EmitPseudoEnums(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpEnum> pseudoEnums)
		{
			foreach (var pseudoEnum in pseudoEnums)
			{
				EmitPseudoEnum(builder, pseudoEnum);
			}
		}

		private static void EmitPseudoEnum(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			CSharpEnum pseudoEnum)
		{
			var hasAddedLocationComment = false;
			foreach (var pseudoEnumConstant in pseudoEnum.Values)
			{
				if (!hasAddedLocationComment)
				{
					hasAddedLocationComment = true;
					var code = $@"
{pseudoEnum.CodeLocationComment.Replace("Enum ", $"Pseudo enum '{pseudoEnum.Name}' ")}
public const uint {pseudoEnumConstant.Name} = {pseudoEnumConstant.Value};
";
					var member = ParseMemberCode<FieldDeclarationSyntax>(code);
					builder.Add(member);
				}
				else
				{
					var code = $@"
public const uint {pseudoEnumConstant.Name} = {pseudoEnumConstant.Value};
".TrimStart();
					var member = ParseMemberCode<FieldDeclarationSyntax>(code);
					builder.Add(member);
				}
			}
		}

		private static T ParseMemberCode<T>(string memberCode)
			where T : MemberDeclarationSyntax
		{
			var member = ParseMemberDeclaration(memberCode)!;
			if (member is T syntax)
			{
				return syntax;
			}

			var up = new CSharpCodeGenerationException($"Error generating C# code for {typeof(T).Name}.");
			throw up;
		}
	}
}
