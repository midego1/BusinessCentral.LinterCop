﻿using System.Collections.Immutable;
using BusinessCentral.LinterCop.Helpers;
using Microsoft.Dynamics.Nav.CodeAnalysis;
using Microsoft.Dynamics.Nav.CodeAnalysis.Diagnostics;

namespace BusinessCentral.LinterCop.Design;

[DiagnosticAnalyzer]
public class Rule0006FieldNotAutoIncrementInTemporaryTable : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.Rule0006FieldNotAutoIncrementInTemporaryTable);

    public override void Initialize(AnalysisContext context) =>
        context.RegisterSymbolAction(new Action<SymbolAnalysisContext>(this.AnalyzeTemporaryTables), SymbolKind.Table);

    private void AnalyzeTemporaryTables(SymbolAnalysisContext ctx)
    {
        if (ctx.IsObsoletePendingOrRemoved() || ctx.Symbol is not ITableTypeSymbol table)
            return;

        if (table.TableType != TableTypeKind.Temporary)
            return;

        foreach (var field in table.Fields)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();

            var autoIncrementProperty = field.GetProperty(PropertyKind.AutoIncrement);
            if (autoIncrementProperty is not null &&
                 autoIncrementProperty.Value is bool isAutoIncrement && isAutoIncrement)

                ctx.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.Rule0006FieldNotAutoIncrementInTemporaryTable,
                    autoIncrementProperty.GetLocation()));
        }
    }
}