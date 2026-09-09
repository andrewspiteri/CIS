using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Data.Sqlite;

namespace Cis.Modules.Graph;

public sealed class SqliteGraphStore
{
    public const int StorageSchemaVersion = 5;
    public const string DatabaseRelativePath = ".cis/local/graph/context.db";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string DatabasePath(string repositoryPath)
        => Path.Combine(repositoryPath, DatabaseRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public GraphStoreBuildState? ReadBuildState(string repositoryPath)
    {
        var path = DatabasePath(repositoryPath);
        if (!File.Exists(path)) return null;
        using var connection = Open(path, readOnly: true);
        EnsureReadableSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT schema_version, build_id, repository_id, head, dirty, status,
                   documentation_root, node_count, edge_count
            FROM graph_builds WHERE singleton = 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new GraphStoreBuildState(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4) != 0,
            reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8));
    }

    public GraphStoreReadResult Read(string repositoryPath)
        => GraphReadScope.Read(this, nameof(Read), repositoryPath, () => ReadCore(repositoryPath));

    private GraphStoreReadResult ReadCore(string repositoryPath)
    {
        var path = DatabasePath(repositoryPath);
        if (!File.Exists(path)) return GraphStoreReadResult.Failure("SQLite graph database is missing.");
        try
        {
            using var connection = Open(path, readOnly: true);
            EnsureReadableSchema(connection);
            var state = ReadBuildState(connection);
            if (state is null) return GraphStoreReadResult.Failure("SQLite graph build metadata is missing.");
            var evidence = ReadEvidence(connection);
            var nodes = ReadNodes(connection, null, evidence);
            var edges = ReadEdges(connection, null, evidence);
            var diagnostics = ReadDiagnostics(connection);
            var inputs = ReadInputs(connection);
            var extractors = ReadExtractors(connection);
            var graph = new CisGraphDocument(
                state.SchemaVersion,
                new CisGraphBuildMetadata(state.BuildId, state.RepositoryId, state.Head, state.Dirty, state.Status),
                nodes,
                edges,
                new CisGraphDiagnosticSummary(
                    diagnostics.Count(item => item.Severity == "error"),
                    diagnostics.Count(item => item.Severity == "warning"),
                    diagnostics.Count(item => item.Severity is "info" or "information")));
            var manifest = new CisGraphManifest(
                state.SchemaVersion, state.BuildId, state.RepositoryId, state.DocumentationRoot,
                state.Head, state.Dirty, inputs, extractors);
            return new GraphStoreReadResult(true, graph, manifest, diagnostics, null);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            return GraphStoreReadResult.Failure(exception.Message);
        }
    }

    public GraphStoreHeaderResult ReadHeader(string repositoryPath)
        => GraphReadScope.Read(this, nameof(ReadHeader), repositoryPath, () => ReadHeaderCore(repositoryPath));

    private GraphStoreHeaderResult ReadHeaderCore(string repositoryPath)
    {
        var path = DatabasePath(repositoryPath);
        if (!File.Exists(path)) return GraphStoreHeaderResult.Failure("SQLite graph database is missing.");
        try
        {
            using var connection = Open(path, readOnly: true);
            EnsureReadableSchema(connection);
            var state = ReadBuildState(connection);
            if (state is null) return GraphStoreHeaderResult.Failure("SQLite graph build metadata is missing.");
            return new GraphStoreHeaderResult(
                true,
                new CisGraphBuildMetadata(state.BuildId, state.RepositoryId, state.Head, state.Dirty, state.Status),
                new CisGraphManifest(state.SchemaVersion, state.BuildId, state.RepositoryId, state.DocumentationRoot,
                    state.Head, state.Dirty, ReadInputs(connection), ReadExtractors(connection)),
                ReadDiagnostics(connection),
                state.NodeCount,
                state.EdgeCount,
                null);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            return GraphStoreHeaderResult.Failure(exception.Message);
        }
    }

    public void Write(
        string repositoryPath,
        CisGraphDocument graph,
        CisGraphManifest manifest,
        IReadOnlyList<CisGraphDiagnostic> diagnostics)
    {
        using var timing = CisPerformanceTrace.Start("sqlite.write");
        var path = DatabasePath(repositoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existed = File.Exists(path);
        var replaceIncompatible = existed && ReadStorageVersion(path) is < 1 or > StorageSchemaVersion;
        var writePath = replaceIncompatible ? path + "." + Guid.NewGuid().ToString("N") + ".tmp" : path;
        try
        {
            using (var connection = Open(writePath, readOnly: false))
            {
                using (var pragma = connection.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
                    pragma.ExecuteNonQuery();
                }
                ApplyMigrations(connection);
                using var transaction = connection.BeginTransaction();
                InsertBuild(connection, transaction, graph, manifest);
                InsertInputs(connection, transaction, manifest.Inputs);
                InsertExtractors(connection, transaction, manifest.Extractors);
                InsertDiagnostics(connection, transaction, diagnostics);
                InsertGraph(connection, transaction, graph);
                using var check = connection.CreateCommand();
                check.Transaction = transaction;
                check.CommandText = "PRAGMA foreign_key_check;";
                using var reader = check.ExecuteReader();
                if (reader.Read()) throw new InvalidDataException("SQLite graph foreign-key validation failed.");
                reader.Close();
                transaction.Commit();
                using var reclaim = connection.CreateCommand();
                reclaim.CommandText = "PRAGMA incremental_vacuum(256);";
                reclaim.ExecuteNonQuery();
            }
            if (replaceIncompatible) File.Move(writePath, path, overwrite: true);
        }
        catch
        {
            if (replaceIncompatible && File.Exists(writePath)) File.Delete(writePath);
            if (!existed && File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public IReadOnlyList<CisGraphNode> FindNodes(
        string repositoryPath,
        string? id,
        string? kind,
        string? subtype,
        string? facet,
        string? text,
        int limit)
    {
        using var connection = Open(DatabasePath(repositoryPath), readOnly: true);
        EnsureReadableSchema(connection);
        using var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(id))
        {
            clauses.Add("(n.key = $id COLLATE NOCASE OR n.local_id = $id COLLATE NOCASE)");
            command.Parameters.AddWithValue("$id", id);
        }
        AddExact(clauses, command, "n.kind", "$kind", kind);
        AddExact(clauses, command, "n.subtype", "$subtype", subtype);
        if (!string.IsNullOrWhiteSpace(facet))
        {
            clauses.Add("EXISTS (SELECT 1 FROM node_facets f WHERE f.node_key = n.key AND f.facet = $facet COLLATE NOCASE)");
            command.Parameters.AddWithValue("$facet", facet);
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (text.Length >= 3)
            {
                clauses.Add("EXISTS (SELECT 1 FROM node_search s WHERE s.rowid=n.rowid AND node_search MATCH $text)");
                command.Parameters.AddWithValue("$text", "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
            }
            else
            {
                clauses.Add("(n.label LIKE $text ESCAPE '\\' COLLATE NOCASE OR n.local_id LIKE $text ESCAPE '\\' COLLATE NOCASE OR n.properties_json LIKE $text ESCAPE '\\' COLLATE NOCASE)");
                command.Parameters.AddWithValue("$text", "%" + EscapeLike(text) + "%");
            }
        }
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = "SELECT n.key FROM nodes n" // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- clauses are fixed SQL with bound values
            + (clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses))
            + " ORDER BY n.key LIMIT $limit;";
        var keys = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) keys.Add(reader.GetString(0));
        return ReadSelectedNodes(connection, keys);
    }

    public IReadOnlyList<CisGraphNode> ResolveNodes(string repositoryPath, string id, string? kind)
        => FindNodes(repositoryPath, id, kind, null, null, null, 1001);

    public IReadOnlyList<CisGraphNode> ReadNodes(string repositoryPath, IEnumerable<string> keys)
    {
        using var connection = Open(DatabasePath(repositoryPath), readOnly: true);
        EnsureReadableSchema(connection);
        return ReadSelectedNodes(connection, keys.Distinct(StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<CisGraphEdge> ReadAdjacentEdges(
        string repositoryPath,
        string nodeKey,
        string direction,
        IReadOnlyList<string> edgeTypes,
        bool includeProposed)
    {
        using var connection = Open(DatabasePath(repositoryPath), readOnly: true);
        EnsureReadableSchema(connection);
        using var command = connection.CreateCommand();
        var directionClause = direction switch
        {
            "in" => "e.to_hash = $nodeHash AND e.to_key = $node",
            "out" => "e.from_hash = $nodeHash AND e.from_key = $node",
            _ => "((e.from_hash = $nodeHash AND e.from_key = $node) OR (e.to_hash = $nodeHash AND e.to_key = $node))",
        };
        command.Parameters.AddWithValue("$node", nodeKey);
        command.Parameters.AddWithValue("$nodeHash", KeyHash(nodeKey));
        var states = includeProposed
            ? "('declared','discovered','confirmed','proposed')"
            : "('declared','discovered','confirmed')";
        var typeClause = string.Empty;
        if (edgeTypes.Count > 0)
        {
            var names = new List<string>();
            for (var index = 0; index < edgeTypes.Count; index++)
            {
                var name = "$type" + index;
                names.Add(name);
                command.Parameters.AddWithValue(name, edgeTypes[index]);
            }
            typeClause = " AND e.type IN (" + string.Join(',', names) + ")";
        }
        // Every SQL fragment is selected from a closed switch or generated parameter name.
        // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli
        command.CommandText = $"SELECT e.key FROM edges e WHERE {directionClause} AND e.state IN {states}{typeClause} ORDER BY e.key;";
        var keys = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) keys.Add(reader.GetString(0));
        var evidence = ReadEvidence(connection);
        return ReadEdges(connection, keys, evidence);
    }

    private static void InsertBuild(SqliteConnection connection, SqliteTransaction transaction, CisGraphDocument graph, CisGraphManifest manifest)
    {
        Execute(connection, transaction, "INSERT INTO repositories(repository_id, documentation_root) VALUES($repository,$docs) ON CONFLICT(repository_id) DO UPDATE SET documentation_root=excluded.documentation_root;",
            ("$repository", manifest.RepositoryId), ("$docs", manifest.DocumentationRoot));
        Execute(connection, transaction, """
            INSERT INTO graph_builds(singleton, schema_version, build_id, repository_id, head, dirty, status,
                                     documentation_root, node_count, edge_count)
            VALUES(1,$schema,$build,$repository,$head,$dirty,$status,$docs,$nodes,$edges)
            ON CONFLICT(singleton) DO UPDATE SET schema_version=excluded.schema_version,build_id=excluded.build_id,
              repository_id=excluded.repository_id,head=excluded.head,dirty=excluded.dirty,status=excluded.status,
              documentation_root=excluded.documentation_root,node_count=excluded.node_count,edge_count=excluded.edge_count;
            """,
            ("$schema", graph.SchemaVersion), ("$build", graph.Build.Id), ("$repository", graph.Build.RepositoryId),
            ("$head", graph.Build.Head), ("$dirty", graph.Build.Dirty ? 1 : 0), ("$status", graph.Build.Status),
            ("$docs", manifest.DocumentationRoot), ("$nodes", graph.Nodes.Count), ("$edges", graph.Edges.Count));
    }

    private static void InsertInputs(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<CisGraphInput> inputs)
    {
        Execute(connection, transaction, "DELETE FROM source_inputs;");
        for (var index = 0; index < inputs.Count; index++)
            Execute(connection, transaction, "INSERT INTO source_inputs(ordinal,path,hash) VALUES($ordinal,$path,$hash);",
                ("$ordinal", index), ("$path", inputs[index].Path), ("$hash", inputs[index].Hash));
    }

    private static void InsertExtractors(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> extractors)
    {
        Execute(connection, transaction, "DELETE FROM extractors;");
        for (var index = 0; index < extractors.Count; index++)
            Execute(connection, transaction, "INSERT INTO extractors(ordinal,name) VALUES($ordinal,$name);",
                ("$ordinal", index), ("$name", extractors[index]));
    }

    private static void InsertDiagnostics(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<CisGraphDiagnostic> diagnostics)
    {
        Execute(connection, transaction, "DELETE FROM diagnostics;");
        for (var index = 0; index < diagnostics.Count; index++)
            Execute(connection, transaction, "INSERT INTO diagnostics(ordinal,code,severity,message,evidence_json) VALUES($ordinal,$code,$severity,$message,$evidence);",
                ("$ordinal", index), ("$code", diagnostics[index].Code), ("$severity", diagnostics[index].Severity),
                ("$message", diagnostics[index].Message), ("$evidence", JsonSerializer.Serialize(diagnostics[index].Evidence, JsonOptions)));
    }

    private static void InsertGraph(SqliteConnection connection, SqliteTransaction transaction, CisGraphDocument graph)
    {
        Execute(connection, transaction, "CREATE TEMP TABLE desired_nodes(key TEXT PRIMARY KEY); CREATE TEMP TABLE desired_edges(key TEXT PRIMARY KEY);");
        foreach (var node in graph.Nodes)
            Execute(connection, transaction, "INSERT INTO desired_nodes(key) VALUES($key);", ("$key", node.Key));
        foreach (var edge in graph.Edges)
            Execute(connection, transaction, "INSERT INTO desired_edges(key) VALUES($key);", ("$key", edge.Key));
        Execute(connection, transaction, "DELETE FROM edges WHERE key NOT IN (SELECT key FROM desired_edges);");
        Execute(connection, transaction, "DELETE FROM node_search WHERE rowid NOT IN (SELECT rowid FROM nodes WHERE key IN (SELECT key FROM desired_nodes));");
        Execute(connection, transaction, "DELETE FROM nodes WHERE key NOT IN (SELECT key FROM desired_nodes);");

        var existingNodeHashes = ReadHashes(connection, transaction, "nodes");
        var existingEdgeHashes = ReadHashes(connection, transaction, "edges");
        var evidenceIds = ReadEvidenceIdentities(connection, transaction);
        foreach (var node in graph.Nodes)
        {
            var contentHash = ContentHash(node);
            if (existingNodeHashes.TryGetValue(node.Key, out var existingHash)
                && string.Equals(existingHash, contentHash, StringComparison.Ordinal)) continue;
            Execute(connection, transaction, """
                INSERT INTO nodes(key,repository_id,kind,subtype,local_id,label,authority,lifecycle,properties_json,content_hash)
                VALUES($key,$repository,$kind,$subtype,$local,$label,$authority,$lifecycle,$properties,$contentHash)
                ON CONFLICT(key) DO UPDATE SET repository_id=excluded.repository_id,kind=excluded.kind,
                  subtype=excluded.subtype,local_id=excluded.local_id,label=excluded.label,
                  authority=excluded.authority,lifecycle=excluded.lifecycle,
                  properties_json=excluded.properties_json,content_hash=excluded.content_hash;
                """, ("$key", node.Key), ("$repository", node.RepositoryId), ("$kind", node.Kind),
                ("$subtype", node.Subtype), ("$local", node.LocalId), ("$label", node.Label),
                ("$authority", node.Authority), ("$lifecycle", node.Lifecycle),
                ("$properties", JsonSerializer.Serialize(node.Properties, JsonOptions)), ("$contentHash", contentHash));
            Execute(connection, transaction, "DELETE FROM node_facets WHERE node_key=$key; DELETE FROM node_locations WHERE node_key=$key; DELETE FROM node_evidence WHERE node_key=$key; DELETE FROM node_search WHERE rowid=(SELECT rowid FROM nodes WHERE key=$key);", ("$key", node.Key));
            for (var index = 0; index < node.Facets.Count; index++)
                Execute(connection, transaction, "INSERT INTO node_facets(node_key,ordinal,facet) VALUES($key,$ordinal,$facet);",
                    ("$key", node.Key), ("$ordinal", index), ("$facet", node.Facets[index]));
            for (var index = 0; index < node.Locations.Count; index++)
            {
                var location = node.Locations[index];
                Execute(connection, transaction, "INSERT INTO node_locations(node_key,ordinal,path,locator_kind,locator_value) VALUES($key,$ordinal,$path,$kind,$value);",
                    ("$key", node.Key), ("$ordinal", index), ("$path", location.Path),
                    ("$kind", location.LocatorKind), ("$value", location.LocatorValue));
            }
            InsertEvidenceLinks(connection, transaction, "node_evidence", "node_key", node.Key, node.Provenance, evidenceIds);
            var searchText = string.Join('\n', new[] { node.LocalId, node.Label }
                .Concat(node.Properties.SelectMany(pair => new[] { pair.Key, pair.Value })));
            Execute(connection, transaction, "INSERT INTO node_search(rowid,search_text) SELECT rowid,$text FROM nodes WHERE key=$key;",
                ("$key", node.Key), ("$text", searchText));
        }
        foreach (var edge in graph.Edges)
        {
            var contentHash = ContentHash(edge);
            if (existingEdgeHashes.TryGetValue(edge.Key, out var existingHash)
                && string.Equals(existingHash, contentHash, StringComparison.Ordinal)) continue;
            Execute(connection, transaction, """
                INSERT INTO edges(key,type,from_key,to_key,from_hash,to_hash,state,confidence,properties_json,content_hash)
                VALUES($key,$type,$from,$to,$fromHash,$toHash,$state,$confidence,$properties,$contentHash)
                ON CONFLICT(key) DO UPDATE SET type=excluded.type,from_key=excluded.from_key,to_key=excluded.to_key,
                  from_hash=excluded.from_hash,to_hash=excluded.to_hash,state=excluded.state,
                  confidence=excluded.confidence,properties_json=excluded.properties_json,
                  content_hash=excluded.content_hash;
                """, ("$key", edge.Key), ("$type", edge.Type), ("$from", edge.From), ("$to", edge.To),
                ("$fromHash", KeyHash(edge.From)), ("$toHash", KeyHash(edge.To)),
                ("$state", edge.State), ("$confidence", edge.Confidence),
                ("$properties", JsonSerializer.Serialize(edge.Properties, JsonOptions)), ("$contentHash", contentHash));
            Execute(connection, transaction, "DELETE FROM edge_evidence WHERE edge_key=$key;", ("$key", edge.Key));
            InsertEvidenceLinks(connection, transaction, "edge_evidence", "edge_key", edge.Key, edge.Observations, evidenceIds);
        }
        Execute(connection, transaction, "DELETE FROM evidence WHERE id NOT IN (SELECT evidence_id FROM node_evidence UNION SELECT evidence_id FROM edge_evidence); DROP TABLE desired_edges; DROP TABLE desired_nodes;");
    }

    private static void InsertEvidenceLinks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        string ownerKey,
        IReadOnlyList<CisGraphEvidence> items,
        IDictionary<string, long> evidenceIds)
    {
        ValidateSqlIdentifiers(table, ownerColumn);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var identity = JsonSerializer.Serialize(item, JsonOptions);
            if (!evidenceIds.TryGetValue(identity, out var evidenceId))
            {
                Execute(connection, transaction, """
                    INSERT INTO evidence(source_kind,path,locator_kind,locator_value,content_hash,method,extractor,confidence,confidence_reason)
                    VALUES($source,$path,$locatorKind,$locatorValue,$hash,$method,$extractor,$confidence,$reason);
                    """, ("$source", item.SourceKind), ("$path", item.Path), ("$locatorKind", item.LocatorKind),
                    ("$locatorValue", item.LocatorValue), ("$hash", item.ContentHash), ("$method", item.Method),
                    ("$extractor", item.Extractor), ("$confidence", item.Confidence), ("$reason", item.ConfidenceReason));
                using var id = connection.CreateCommand();
                id.Transaction = transaction;
                id.CommandText = "SELECT last_insert_rowid();";
                evidenceId = (long)id.ExecuteScalar()!;
                evidenceIds[identity] = evidenceId;
            }
            // Identifiers are constrained by ValidateSqlIdentifiers.
            // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli
            Execute(connection, transaction, $"INSERT INTO {table}({ownerColumn},ordinal,evidence_id) VALUES($owner,$ordinal,$evidence);",
                ("$owner", ownerKey), ("$ordinal", index), ("$evidence", evidenceId));
        }
    }

    private static Dictionary<string, string> ReadHashes(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        ValidateSqlIdentifiers(table);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The table identifier is constrained by ValidateSqlIdentifiers.
        // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli
        command.CommandText = $"SELECT key,content_hash FROM {table};";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static Dictionary<string, long> ReadEvidenceIdentities(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,source_kind,path,locator_kind,locator_value,content_hash,method,extractor,confidence,confidence_reason FROM evidence;";
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = new CisGraphEvidence(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9));
            result[JsonSerializer.Serialize(item, JsonOptions)] = reader.GetInt64(0);
        }
        return result;
    }

    private static string ContentHash<T>(T value)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, JsonOptions))));

    private static byte[] KeyHash(string value)
        => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static GraphStoreBuildState? ReadBuildState(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version,build_id,repository_id,head,dirty,status,documentation_root,node_count,edge_count FROM graph_builds WHERE singleton=1;";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new GraphStoreBuildState(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4) != 0,
                reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8))
            : null;
    }

    private static Dictionary<long, CisGraphEvidence> ReadEvidence(SqliteConnection connection)
    {
        var result = new Dictionary<long, CisGraphEvidence>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source_kind,path,locator_kind,locator_value,content_hash,method,extractor,confidence,confidence_reason FROM evidence;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result[reader.GetInt64(0)] = new CisGraphEvidence(
            reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9));
        return result;
    }

    private static IReadOnlyList<CisGraphNode> ReadSelectedNodes(SqliteConnection connection, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return [];
        var evidence = ReadEvidence(connection);
        return ReadNodes(connection, keys, evidence);
    }

    private static IReadOnlyList<CisGraphNode> ReadNodes(
        SqliteConnection connection,
        IReadOnlyList<string>? selectedKeys,
        IReadOnlyDictionary<long, CisGraphEvidence> evidence)
    {
        var facets = ReadStringChildren(connection, "node_facets", "node_key", "facet", selectedKeys);
        var locations = ReadLocations(connection, selectedKeys);
        var provenance = ReadEvidenceChildren(connection, "node_evidence", "node_key", selectedKeys, evidence);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key,repository_id,kind,subtype,local_id,label,authority,lifecycle,properties_json FROM nodes"
            + WhereKeys(command, "key", selectedKeys) + " ORDER BY key;";
        var result = new List<CisGraphNode>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            result.Add(new CisGraphNode(key, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), facets.GetValueOrDefault(key) ?? [],
                reader.GetString(6), reader.GetString(7), locations.GetValueOrDefault(key) ?? [],
                DeserializeDictionary(reader.GetString(8)), provenance.GetValueOrDefault(key) ?? []));
        }
        return result;
    }

    private static IReadOnlyList<CisGraphEdge> ReadEdges(
        SqliteConnection connection,
        IReadOnlyList<string>? selectedKeys,
        IReadOnlyDictionary<long, CisGraphEvidence> evidence)
    {
        if (selectedKeys is { Count: 0 }) return [];
        var observations = ReadEvidenceChildren(connection, "edge_evidence", "edge_key", selectedKeys, evidence);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key,type,from_key,to_key,state,confidence,properties_json FROM edges"
            + WhereKeys(command, "key", selectedKeys) + " ORDER BY key;";
        var result = new List<CisGraphEdge>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            result.Add(new CisGraphEdge(key, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), observations.GetValueOrDefault(key) ?? [],
                DeserializeDictionary(reader.GetString(6))));
        }
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadStringChildren(
        SqliteConnection connection, string table, string ownerColumn, string valueColumn, IReadOnlyList<string>? keys)
    {
        ValidateSqlIdentifiers(table, ownerColumn, valueColumn);
        using var command = connection.CreateCommand();
        // Identifiers are constrained by ValidateSqlIdentifiers.
        // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli
        command.CommandText = $"SELECT {ownerColumn},{valueColumn} FROM {table}" + WhereKeys(command, ownerColumn, keys) + $" ORDER BY {ownerColumn},ordinal;";
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) Add(grouped, reader.GetString(0), reader.GetString(1));
        return grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, IReadOnlyList<CisGraphLocation>> ReadLocations(SqliteConnection connection, IReadOnlyList<string>? keys)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT node_key,path,locator_kind,locator_value FROM node_locations"
            + WhereKeys(command, "node_key", keys) + " ORDER BY node_key,ordinal;";
        var grouped = new Dictionary<string, List<CisGraphLocation>>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) Add(grouped, reader.GetString(0), new CisGraphLocation(reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CisGraphLocation>)pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, IReadOnlyList<CisGraphEvidence>> ReadEvidenceChildren(
        SqliteConnection connection, string table, string ownerColumn, IReadOnlyList<string>? keys,
        IReadOnlyDictionary<long, CisGraphEvidence> evidence)
    {
        ValidateSqlIdentifiers(table, ownerColumn);
        using var command = connection.CreateCommand();
        // Identifiers are constrained by ValidateSqlIdentifiers.
        // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli
        command.CommandText = $"SELECT {ownerColumn},evidence_id FROM {table}" + WhereKeys(command, ownerColumn, keys) + $" ORDER BY {ownerColumn},ordinal;";
        var grouped = new Dictionary<string, List<CisGraphEvidence>>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) if (evidence.TryGetValue(reader.GetInt64(1), out var item)) Add(grouped, reader.GetString(0), item);
        return grouped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CisGraphEvidence>)pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<CisGraphDiagnostic> ReadDiagnostics(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT code,severity,message,evidence_json FROM diagnostics ORDER BY ordinal;";
        var result = new List<CisGraphDiagnostic>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new CisGraphDiagnostic(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            JsonSerializer.Deserialize<string[]>(reader.GetString(3), JsonOptions) ?? []));
        return result;
    }

    private static IReadOnlyList<CisGraphInput> ReadInputs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path,hash FROM source_inputs ORDER BY ordinal;";
        var result = new List<CisGraphInput>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new CisGraphInput(reader.GetString(0), reader.GetString(1)));
        return result;
    }

    private static IReadOnlyList<string> ReadExtractors(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM extractors ORDER BY ordinal;";
        var result = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static string WhereKeys(SqliteCommand command, string column, IReadOnlyList<string>? keys)
    {
        ValidateSqlIdentifiers(column);
        if (keys is null) return string.Empty;
        if (keys.Count == 0) return " WHERE 0";
        var parameters = new List<string>();
        for (var index = 0; index < keys.Count; index++)
        {
            var name = "$key" + index;
            parameters.Add(name);
            command.Parameters.AddWithValue(name, keys[index]);
        }
        // The column is constrained and values remain bound parameters.
        // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli
        return $" WHERE {column} IN ({string.Join(',', parameters)})";
    }

    private static void ValidateSqlIdentifiers(params string[] identifiers)
    {
        string[] allowed = ["nodes", "edges", "node_evidence", "edge_evidence", "node_facets", "node_key", "edge_key", "facet", "key"];
        foreach (var identifier in identifiers)
            if (!allowed.Contains(identifier, StringComparer.Ordinal))
                throw new InvalidOperationException($"Unsupported graph-store SQL identifier '{identifier}'.");
    }

    private static void AddExact(List<string> clauses, SqliteCommand command, string column, string parameter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        clauses.Add($"{column} = {parameter} COLLATE NOCASE");
        command.Parameters.AddWithValue(parameter, value);
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static void Add<T>(IDictionary<string, List<T>> values, string key, T value)
    {
        if (!values.TryGetValue(key, out var items)) values[key] = items = [];
        items.Add(value);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql; // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- private callers provide reviewed SQL and bind all values
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static int ReadStorageVersion(string path)
    {
        try
        {
            using var connection = Open(path, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static void EnsureReadableSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (version != StorageSchemaVersion)
            throw new InvalidDataException($"Unsupported SQLite graph schema {version}; expected {StorageSchemaVersion}. Run `cis graph build`.");
    }

    private static void ApplyMigrations(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (version == 0)
        {
            using var command = connection.CreateCommand();
            command.CommandText = SchemaVersion1;
            command.ExecuteNonQuery();
            version = 1;
        }
        if (version == 1)
        {
            using var command = connection.CreateCommand();
            command.CommandText = SchemaVersion2;
            command.ExecuteNonQuery();
            version = 2;
        }
        if (version == 2)
        {
            using var command = connection.CreateCommand();
            command.CommandText = SchemaVersion3;
            command.ExecuteNonQuery();
            version = 3;
        }
        if (version == 3)
        {
            using (var configure = connection.CreateCommand())
            {
                configure.CommandText = "PRAGMA auto_vacuum=INCREMENTAL;";
                configure.ExecuteNonQuery();
            }
            using (var vacuum = connection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
            using (var mark = connection.CreateCommand())
            {
                mark.CommandText = "PRAGMA user_version=4;";
                mark.ExecuteNonQuery();
            }
            version = 4;
        }
        if (version == 4)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = SchemaVersion5;
                command.ExecuteNonQuery();
            }
            using (var vacuum = connection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
            version = 5;
        }
        if (version != StorageSchemaVersion)
            throw new InvalidDataException($"Unsupported SQLite graph schema {version}; expected {StorageSchemaVersion}.");
    }

    private const string SchemaVersion1 = """
        CREATE TABLE repositories(repository_id TEXT PRIMARY KEY, documentation_root TEXT NOT NULL);
        CREATE TABLE graph_builds(
          singleton INTEGER PRIMARY KEY CHECK(singleton=1), schema_version INTEGER NOT NULL,
          build_id TEXT NOT NULL, repository_id TEXT NOT NULL REFERENCES repositories(repository_id),
          head TEXT NULL, dirty INTEGER NOT NULL CHECK(dirty IN (0,1)), status TEXT NOT NULL,
          documentation_root TEXT NOT NULL, node_count INTEGER NOT NULL, edge_count INTEGER NOT NULL);
        CREATE TABLE source_inputs(ordinal INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE, hash TEXT NOT NULL);
        CREATE TABLE extractors(ordinal INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE);
        CREATE TABLE diagnostics(ordinal INTEGER PRIMARY KEY, code TEXT NOT NULL, severity TEXT NOT NULL, message TEXT NOT NULL, evidence_json TEXT NOT NULL);
        CREATE TABLE nodes(
          key TEXT PRIMARY KEY, repository_id TEXT NOT NULL REFERENCES repositories(repository_id),
          kind TEXT NOT NULL, subtype TEXT NOT NULL, local_id TEXT NOT NULL, label TEXT NOT NULL,
          authority TEXT NOT NULL, lifecycle TEXT NOT NULL, properties_json TEXT NOT NULL,
          UNIQUE(repository_id,kind,local_id));
        CREATE TABLE node_facets(node_key TEXT NOT NULL REFERENCES nodes(key) ON DELETE CASCADE, ordinal INTEGER NOT NULL, facet TEXT NOT NULL, PRIMARY KEY(node_key,ordinal));
        CREATE TABLE node_locations(node_key TEXT NOT NULL REFERENCES nodes(key) ON DELETE CASCADE, ordinal INTEGER NOT NULL, path TEXT NOT NULL, locator_kind TEXT NOT NULL, locator_value TEXT NOT NULL, PRIMARY KEY(node_key,ordinal));
        CREATE TABLE evidence(
          id INTEGER PRIMARY KEY, source_kind TEXT NOT NULL, path TEXT NOT NULL, locator_kind TEXT NOT NULL,
          locator_value TEXT NOT NULL, content_hash TEXT NULL, method TEXT NOT NULL, extractor TEXT NOT NULL,
          confidence TEXT NOT NULL, confidence_reason TEXT NULL);
        CREATE TABLE node_evidence(node_key TEXT NOT NULL REFERENCES nodes(key) ON DELETE CASCADE, ordinal INTEGER NOT NULL, evidence_id INTEGER NOT NULL REFERENCES evidence(id), PRIMARY KEY(node_key,ordinal));
        CREATE TABLE edges(
          key TEXT PRIMARY KEY, type TEXT NOT NULL, from_key TEXT NOT NULL REFERENCES nodes(key),
          to_key TEXT NOT NULL REFERENCES nodes(key), state TEXT NOT NULL, confidence TEXT NOT NULL,
          properties_json TEXT NOT NULL);
        CREATE TABLE edge_evidence(edge_key TEXT NOT NULL REFERENCES edges(key) ON DELETE CASCADE, ordinal INTEGER NOT NULL, evidence_id INTEGER NOT NULL REFERENCES evidence(id), PRIMARY KEY(edge_key,ordinal));
        CREATE VIRTUAL TABLE node_search USING fts5(key UNINDEXED, search_text, tokenize='unicode61');
        CREATE INDEX ix_nodes_kind_subtype ON nodes(kind,subtype,key);
        CREATE INDEX ix_nodes_local_id ON nodes(local_id);
        CREATE INDEX ix_node_facets_facet ON node_facets(facet,node_key);
        CREATE INDEX ix_node_locations_path ON node_locations(path,node_key);
        CREATE INDEX ix_edges_from_type_state ON edges(from_key,type,state,to_key);
        CREATE INDEX ix_edges_to_type_state ON edges(to_key,type,state,from_key);
        CREATE INDEX ix_edges_type_state ON edges(type,state,key);
        CREATE INDEX ix_evidence_path ON evidence(path);
        PRAGMA user_version=1;
        """;

    private const string SchemaVersion2 = """
        ALTER TABLE nodes ADD COLUMN content_hash TEXT NOT NULL DEFAULT '';
        ALTER TABLE edges ADD COLUMN content_hash TEXT NOT NULL DEFAULT '';
        PRAGMA user_version=2;
        """;

    private const string SchemaVersion3 = """
        DROP INDEX ix_edges_from_type_state;
        DROP INDEX ix_edges_to_type_state;
        DROP INDEX ix_edges_type_state;
        ALTER TABLE edges ADD COLUMN from_hash BLOB NOT NULL DEFAULT X'';
        ALTER TABLE edges ADD COLUMN to_hash BLOB NOT NULL DEFAULT X'';
        UPDATE nodes SET content_hash='';
        UPDATE edges SET content_hash='';
        DROP TABLE node_search;
        CREATE VIRTUAL TABLE node_search USING fts5(search_text, content='', contentless_delete=1, tokenize='unicode61');
        CREATE INDEX ix_edges_from_hash_type_state ON edges(from_hash,type,state,to_hash);
        CREATE INDEX ix_edges_to_hash_type_state ON edges(to_hash,type,state,from_hash);
        CREATE INDEX ix_edges_type_state ON edges(type,state,key);
        PRAGMA user_version=3;
        """;

    private const string SchemaVersion5 = """
        UPDATE nodes SET content_hash='';
        DROP TABLE node_search;
        CREATE VIRTUAL TABLE node_search USING fts5(search_text, content='', contentless_delete=1, tokenize='trigram');
        PRAGMA user_version=5;
        """;
}

public sealed record GraphStoreBuildState(
    int SchemaVersion,
    string BuildId,
    string RepositoryId,
    string? Head,
    bool Dirty,
    string Status,
    string DocumentationRoot,
    int NodeCount,
    int EdgeCount);

public sealed record GraphStoreReadResult(
    bool Success,
    CisGraphDocument? Graph,
    CisGraphManifest? Manifest,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    string? Error)
{
    public static GraphStoreReadResult Failure(string error) => new(false, null, null, [], error);
}

public sealed record GraphStoreHeaderResult(
    bool Success,
    CisGraphBuildMetadata? Build,
    CisGraphManifest? Manifest,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    int NodeCount,
    int EdgeCount,
    string? Error)
{
    public static GraphStoreHeaderResult Failure(string error) => new(false, null, null, [], 0, 0, error);
}
