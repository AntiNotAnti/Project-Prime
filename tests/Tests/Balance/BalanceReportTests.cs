using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Collections.Immutable;
using MphRead.Identity;
using MphRead.Telemetry;
using MphRead.Tests.Reporting;
using Xunit;
namespace MphRead.Tests;
public sealed class BalanceReportTests
{
    private static void Run(params string[] args) => Assembly.Load("ProjectPrimeTools").GetType("MphRead.BalanceCommand")!
        .GetMethod("Run", BindingFlags.Static|BindingFlags.Public)!.Invoke(null,new object[]{args});
    [Fact]
    public void AggregateUsesMeasuredDamageAndSeparatesChangedHunterAndInsufficientSamples()
    {
        string path=Path.Combine(Path.GetTempPath(),"prime-balance-"+Guid.NewGuid()); Directory.CreateDirectory(path);
        try
        {
            var report=MatchReportOutboxTests.Report(); var first=report.Participants[0];
            var second=first with {ParticipantId=Guid.NewGuid(), Metrics=first.Metrics with {Standing=1,DamageDealt=6000},
                Spans=ImmutableArray.Create(new MatchParticipationSpan(1,31,ParticipantExitReason.Timeout,1,Hunter.Kanden,1,30),
                    new MatchParticipationSpan(31,61,ParticipantExitReason.Completed,1,Hunter.Trace,1,30))};
            report=report with {Participants=ImmutableArray.Create(first,second)};
            File.WriteAllText(Path.Combine(path,"one.json"),JsonSerializer.Serialize(report));
            Run("balance",path,Path.Combine(path,"output"));
            using var output=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"output.json")));
            var cohort=output.RootElement.GetProperty("cohorts")[0];
            Assert.Equal(1,cohort.GetProperty("ExcludedChangingHunterMetrics").GetInt32());
            Assert.False(cohort.GetProperty("SufficientMatches").GetBoolean());
            var hunter=cohort.GetProperty("Hunters")[0]; Assert.Equal(6000,hunter.GetProperty("DamagePerMinute").GetDouble());
            Assert.Equal(1,hunter.GetProperty("Wins").GetInt32());
        }
        finally {Directory.Delete(path,true);}
    }
    [Fact]
    public void JoinedTelemetryUsesPickupMappingAndCensorsShortSpawnWindows()
    {
        string path=Path.Combine(Path.GetTempPath(),"prime-balance-"+Guid.NewGuid()); Directory.CreateDirectory(path);
        string reports=Path.Combine(path,"reports"), captures=Path.Combine(path,"captures");Directory.CreateDirectory(reports);Directory.CreateDirectory(captures);
        try
        {
            var report=MatchReportOutboxTests.Report();File.WriteAllText(Path.Combine(reports,"one.json"),JsonSerializer.Serialize(report));
            var telemetry=new MatchTelemetry(1,report.MatchId,report.Rules.RoomKey,report.Rules.Mode,1,1,61,true,0,
                [new(1,TelemetryKind.Spawn,0,1,0,0,0),new(30,TelemetryKind.World,0,1,0,0,0,Weapon:5,Value:(int)WorldSignalKind.PickupConsumed)]);
            using(var file=File.Create(Path.Combine(captures,"one.telemetry.json.gz")))
            using(var gzip=new GZipStream(file,CompressionLevel.Fastest))JsonSerializer.Serialize(gzip,telemetry);
            Run("balance",reports,Path.Combine(path,"output"),captures,"1");
            using var output=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"output.json")));
            var row=output.RootElement.GetProperty("telemetry")[0];
            Assert.Equal(0,row.GetProperty("Eligible3").GetInt32());
            var weapon=row.GetProperty("Weapons")[0];Assert.Equal(1,weapon.GetProperty("Weapon").GetInt32());
            Assert.Equal(1,weapon.GetProperty("Pickups").GetInt32());
            Assert.Equal(60,weapon.GetProperty("PickupsPerMinute").GetDouble());
        }
        finally {Directory.Delete(path,true);}
    }
    [Fact]
    public void SameUuidConflictingBodyFailsInsteadOfDoubleCounting()
    {
        string path=Path.Combine(Path.GetTempPath(),"prime-balance-"+Guid.NewGuid());Directory.CreateDirectory(path);
        try
        {
            var report=MatchReportOutboxTests.Report();File.WriteAllText(Path.Combine(path,"one.json"),JsonSerializer.Serialize(report));
            File.WriteAllText(Path.Combine(path,"two.json"),JsonSerializer.Serialize(report with {BuildVersion="different"}));
            Assert.IsType<InvalidDataException>(Assert.Throws<TargetInvocationException>(()=>Run("balance",path,Path.Combine(path,"output"))).InnerException);
        }
        finally {Directory.Delete(path,true);}
    }
    [Fact]
    public void DuplicateBodiesCountOnceAndBotMatchesAreExcluded()
    {
        string path=Path.Combine(Path.GetTempPath(),"prime-balance-"+Guid.NewGuid());Directory.CreateDirectory(path);
        try
        {
            var report=MatchReportOutboxTests.Report(); string body=JsonSerializer.Serialize(report);
            File.WriteAllText(Path.Combine(path,"one.json"),body); File.WriteAllText(Path.Combine(path,"duplicate.json"),body);
            var bots=report with {MatchId=Guid.NewGuid(),Participants=ImmutableArray.Create(report.Participants[0] with {Kind=ParticipantKind.Bot})};
            File.WriteAllText(Path.Combine(path,"bots.json"),JsonSerializer.Serialize(bots));
            Run("balance",path,Path.Combine(path,"output"));
            using var output=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"output.json")));
            Assert.Equal(1,output.RootElement.GetProperty("excludedBotOrPracticeMatches").GetInt32());
            Assert.Equal(1,output.RootElement.GetProperty("cohorts")[0].GetProperty("Matches").GetInt32());
        }
        finally {Directory.Delete(path,true);}
    }

}
