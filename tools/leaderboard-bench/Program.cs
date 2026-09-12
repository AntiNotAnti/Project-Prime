using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectPrime.LeaderboardBench;

internal static class Program
{
    // CareerQueries.Take(count + 1) fetches one sentinel row for the cursor;
    // the HTTP response trims it back to this public page size.
    private const int LeaderboardLimit = 25;
    private const int QueryTake = LeaderboardLimit + 1;
    private const int DefaultRows = 100_000;
    private const int DefaultIterations = 10;
    private const int DefaultSeed = 20260911;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            using CancellationTokenSource stop = new();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };
            await RunAsync(options, stop.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("leaderboard benchmark cancelled");
            return 130;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"leaderboard benchmark failed: {error.Message}");
            return 2;
        }
    }

    private static async Task RunAsync(Options options, CancellationToken cancellationToken)
    {
        string connectionFile = Path.GetFullPath(options.ConnectionFile);
        if (!File.Exists(connectionFile))
            throw new FileNotFoundException("The PostgreSQL connection-string file was not found.", connectionFile);
        if ((File.GetAttributes(connectionFile) & FileAttributes.Directory) != 0)
            throw new IOException("The PostgreSQL connection-string path is a directory.");

        string connectionString = (await File.ReadAllTextAsync(connectionFile, cancellationToken)).Trim();
        if (connectionString.Length == 0)
            throw new InvalidDataException("The PostgreSQL connection-string file is empty.");
        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "ProjectPrimeLeaderboardBench"
        };

        await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        string serverVersion = await ScalarAsync(connection, null, "SHOW server_version", cancellationToken) as string ?? "unknown";
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, """
            CREATE TEMP TABLE prime_bench_profiles
            (
                player_id uuid PRIMARY KEY,
                display_name text NOT NULL
            ) ON COMMIT DROP;
            CREATE TEMP TABLE prime_bench_hunter_licenses
            (
                player_id uuid PRIMARY KEY,
                rating_points integer NOT NULL
            ) ON COMMIT DROP;
            CREATE TEMP TABLE prime_bench_career_aggregates
            (
                player_id uuid NOT NULL,
                trust_class integer NOT NULL,
                dimension text NOT NULL,
                key text NOT NULL,
                kills bigint NOT NULL,
                deaths bigint NOT NULL,
                wins bigint NOT NULL,
                matches bigint NOT NULL,
                outcome_samples bigint NOT NULL,
                headshot_kills bigint NOT NULL,
                octolith_scores bigint NOT NULL,
                nodes_captured bigint NOT NULL,
                kills_as_prime bigint NOT NULL,
                PRIMARY KEY (player_id, trust_class, dimension, key)
            ) ON COMMIT DROP;
            -- These are the two existing career aggregate indexes, reproduced
            -- for measurement. This tool does not change the application schema.
            CREATE INDEX prime_bench_career_kills
                ON prime_bench_career_aggregates (trust_class, dimension, kills, player_id);
            CREATE INDEX prime_bench_career_wins
                ON prime_bench_career_aggregates (trust_class, dimension, wins, player_id);
            """, cancellationToken);

        SeedData seed = Seed(options.Rows, options.Seed);
        await InsertAsync(connection, transaction, seed, cancellationToken);
        await ExecuteAsync(connection, transaction, "ANALYZE prime_bench_profiles; ANALYZE prime_bench_hunter_licenses; ANALYZE prime_bench_career_aggregates;", cancellationToken);

        var metrics = new List<MetricReport>(2);
        if (options.Metric is "rp" or "both")
            metrics.Add(await MeasureAsync(connection, transaction, "rp", options.Iterations, cancellationToken));
        if (options.Metric is "career" or "both")
            metrics.Add(await MeasureAsync(connection, transaction, "career", options.Iterations, cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        var report = new
        {
            Tool = "ProjectPrimeLeaderboardBench",
            Schema = "temporary tables shaped after CareerQueries.LeaderboardAsync",
            ServerVersion = serverVersion,
            options.Seed,
            options.Rows,
            options.Iterations,
            WarmupIterations = 1,
            Limit = LeaderboardLimit,
            QueryTake,
            Metrics = metrics
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SeedData Seed(int rows, int seed)
    {
        var ids = new Guid[rows];
        var names = new string[rows];
        var ratings = new int[rows];
        var kills = new long[rows];
        var deaths = new long[rows];
        var wins = new long[rows];
        var matches = new long[rows];
        var samples = new long[rows];
        var headshots = new long[rows];
        var octolithScores = new long[rows];
        var nodesCaptured = new long[rows];
        var killsAsPrime = new long[rows];
        for (int i = 0; i < rows; i++)
        {
            byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes($"project-prime-leaderboard:{seed}:{i}"));
            ids[i] = new Guid(identity.AsSpan(0, 16));
            names[i] = $"Bench{i:D8}";
            long value = unchecked((long)seed * 1_000_003L + i * 97_003L);
            long positive = value & long.MaxValue;
            ratings[i] = (int)(positive % 1_001);
            kills[i] = 10 + positive % 5_000;
            deaths[i] = 1 + (positive / 7) % 3_000;
            wins[i] = positive % 100;
            matches[i] = 10 + positive % 250;
            samples[i] = matches[i];
            headshots[i] = positive % 1_000;
            octolithScores[i] = positive % 2_000;
            nodesCaptured[i] = positive % 500;
            killsAsPrime[i] = positive % 750;
        }
        return new(ids, names, ratings, kills, deaths, wins, matches, samples, headshots,
            octolithScores, nodesCaptured, killsAsPrime);
    }

    private static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeedData seed, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO prime_bench_profiles (player_id, display_name)
                SELECT x.player_id, x.display_name
                FROM unnest(@ids::uuid[], @names::text[]) AS x(player_id, display_name);
            INSERT INTO prime_bench_hunter_licenses (player_id, rating_points)
                SELECT x.player_id, x.rating_points
                FROM unnest(@ids::uuid[], @ratings::integer[]) AS x(player_id, rating_points);
            INSERT INTO prime_bench_career_aggregates
                (player_id, trust_class, dimension, key, kills, deaths, wins, matches,
                 outcome_samples, headshot_kills, octolith_scores, nodes_captured, kills_as_prime)
                SELECT x.player_id, -1, 'career', '', x.kills, x.deaths, x.wins, x.matches,
                       x.outcome_samples, x.headshots, x.octolith_scores, x.nodes_captured, x.kills_as_prime
                FROM unnest(@ids::uuid[], @kills::bigint[], @deaths::bigint[], @wins::bigint[],
                            @matches::bigint[], @samples::bigint[], @headshots::bigint[],
                            @octolith_scores::bigint[], @nodes_captured::bigint[], @kills_as_prime::bigint[])
                     AS x(player_id, kills, deaths, wins, matches, outcome_samples, headshots,
                          octolith_scores, nodes_captured, kills_as_prime);
            """;
        AddArray(command, "ids", NpgsqlDbType.Uuid, seed.Ids);
        AddArray(command, "names", NpgsqlDbType.Text, seed.Names);
        AddArray(command, "ratings", NpgsqlDbType.Integer, seed.Ratings);
        AddArray(command, "kills", NpgsqlDbType.Bigint, seed.Kills);
        AddArray(command, "deaths", NpgsqlDbType.Bigint, seed.Deaths);
        AddArray(command, "wins", NpgsqlDbType.Bigint, seed.Wins);
        AddArray(command, "matches", NpgsqlDbType.Bigint, seed.Matches);
        AddArray(command, "samples", NpgsqlDbType.Bigint, seed.Samples);
        AddArray(command, "headshots", NpgsqlDbType.Bigint, seed.Headshots);
        AddArray(command, "octolith_scores", NpgsqlDbType.Bigint, seed.OctolithScores);
        AddArray(command, "nodes_captured", NpgsqlDbType.Bigint, seed.NodesCaptured);
        AddArray(command, "kills_as_prime", NpgsqlDbType.Bigint, seed.KillsAsPrime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddArray<T>(NpgsqlCommand command, string name, NpgsqlDbType elementType, T[] value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Array | elementType).Value = value;
    }

    private static async Task<MetricReport> MeasureAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string metric, int iterations, CancellationToken cancellationToken)
    {
        string query = metric == "rp" ? """
            EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
            SELECT l.player_id, p.display_name,
                   CAST(l.rating_points AS numeric) AS score
            FROM prime_bench_hunter_licenses l
            JOIN prime_bench_profiles p ON p.player_id = l.player_id
            ORDER BY CAST(l.rating_points AS numeric) DESC, l.player_id ASC
            LIMIT 26;
            """ : """
            EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
            SELECT a.player_id, p.display_name, a.kills, a.deaths, a.wins,
                   a.matches, a.outcome_samples AS attributed_matches,
                   CAST(a.kills AS numeric) AS score
            FROM prime_bench_career_aggregates a
            JOIN prime_bench_profiles p ON p.player_id = a.player_id
            WHERE a.trust_class = -1 AND a.dimension = 'career'
            ORDER BY CAST(a.kills AS numeric) DESC, a.player_id ASC
            LIMIT 26;
            """;
        ValidateQueryShape(metric, query);
        var times = new List<double>(iterations);
        JsonElement plan = default;
        string topNode = "unknown";
        string planHash = "unknown";
        for (int i = 0; i <= iterations; i++)
        {
            string raw = Convert.ToString(await ScalarAsync(connection, transaction, query, cancellationToken),
                CultureInfo.InvariantCulture) ?? throw new InvalidDataException("PostgreSQL returned no query plan.");
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement summary = document.RootElement[0];
            double elapsed = summary.GetProperty("Execution Time").GetDouble();
            JsonElement currentPlan = summary.GetProperty("Plan");
            if (i > 0) times.Add(elapsed);
            plan = currentPlan.Clone();
            topNode = currentPlan.TryGetProperty("Node Type", out JsonElement node)
                ? node.GetString() ?? "unknown" : "unknown";
            planHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(currentPlan.GetRawText())))
                .ToLowerInvariant();
        }
        times.Sort();
        return new(metric, times[PercentileIndex(times.Count, 0.50)],
            times[PercentileIndex(times.Count, 0.95)], topNode, planHash, plan);
    }

    private static void ValidateQueryShape(string metric, string query)
    {
        if (Occurrences(query, "JOIN prime_bench_profiles p") != 1
            || !query.Contains("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)", StringComparison.Ordinal)
            || !query.Contains($"LIMIT {QueryTake}", StringComparison.Ordinal))
            throw new InvalidDataException($"The {metric} benchmark query shape is invalid.");
        if (metric == "rp" && (!query.Contains("rating_points", StringComparison.Ordinal)
            || !query.Contains("CAST(l.rating_points AS numeric)", StringComparison.Ordinal)))
            throw new InvalidDataException("The RP benchmark query is missing rating_points.");
        if (metric == "career" && (!query.Contains("prime_bench_career_aggregates a", StringComparison.Ordinal)
            || !query.Contains("a.trust_class = -1", StringComparison.Ordinal)
            || !query.Contains("a.dimension = 'career'", StringComparison.Ordinal)
            || !query.Contains("CAST(a.kills AS numeric)", StringComparison.Ordinal)))
            throw new InvalidDataException("The career benchmark query is missing the official career filter.");
    }

    private static int Occurrences(string value, string needle)
    {
        int count = 0, start = 0;
        while (true)
        {
            int match = value.IndexOf(needle, start, StringComparison.Ordinal);
            if (match < 0) return count;
            count++;
            start = match + needle.Length;
        }
    }

    private static int PercentileIndex(int count, double percentile)
        => Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record MetricReport(string Metric, double P50ExecutionMs, double P95ExecutionMs,
        string TopNode, string QueryPlanSha256, JsonElement Plan);

    private sealed record SeedData(Guid[] Ids, string[] Names, int[] Ratings, long[] Kills, long[] Deaths,
        long[] Wins, long[] Matches, long[] Samples, long[] Headshots, long[] OctolithScores,
        long[] NodesCaptured, long[] KillsAsPrime);

    private sealed record Options(string ConnectionFile, int Rows, int Iterations, int Seed, string Metric)
    {
        public static Options Parse(string[] args)
        {
            string? connectionFile = Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE");
            int rows = DefaultRows;
            int iterations = DefaultIterations;
            int seed = DefaultSeed;
            string metric = "both";
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (name is "--help" or "-h")
                {
                    Console.WriteLine("Usage: dotnet run --project tools/leaderboard-bench -- [--connection-file PATH] [--rows N] [--iterations N] [--seed N] [--metric rp|career|both]");
                    Environment.Exit(0);
                }
                string value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {name}.");
                switch (name)
                {
                    case "--connection-file": connectionFile = value; break;
                    case "--rows": rows = ParseInt(value, name, 1, 1_000_000); break;
                    case "--iterations": iterations = ParseInt(value, name, 1, 100); break;
                    case "--seed": seed = ParseInt(value, name, int.MinValue, int.MaxValue); break;
                    case "--metric" when value is "rp" or "career" or "both": metric = value; break;
                    case "--metric": throw new ArgumentException("--metric must be rp, career, or both.");
                    default: throw new ArgumentException($"Unknown option {name}.");
                }
            }
            if (string.IsNullOrWhiteSpace(connectionFile))
                throw new ArgumentException("Set PRIME_TEST_POSTGRES_FILE or pass --connection-file PATH.");
            return new(connectionFile, rows, iterations, seed, metric);
        }

        private static int ParseInt(string value, string name, int minimum, int maximum)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                || parsed < minimum || parsed > maximum)
                throw new ArgumentException($"{name} must be an integer in [{minimum}, {maximum}].");
            return parsed;
        }
    }
}
