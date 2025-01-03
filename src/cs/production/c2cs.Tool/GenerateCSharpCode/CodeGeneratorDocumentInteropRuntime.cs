// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace C2CS.GenerateCSharpCode;

public sealed class CodeGeneratorDocumentInteropRuntime
{
    public CodeProjectDocument Generate(CodeGeneratorDocumentOptions options)
    {
        var codeTemplate = $"""
                   // <auto-generated>
                   //  This code was generated by the following tool on {options.DateTimeStamp}:
                   //      https://github.com/bottlenoselabs/c2cs (v{options.VersionStamp})
                   //
                   //  Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
                   // </auto-generated>
                   // ReSharper disable All

                   // To disable generating this file set `isEnabledGeneratingRuntimeCode` to `false` in the config file for generating C# code.

                   using System;
                   using System.Globalization;
                   using System.Runtime.InteropServices;

                   namespace Interop.Runtime{(options.IsEnabledFileScopedNamespace ? ";" : "{}")}

                   """;

        var compilationUnitRoot = SyntaxFactory.ParseSyntaxTree(codeTemplate).GetCompilationUnitRoot();
        var rootNamespaceOriginal = (BaseNamespaceDeclarationSyntax)compilationUnitRoot.Members[0];
        var members = CreateMembers();
        var rootNamespaceWithMembers = rootNamespaceOriginal
            .WithMembers(rootNamespaceOriginal.Members.AddRange(members));
        var code = compilationUnitRoot
            .ReplaceNode(rootNamespaceOriginal, rootNamespaceWithMembers)
            .GetCode();

        var document = new CodeProjectDocument
        {
            FileName = "Runtime.g.cs",
            Code = code
        };

        return document;
    }

    private static ImmutableArray<MemberDeclarationSyntax> CreateMembers()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcesNames = assembly.GetManifestResourceNames();
        var builderMembers = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

        foreach (var resourceName in resourcesNames)
        {
            CreateMember(resourceName, assembly, builderMembers);
        }

        return builderMembers.ToImmutable();
    }

    private static void CreateMember(
        string resourceName,
        Assembly assembly,
        ImmutableArray<MemberDeclarationSyntax>.Builder builderMembers)
    {
        if (!resourceName.EndsWith(".cs", StringComparison.InvariantCulture))
        {
            return;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var streamReader = new StreamReader(stream!);
        var fileContents = streamReader
            .ReadToEnd()
            .Replace("\nnamespace Interop.Runtime;\n", string.Empty, StringComparison.InvariantCulture);

        var syntaxTree = SyntaxFactory.ParseSyntaxTree(fileContents);
        if (syntaxTree.GetRoot() is not CompilationUnitSyntax compilationUnit)
        {
            return;
        }

        foreach (var member in compilationUnit.Members)
        {
            builderMembers.Add(member);
        }
    }
}