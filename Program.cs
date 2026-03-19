using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length == 0 || HasHelpFlag(args))
{
    PrintHelp();
    return;
}

var command = args[0].ToLowerInvariant();
var commandArgs = args.Skip(1).ToArray();

switch (command)
{
    case "import-men":
        await RunImportMenAsync(commandArgs);
        break;
    case "seedweight-men":
        await RunSeedweightMenAsync(commandArgs);
        break;
    case "scrape":
        await RunScrapeAsync(commandArgs);
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        PrintHelp();
        Environment.ExitCode = 1;
        break;
}

return;

static async Task RunScrapeAsync(string[] args)
{
    var options = ScrapeOptions.Parse(args);

    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
        "sweet16/0.1 (+https://github.com/your-org/sweet16)");
    httpClient.Timeout = TimeSpan.FromSeconds(30);

    var scraper = new NcaaTournamentScraper(httpClient);

    try
    {
        var menTask = scraper.GetEntrantsAsync(TournamentType.Mens, options.Year, options.Verbose);
        var womenTask = scraper.GetEntrantsAsync(TournamentType.Womens, options.Year, options.Verbose);

        await Task.WhenAll(menTask, womenTask);

        var result = new TournamentSeedPull(options.Year, menTask.Result, womenTask.Result);
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(result, JsonOptions.Default));

        Console.WriteLine($"Saved tournament seeds to {outputPath}");

        if (!options.Quiet)
        {
            Console.WriteLine();
            PrintTournament(result);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}

static async Task RunImportMenAsync(string[] args)
{
    try
    {
        var options = ImportMenOptions.Parse(args);
        var importer = new MensTournamentImporter();
        var file = await importer.ImportAsync(options);

        var outputPath = Path.GetFullPath(options.OutputPath ?? MensTournamentImporter.BuildDefaultOutputPath(file.Year));
        var outputDirectory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(file, JsonOptions.Default));

        Console.WriteLine($"Saved men's tournament entrants to {outputPath}");
        Console.WriteLine($"Year: {file.Year}");
        Console.WriteLine($"Teams: {file.Teams.Count}");

        if (!options.Quiet)
        {
            Console.WriteLine();
            foreach (var team in file.Teams)
            {
                Console.WriteLine($"{team.Seed,2}  {team.Team}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}

static async Task RunSeedweightMenAsync(string[] args)
{
    try
    {
        var options = SeedweightMenOptions.Parse(args);
        var inputPath = Path.GetFullPath(options.InputPath);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input JSON file was not found.", inputPath);
        }

        var json = await File.ReadAllTextAsync(inputPath);
        var field = JsonSerializer.Deserialize<MensTournamentFieldFile>(json, JsonOptions.Default)
            ?? throw new InvalidOperationException("Could not parse the men's tournament file.");

        var simulator = new SeedweightMensSimulator();
        var simulation = simulator.Simulate(field, options.RandomSeed);

        var outputPath = Path.GetFullPath(options.OutputPath ?? SeedweightMensSimulator.BuildDefaultOutputPath(field.Year));
        var outputDirectory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(simulation, JsonOptions.Default));

        Console.WriteLine($"Saved seedweight scenario to {outputPath}");
        Console.WriteLine($"Champion: {simulation.Champion.Team} ({simulation.Champion.Seed})");

        if (!options.Quiet)
        {
            Console.WriteLine();
            foreach (var game in simulation.Games)
            {
                Console.WriteLine(
                    $"{game.Round,-11} {game.Region,-8} G{game.GameNumber}: {game.TeamA.Team} ({game.TeamA.Seed}) vs {game.TeamB.Team} ({game.TeamB.Seed}) -> {game.Winner.Team} ({game.Winner.Seed})");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}

static void PrintTournament(TournamentSeedPull pull)
{
    Console.WriteLine($"sweet16 NCAA tournament entrants for {pull.Year}");
    Console.WriteLine();

    PrintDivision("Men", pull.Mens);
    Console.WriteLine();
    PrintDivision("Women", pull.Womens);
}

static void PrintDivision(string label, IReadOnlyList<TeamSeed> entrants)
{
    Console.WriteLine(label);
    Console.WriteLine(new string('-', label.Length));

    foreach (var team in entrants.OrderBy(entry => entry.Seed).ThenBy(entry => entry.Team))
    {
        Console.WriteLine($"{team.Seed,2}  {team.Team}");
    }

    Console.WriteLine();
    Console.WriteLine($"Total teams: {entrants.Count}");
}

static bool HasHelpFlag(IEnumerable<string> args) =>
    args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase));

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  sweet16 import-men --csv <path> [--year <year>] [--out <path>] [--quiet]");
    Console.WriteLine("  sweet16 seedweight-men --input <path> [--out <path>] [--seed <number>] [--quiet]");
    Console.WriteLine("  sweet16 scrape [--year <year>] [--out <path>] [--quiet] [--verbose]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  import-men   Import a men's NCAA tournament CSV and save a normalized entrant file.");
    Console.WriteLine("  seedweight-men   Simulate a men's tournament scenario using seedweight random draws.");
    Console.WriteLine("  scrape   Pull NCAA men's and women's tournament entrants and seeds from official NCAA pages and save them to a JSON file.");
}

enum TournamentType
{
    Mens,
    Womens
}

sealed record ScrapeOptions(int Year, string OutputPath, bool Quiet, bool Verbose)
{
    public static ScrapeOptions Parse(string[] args)
    {
        var year = DateTime.UtcNow.Year;
        string? outputPath = null;
        var quiet = false;
        var verbose = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--year":
                case "-y":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out year))
                    {
                        throw new ArgumentException("Expected a numeric year after --year.");
                    }
                    break;
                case "--out":
                case "-o":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("Expected a file path after --out.");
                    }

                    outputPath = args[++index];
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown scrape argument '{argument}'. Try 'sweet16 --help'.");
            }
        }

        outputPath ??= Path.Combine("data", $"ncaa-tournament-seeds-{year}.json");

        return new ScrapeOptions(year, outputPath, quiet, verbose);
    }
}

