// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using c2ffi.Data;
using c2ffi.Data.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace C2CS.GenerateCSharpCode;

public sealed class CodeGeneratorDocumentPInvoke
{
    public CodeProjectDocument Generate(
        CodeGeneratorDocumentOptions options,
        CodeGeneratorContext context,
        CFfiCrossPlatform ffi)
    {
        var codeTemplate = $$"""
                    // <auto-generated>
                    //  This code was generated by the following tool on {{options.DateTimeStamp}}:
                    //      https://github.com/bottlenoselabs/c2cs (v{{options.VersionStamp}})
                    //
                    //  Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                    // </auto-generated>
                    // ReSharper disable All

                    #region Template
                    {{(options.IsEnabledNullables ? string.Empty : "\n#nullable enable")}}
                    #pragma warning disable CS1591
                    #pragma warning disable CS8981
                    using Interop.Runtime;
                    using System;
                    using System.Collections.Generic;
                    using System.Globalization;
                    using System.Runtime.InteropServices;
                    using System.Runtime.CompilerServices;

                    #endregion
                    {{options.CodeRegionHeader}}
                    namespace {{options.NamespaceName}}{{(options.IsEnabledFileScopedNamespace ? ";" : " {")}}

                    public static unsafe partial class {{options.ClassName}}
                    {
                        private const string LibraryName = "{{options.LibraryName}}";
                    }

                    {{(options.IsEnabledFileScopedNamespace ? string.Empty : "}")}}
                    {{options.CodeRegionFooter}}
                    """;

        var compilationUnit = SyntaxFactory
            .ParseSyntaxTree(codeTemplate)
            .GetCompilationUnitRoot();
        var rootClassDeclaration = compilationUnit
            .DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault() ?? throw new InvalidOperationException("Class declaration is null.");
        var rootClassDeclarationWithMembers = rootClassDeclaration;

        var members = GenerateClassMembers(context, ffi);
        rootClassDeclarationWithMembers = rootClassDeclarationWithMembers.AddMembers([.. members]);

        var newCompilationUnit = compilationUnit.ReplaceNode(
            rootClassDeclaration,
            rootClassDeclarationWithMembers);
        var code = newCompilationUnit.GetCode();

        var codeDocument = new CodeProjectDocument
        {
            FileName = $"{options.ClassName}.g.cs",
            Code = code
        };

        return codeDocument;
    }

    private ImmutableArray<MemberDeclarationSyntax> GenerateClassMembers(
        CodeGeneratorContext context, CFfiCrossPlatform ffi)
    {
        var members = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

        // NOTE: Function pointers need to be processed first because their types are used in a function, function parameter, struct, type alias, or otherwise recursive.
        var functionPointers = ProcessCNodes<CFunctionPointer, MemberDeclarationSyntax>(
            context, ffi.FunctionPointers.Values);
        var structs = ProcessCNodes<CRecord, StructDeclarationSyntax>(
            context, ffi.Records.Values);
        var functions = ProcessCNodes<CFunction, MethodDeclarationSyntax>(
            context, ffi.Functions.Values);
        // ProcessCNodes(context, ffi.Variables.Values);
        var enums = ProcessCNodes<CEnum, EnumDeclarationSyntax>(
            context, ffi.Enums.Values);
        var opaqueTypes = ProcessCNodes<COpaqueType, StructDeclarationSyntax>(
            context, ffi.OpaqueTypes.Values);
        var macroObjects = ProcessCNodes<CMacroObject, FieldDeclarationSyntax>(
            context, ffi.MacroObjects.Values);
        // NOTE: Type aliases need to be processed last because they often will have the same name (collision) with another type, usually an enum or a record.
        //  This happens because, e.g., the enum type will be `enum MY_ENUM` and the type alias name would be `MY_ENUM` but we want both to have the name `MY_ENUM`.
        //  When this happens, the type alias is skipped (not generated) because the aliased type already exists when mapped to C#.
        var aliasTypes = ProcessCNodes<CTypeAlias, StructDeclarationSyntax>(
            context, ffi.TypeAliases.Values);

        // NOTE: The order they are added effects the order they appear in the generated C# file.
        members.AddRange(functions);
        members.AddRange(macroObjects);
        members.AddRange(structs);
        members.AddRange(enums);
        members.AddRange(opaqueTypes);
        members.AddRange(aliasTypes);
        members.AddRange(functionPointers);

        return members.ToImmutable();
    }

    private ImmutableArray<TMemberDeclarationSyntax> ProcessCNodes<TCNode, TMemberDeclarationSyntax>(
        CodeGeneratorContext context, IEnumerable<TCNode> cNodes)
        where TCNode : CNode
        where TMemberDeclarationSyntax : MemberDeclarationSyntax
    {
        var builder = ImmutableArray.CreateBuilder<TMemberDeclarationSyntax>();

        foreach (var cNode in cNodes)
        {
            var memberSyntax = context.ProcessCNode<TCNode, TMemberDeclarationSyntax>(cNode);
            if (memberSyntax == null)
            {
                continue;
            }

            builder.Add(memberSyntax);
        }

        return builder.ToImmutable();
    }
}
