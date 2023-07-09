// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using C2CS.Features.WriteCodeCSharp.Data;
using C2CS.Features.WriteCodeCSharp.Domain.CodeGenerator;
using C2CS.Features.WriteCodeCSharp.Domain.CodeGenerator.Diagnostics;
using C2CS.Features.WriteCodeCSharp.Domain.Mapper;
using C2CS.Features.WriteCodeCSharp.Input;
using C2CS.Features.WriteCodeCSharp.Input.Sanitized;
using C2CS.Features.WriteCodeCSharp.Input.Unsanitized;
using C2CS.Features.WriteCodeCSharp.Output;
using C2CS.Foundation.Tool;
using CAstFfi.Data;
using CAstFfi.Data.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace C2CS.Features.WriteCodeCSharp;

public sealed class WriteCodeCSharpTool : Tool<WriteCSharpCodeOptions, WriteCodeCSharpInput, WriteCodeCSharpOutput>
{
    private readonly IServiceProvider _services;

    public WriteCodeCSharpTool(
        ILogger<WriteCodeCSharpTool> logger,
        WriteCodeCSharpInputSanitizer inputSanitizer,
        IServiceProvider services,
        IFileSystem fileSystem)
        : base(logger, inputSanitizer, fileSystem)
    {
        _services = services;
    }

    protected override void Execute(WriteCodeCSharpInput input, WriteCodeCSharpOutput output)
    {
        var abstractSyntaxTreesC = LoadCAbstractSyntaxTree(input.InputFilePath);

        var nodesPerPlatform = MapCNodesToCSharpNodes(
            abstractSyntaxTreesC,
            input.MapperOptions);

        var project = GenerateCSharpLibrary(nodesPerPlatform, input.GeneratorOptions);
        WriteFilesToStorage(input.OutputFileDirectory, project);

        if (input.GeneratorOptions.IsEnabledVerifyCSharpCodeCompiles)
        {
            VerifyCSharpCodeCompiles(input.OutputFileDirectory, project);
        }
    }

    private CAbstractSyntaxTreeCrossPlatform LoadCAbstractSyntaxTree(string filePath)
    {
        BeginStep("Load C abstract syntax tree");

        var ast = CJsonSerializer.ReadAbstractSyntaxTreeCrossPlatform(filePath);

        EndStep();

        return ast;
    }

    private CSharpAbstractSyntaxTree MapCNodesToCSharpNodes(
        CAbstractSyntaxTreeCrossPlatform abstractSyntaxTree,
        CSharpCodeMapperOptions options)
    {
        BeginStep("Map C syntax tree nodes to C#");

        var mapper = new CSharpCodeMapper(options);
        var result = mapper.Map(Diagnostics, abstractSyntaxTree);

        EndStep();

        return result;
    }

    private CSharpProject GenerateCSharpLibrary(
        CSharpAbstractSyntaxTree abstractSyntaxTree,
        CSharpCodeGeneratorOptions options)
    {
        BeginStep("Generate C# library files");

        var codeGenerator = new CSharpCodeGenerator(_services, options);
        var project = codeGenerator.Emit(abstractSyntaxTree);

        EndStep();

        return project;
    }

    private void WriteFilesToStorage(
        string outputFileDirectory,
        CSharpProject project)
    {
        BeginStep("Write generated files to storage");

        foreach (var document in project.Documents)
        {
            var fullFilePath = Path.GetFullPath(Path.Combine(outputFileDirectory, document.FileName));
            File.WriteAllText(fullFilePath, document.Contents);
        }

        EndStep();
    }

    private void VerifyCSharpCodeCompiles(string outputFilePath, CSharpProject project)
    {
        BeginStep("Verify C# code compiles");

        var cSharpDocuments = project.Documents.Where(x =>
            x.FileName.EndsWith(".cs", StringComparison.InvariantCulture));

        var syntaxTrees = new List<SyntaxTree>();
        foreach (var cSharpDocument in cSharpDocuments)
        {
            var sharpSyntaxTree = CSharpSyntaxTree.ParseText(cSharpDocument.Contents);
            syntaxTrees.Add(sharpSyntaxTree);
        }

        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithPlatform(Platform.AnyCpu)
            .WithAllowUnsafe(true);
        var compilation = CSharpCompilation.Create(
            "TestAssemblyName",
            syntaxTrees,
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            compilationOptions);
        using var dllStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitResult = compilation.Emit(dllStream, pdbStream);

        foreach (var diagnostic in emitResult.Diagnostics)
        {
            // Obviously errors should be considered, but should warnings be considered too? Yes, yes they should. Some warnings can be indicative of bindings which are not correct.
            var isErrorOrWarning = diagnostic.Severity is
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;
            if (!isErrorOrWarning)
            {
                continue;
            }

            Diagnostics.Add(new CSharpCompileDiagnostic(outputFilePath, diagnostic));
        }

        EndStep();
    }
}