sealed record ImportMenOptions(string CsvPath, int? Year, string? OutputPath, bool Quiet)
{
    public static ImportMenOptions Parse(string[] args)
    {
        string? csvPath = null;
        int? year = null;
        string? outputPath = null;
        var quiet = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--csv":
                case "-c":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("Expected a file path after --csv.");
                    }

                    csvPath = args[++index];
                    break;
                case "--year":
                case "-y":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out var parsedYear))
                    {
                        throw new ArgumentException("Expected a numeric year after --year.");
                    }

                    year = parsedYear;
                    break;
                case "--out":
                case "-o":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("Expected a file path after --out.");
                    }

                    outputPath = args[++index];
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown import-men argument '{argument}'. Try 'sweet16 --help'.");
            }
        }

        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("The --csv option is required.");
        }

        return new ImportMenOptions(csvPath, year, outputPath, quiet);
    }
}

sealed record SeedweightMenOptions(string InputPath, string? OutputPath, int? RandomSeed, bool Quiet)
{
    public static SeedweightMenOptions Parse(string[] args)
    {
        string? inputPath = null;
        string? outputPath = null;
        int? randomSeed = null;
        var quiet = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--input":
                case "-i":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("Expected a file path after --input.");
                    }

                    inputPath = args[++index];
                    break;
                case "--out":
                case "-o":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("Expected a file path after --out.");
                    }

                    outputPath = args[++index];
                    break;
                case "--seed":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out var parsedSeed))
                    {
                        throw new ArgumentException("Expected a numeric value after --seed.");
                    }

                    randomSeed = parsedSeed;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown seedweight-men argument '{argument}'. Try 'sweet16 --help'.");
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("The --input option is required.");
        }

        return new SeedweightMenOptions(inputPath, outputPath, randomSeed, quiet);
    }
}

sealed class MensTournamentImporter
{
    public async Task<MensTournamentFieldFile> ImportAsync(ImportMenOptions options)
    {
        var csvPath = Path.GetFullPath(options.CsvPath);

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("CSV file was not found.", csvPath);
        }

        var lines = await File.ReadAllLinesAsync(csvPath);

        if (lines.Length <= 1)
        {
            throw new InvalidOperationException("The CSV file is empty.");
        }

        var rows = ParseRows(lines).ToArray();
        var year = options.Year ?? rows.Max(row => row.Year);

        var teams = BuildTeams(rows, year);

        if (teams.Length < 64)
        {
            throw new InvalidOperationException(
                $"Only found {teams.Length} teams for year {year} in CURRENT ROUND 64. Expected at least 64.");
        }

