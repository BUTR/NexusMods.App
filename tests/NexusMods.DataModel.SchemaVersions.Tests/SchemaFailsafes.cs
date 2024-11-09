using System.Text;
using FluentAssertions;
using NexusMods.DataModel.SchemaVersions;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;

namespace NexusMods.DataModel.Migrations.Tests;

public class SchemaFailsafes(IConnection connection)
{

    [Fact]
    public async Task SchemaFingerprintHasntChanged()
    {
        var db = connection.Db;
        var records = db.AttributeCache.AllAttributeIds
            .OrderBy(id => id.Id, StringComparer.Ordinal)
            .Select(id =>
                {
                    var aid = db.AttributeCache.GetAttributeId(id);
                    return new string[]
                    {
                        id.ToString(),
                        db.AttributeCache.GetValueTag(aid).ToString(),
                        db.AttributeCache.IsIndexed(aid).ToString(),
                        db.AttributeCache.IsCardinalityMany(aid).ToString(),
                        db.AttributeCache.IsNoHistory(aid).ToString(),
                    };
                }
            ).ToArray();

        var prefix = $"""
                     ## NexusMods app schema
                     This schema is written to a markdown file for both documentation and validation reasons. DO NOT EDIT THIS FILE MANUALLY. Instead change the
                     models in the app, then validate the tests to update this file. 
                     
                     ## Statistics
                        - Fingerprint: {SchemaFingerprint.GenerateFingerprint(db)}
                        - Total attributes: {records.Length}
                        - Total namespaces: {db.AttributeCache.AllAttributeIds.Select(id => id.Namespace).Distinct().Count()}
                        
                     ## Attributes
                     """;

        await VerifyTable(prefix, records, "AttributeId", "Type",
            "Indexed", "Many", "NoHistory"
        ).UseFileName("Schema");
    }

    private SettingsTask VerifyTable(string prefix, string[][] rows, params string[] columnNames)
    {
        var rowsMaxSize = columnNames
            .Select((name, index) => Math.Max(rows.Max(row => row[index].Length), name.Length)) 
            .ToArray();
        
        var sb = new StringBuilder();
        sb.AppendLine(prefix);
        sb.Append("| ");
        for (var i = 0; i < columnNames.Length; i++)
        {
            sb.Append(columnNames[i].PadRight(rowsMaxSize[i]));
            sb.Append(" | ");
        }
        sb.AppendLine();
        sb.Append("| ");
        for (var i = 0; i < columnNames.Length; i++)
        {
            sb.Append(new string('-', rowsMaxSize[i]));
            sb.Append(" | ");
        }
        sb.AppendLine();
        
        foreach (var row in rows)
        {
            sb.Append("| ");
            for (var i = 0; i < row.Length; i++)
            {
                sb.Append(row[i].PadRight(rowsMaxSize[i]));
                sb.Append(" | ");
            }
            sb.AppendLine();
        }
        
        return Verify(sb.ToString(), extension: "md");
    }
    
}