        return new MensTournamentFieldFile(
            "mens",
            year,
            Path.GetFileName(csvPath),
            DateTimeOffset.Now,
            teams);
    }

    public static string BuildDefaultOutputPath(int year) =>
        Path.Combine("data", "mens", $"{year}.json");

    private static TeamSeed[] BuildTeams(IEnumerable<MenCsvRow> rows, int year)
    {
        var slotRows = rows
            .Where(row => row.Year == year && row.CurrentRound == 64)
            .OrderByDescending(row => row.ByYearNumber)
            .Chunk(2)
            .Select((chunk, index) =>
            {
                if (chunk.Length != 2)
                {
                    throw new InvalidOperationException($"Expected 2 rows in slot {index + 1}.");
                }

                return new MatchupSlotRow(
                    index + 1,
                    chunk[0].Team,
                    chunk[0].Seed,
                    chunk[1].Team,
                    chunk[1].Seed);
            })
            .ToArray();

        var regions = SplitRegions(slotRows);
        var teams = new List<TeamSeed>();

        foreach (var region in regions)
        {
            foreach (var slot in region.Slots)
            {
                var gameNumber = MapFirstRoundGame(slot.TeamASeed);

                teams.Add(new TeamSeed(slot.TeamA, slot.TeamASeed, region.RegionName, gameNumber));
                teams.Add(new TeamSeed(slot.TeamB, slot.TeamBSeed, region.RegionName, gameNumber));
            }
        }

        return teams
            .GroupBy(team => team.Team, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(team => team.Region, StringComparer.OrdinalIgnoreCase)
            .ThenBy(team => team.FirstRoundGame)
            .ThenBy(team => team.Seed)
            .ThenBy(team => team.Team, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<MenCsvRow> ParseRows(IEnumerable<string> lines)
    {
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = line.Split(',');
            if (cells.Length < 8)
            {
                continue;
            }

            yield return new MenCsvRow(
                Year: ParseInt(cells[0], "YEAR"),
                ByYearNumber: ParseInt(cells[1], "BY YEAR NO"),
                TeamNumber: ParseInt(cells[2], "TEAM NO"),
                Team: cells[3].Trim(),
                Seed: ParseInt(cells[4], "SEED"),
                CurrentRound: ParseInt(cells[6], "CURRENT ROUND"));
        }
    }

    private static RegionSlots[] SplitRegions(IReadOnlyList<MatchupSlotRow> slots)
    {
        var patterns = new[]
        {
            new[] { 1, 8, 5, 4, 6, 3, 7, 2 },
            new[] { 1, 1, 8, 5, 4, 6, 3, 7, 2 },
            new[] { 1, 8, 5, 4, 6, 6, 3, 7, 2 },
            new[] { 1, 1, 8, 5, 4, 6, 6, 3, 7, 2 }
        };

        var regions = new List<RegionSlots>();
        var index = 0;

        while (index < slots.Count)
        {
            var matchedPattern = patterns
                .Select(pattern => new
                {
                    Pattern = pattern,
                    Matches = index + pattern.Length <= slots.Count
                        && pattern.SequenceEqual(slots.Skip(index).Take(pattern.Length).Select(slot => slot.TeamASeed))
                })
                .Where(candidate => candidate.Matches)
                .OrderBy(candidate => candidate.Pattern.Length)
                .FirstOrDefault();

            if (matchedPattern is null)
            {
                throw new InvalidOperationException("Could not determine region boundaries from the men's bracket slot order.");
            }

            var regionSlots = slots.Skip(index).Take(matchedPattern.Pattern.Length).ToArray();
            regions.Add(new RegionSlots($"Region {regions.Count + 1}", regionSlots));
            index += matchedPattern.Pattern.Length;
        }

        if (regions.Count != 4)
        {
            throw new InvalidOperationException($"Expected 4 regions but found {regions.Count}.");
        }

        return regions.ToArray();
    }

    private static int MapFirstRoundGame(int favoredSeed) => favoredSeed switch
    {
        1 => 1,
        8 => 2,
        5 => 3,
        4 => 4,
        6 => 5,
        3 => 6,
        7 => 7,
        2 => 8,
        _ => throw new InvalidOperationException($"Unsupported favored seed '{favoredSeed}' in the men's bracket.")
    };

    private static int ParseInt(string value, string columnName)
    {
        if (!int.TryParse(value.Trim(), out var parsed))
        {
            throw new InvalidOperationException($"Could not parse {columnName} value '{value}'.");
        }

        return parsed;
    }
}

sealed class SeedweightMensSimulator
{
    public MensSeedweightSimulation Simulate(MensTournamentFieldFile field, int? randomSeed)
    {
        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        var games = new List<SimulatedGame>();

        var regionWinners = field.Teams
            .GroupBy(team => team.Region, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(regionGroup => SimulateRegion(regionGroup.Key ?? "Unknown", regionGroup.OrderBy(team => team.FirstRoundGame).ThenBy(team => team.Seed).ToArray(), random, games))
            .ToArray();

        if (regionWinners.Length != 4)
        {
            throw new InvalidOperationException($"Expected 4 regions but found {regionWinners.Length}.");
        }

        var semifinalOne = PlayGame("Final Four", "National", 1, regionWinners[0], regionWinners[1], random, games);
        var semifinalTwo = PlayGame("Final Four", "National", 2, regionWinners[2], regionWinners[3], random, games);
        var champion = PlayGame("Championship", "National", 1, semifinalOne, semifinalTwo, random, games);

        return new MensSeedweightSimulation(
            "seedweight",
            field.Year,
            field.Division,
            randomSeed,
            champion,
            games);
    }

    public static string BuildDefaultOutputPath(int year) =>
        Path.Combine(
            "data",
            "mens",
            "scenarios",
            $"{year}-seedweight-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

    private static TeamSeed SimulateRegion(string region, TeamSeed[] teams, Random random, List<SimulatedGame> games)
    {
        var firstRound = teams
            .GroupBy(team => team.FirstRoundGame)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var matchup = group.OrderBy(team => team.Seed).ThenBy(team => team.Team, StringComparer.OrdinalIgnoreCase).ToArray();
                if (matchup.Length == 2)
                {
                    return PlayGame("Round of 64", region, group.Key ?? 0, matchup[0], matchup[1], random, games);
                }

                if (matchup.Length == 3)
                {
                    var playInTeams = matchup
                        .GroupBy(team => team.Seed)
                        .Where(seedGroup => seedGroup.Count() == 2)
                        .SelectMany(seedGroup => seedGroup)
                        .OrderBy(team => team.Team, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var lockedTeam = matchup
                        .FirstOrDefault(team => playInTeams.All(playInTeam => !string.Equals(playInTeam.Team, team.Team, StringComparison.OrdinalIgnoreCase)));

                    if (playInTeams.Length != 2 || lockedTeam is null)
                    {
                        throw new InvalidOperationException($"Could not resolve the play-in structure for {region} first-round game {group.Key}.");
                    }

                    var playInWinner = playInTeams[random.Next(playInTeams.Length)];
                    return PlayGame("Round of 64", region, group.Key ?? 0, lockedTeam, playInWinner, random, games);
                }

                throw new InvalidOperationException($"Expected 2 or 3 teams in {region} first-round game {group.Key}.");
            })
            .ToArray();

        var secondRound = new[]
        {
            PlayGame("Round of 32", region, 1, firstRound[0], firstRound[1], random, games),
            PlayGame("Round of 32", region, 2, firstRound[2], firstRound[3], random, games),
            PlayGame("Round of 32", region, 3, firstRound[4], firstRound[5], random, games),
            PlayGame("Round of 32", region, 4, firstRound[6], firstRound[7], random, games)
        };

        var sweet16 = new[]
        {
            PlayGame("Sweet 16", region, 1, secondRound[0], secondRound[1], random, games),
            PlayGame("Sweet 16", region, 2, secondRound[2], secondRound[3], random, games)
        };

        return PlayGame("Elite 8", region, 1, sweet16[0], sweet16[1], random, games);
    }

    private static TeamSeed PlayGame(
        string round,
        string region,
        int gameNumber,
        TeamSeed teamA,
        TeamSeed teamB,
        Random random,
        List<SimulatedGame> games)
    {
        var lowerSeed = teamA.Seed <= teamB.Seed ? teamA : teamB;
        var higherSeed = teamA.Seed <= teamB.Seed ? teamB : teamA;
        var slots = lowerSeed.Seed + higherSeed.Seed;
        var weightedSeeds = new List<int>(slots);

        for (var index = 0; index < higherSeed.Seed; index++)
        {
            weightedSeeds.Add(lowerSeed.Seed);
        }

        while (weightedSeeds.Count < slots)
        {
            weightedSeeds.Add(higherSeed.Seed);
        }

        var selectedSeed = weightedSeeds[random.Next(weightedSeeds.Count)];
        var winner = selectedSeed == lowerSeed.Seed ? lowerSeed : higherSeed;

        games.Add(new SimulatedGame(
            round,
            region,
            gameNumber,
            teamA,
            teamB,
            winner,
            weightedSeeds.ToArray()));

        return winner;
    }
}

sealed class NcaaTournamentScraper(HttpClient httpClient)
{
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex TeamSeedRegex = new(
        @"\((?<seed>\d{1,2})\)\s*(?<team>.+?)\s+\d+",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<TeamSeed>> GetEntrantsAsync(TournamentType tournamentType, int year, bool verbose)
    {
        var articleUrl = await ResolveArticleUrlAsync(tournamentType, year, verbose);
        var html = await GetStringAsync(articleUrl);
        var plainText = NormalizeHtml(html);

        var lines = plainText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var entrantLines = ExtractEntrantLines(lines);
        var entrants = ParseEntrants(entrantLines, tournamentType);

        if (entrants.Count == 0)
        {
            throw new InvalidOperationException(
                $"No entrants were parsed for the {Label(tournamentType)} tournament from {articleUrl}.");
        }

        if (verbose)
        {
            Console.Error.WriteLine($"[{Label(tournamentType)}] Source: {articleUrl}");
        }

        return entrants
            .OrderBy(entry => entry.Seed)
            .ThenBy(entry => entry.Team, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string> ResolveArticleUrlAsync(TournamentType tournamentType, int year, bool verbose)
    {
        foreach (var candidate in BuildCandidateUrls(tournamentType, year))
        {
            try
            {
                var html = await GetStringAsync(candidate);

                if (LooksLikeTournamentSchedulePage(html))
                {
                    if (verbose)
                    {
                        Console.Error.WriteLine($"[{Label(tournamentType)}] Matched source candidate: {candidate}");
                    }

                    return candidate;
                }
            }
            catch
            {
                // Try the next official NCAA URL candidate.
            }
        }

        throw new InvalidOperationException(
            $"Could not locate an official NCAA schedule page for the {Label(tournamentType)} {year} tournament.");
    }

    private static IEnumerable<string> BuildCandidateUrls(TournamentType tournamentType, int year)
    {
        if (tournamentType == TournamentType.Mens)
        {
            yield return $"https://www.ncaa.com/news/basketball-men/mml-official-bracket/2022-04-05/2022-ncaa-tournament-bracket-schedule-scores-march-madness";
            yield return $"https://www.ncaa.com/news/basketball-men/article/{year}-03-20/{year}-march-madness-mens-ncaa-tournament-schedule-dates";
            yield return $"https://www.ncaa.com/news/basketball-men/article/{year - 1}-03-20/{year - 1}-march-madness-mens-ncaa-tournament-schedule-dates";
            yield return $"https://www.ncaa.com/news/basketball-men/article/{year - 2}-03-24/{year - 2}-march-madness-mens-ncaa-tournament-schedule-dates";
            yield return $"https://www.ncaa.com/news/basketball-men/mml-official-bracket/{year}-03-17/{year}-ncaa-printable-bracket-schedule-march-madness";
            yield return $"https://www.ncaa.com/news/basketball-men/article/{year}-03-17/{year}-ncaa-printable-bracket-schedule-march-madness";
        }
        else
        {
            yield return $"https://www.ncaa.com/news/basketball-women/article/{year}-01-06/{year}-ncaa-womens-basketball-tournament-bracket-schedule-dates-printable-pdf";
            yield return $"https://www.ncaa.com/news/basketball-women/article/{year}-03-23/{year}-march-madness-womens-ncaa-tournament-schedule-dates-times";
            yield return $"https://www.ncaa.com/news/basketball-women/article/{year}-03-21/{year}-march-madness-womens-ncaa-tournament-schedule-dates-times";
            yield return $"https://www.ncaa.com/news/basketball-women/article/{year - 1}-01-14/{year - 1}-march-madness-womens-ncaa-tournament-schedule-dates-times";
            yield return $"https://www.ncaa.com/news/basketball-women/article/{year - 1}-04-06/{year - 1}-march-madness-womens-ncaa-tournament-schedule-dates-times";
            yield return $"https://www.ncaa.com/news/basketball-women/article/{year}-03-16/ucla-south-carolina-texas-and-southern-california-named-top-seeds-68-team";
        }
    }

    private static bool LooksLikeTournamentSchedulePage(string html)
    {
        var text = NormalizeHtml(html);

        return text.Contains("First Four", StringComparison.OrdinalIgnoreCase)
            && text.Contains("First Round", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Second Round", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractEntrantLines(IEnumerable<string> lines)
    {
        var inRelevantSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var normalizedLine = line.ToLowerInvariant();

            if (normalizedLine.Contains("first four"))
            {
                inRelevantSection = true;
            }

            if (inRelevantSection && (normalizedLine.Contains("second round") || normalizedLine.Contains("round of 32")))
            {
                yield break;
            }

            if (inRelevantSection && line.Contains('(') && line.Contains(')') && line.Contains(','))
            {
                yield return line;
            }
        }
    }

    private static IReadOnlyList<TeamSeed> ParseEntrants(IEnumerable<string> lines, TournamentType tournamentType)
    {
        var entrants = new Dictionary<string, TeamSeed>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var matches = TeamSeedRegex.Matches(line);

            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                var team = CleanTeamName(match.Groups["team"].Value, tournamentType);
                var seed = int.Parse(match.Groups["seed"].Value);

                if (!entrants.TryAdd(team, new TeamSeed(team, seed)))
                {
                    var existing = entrants[team];
                    entrants[team] = existing with { Seed = Math.Min(existing.Seed, seed) };
                }
            }
        }

        return entrants.Values.ToArray();
    }

    private static string CleanTeamName(string value, TournamentType tournamentType)
    {
        var team = value
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Trim();

        team = Regex.Replace(team, @"\s+\((OT|2OT|3OT)\)$", string.Empty, RegexOptions.IgnoreCase);
        team = Regex.Replace(team, @"\s{2,}", " ");

        if (tournamentType == TournamentType.Womens && team.Equals("Southern California", StringComparison.OrdinalIgnoreCase))
        {
            return "USC";
        }

        return team;
    }

    private async Task<string> GetStringAsync(string url)
    {
        using var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string NormalizeHtml(string html)
    {
        if (html.Contains("JavaScript is disabled", StringComparison.OrdinalIgnoreCase)
            || html.Contains("verify that you're not a robot", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "NCAA returned a bot-check page instead of tournament data. Retrying later or adding a browser-backed fetcher may be necessary.");
        }

        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h1>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h2>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h3>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h4>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</tr>", "\n", StringComparison.OrdinalIgnoreCase);

        normalized = TagRegex.Replace(normalized, " ");
        normalized = WebUtility.HtmlDecode(normalized);

        var builder = new StringBuilder();
        foreach (var line in normalized.Split('\n'))
        {
            var collapsed = WhitespaceRegex.Replace(line, " ").Trim();
            if (collapsed.Length > 0)
            {
                builder.AppendLine(collapsed);
            }
        }

        return builder.ToString();
    }

    private static string Label(TournamentType tournamentType) =>
        tournamentType == TournamentType.Mens ? "men's" : "women's";
}

sealed record TeamSeed(string Team, int Seed, string? Region = null, int? FirstRoundGame = null);

sealed record MenCsvRow(int Year, int ByYearNumber, int TeamNumber, string Team, int Seed, int CurrentRound);

sealed record MatchupSlotRow(int SlotNumber, string TeamA, int TeamASeed, string TeamB, int TeamBSeed);

sealed record RegionSlots(string RegionName, IReadOnlyList<MatchupSlotRow> Slots);

sealed record MensTournamentFieldFile(
    string Division,
    int Year,
    string SourceFile,
    DateTimeOffset ImportedAt,
    IReadOnlyList<TeamSeed> Teams);

sealed record SimulatedGame(
    string Round,
    string Region,
    int GameNumber,
    TeamSeed TeamA,
    TeamSeed TeamB,
    TeamSeed Winner,
    IReadOnlyList<int> WeightedSeedArray);

sealed record MensSeedweightSimulation(
    string Method,
    int Year,
    string Division,
    int? RandomSeed,
    TeamSeed Champion,
    IReadOnlyList<SimulatedGame> Games);

sealed record TournamentSeedPull(int Year, IReadOnlyList<TeamSeed> Mens, IReadOnlyList<TeamSeed> Womens);

static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
