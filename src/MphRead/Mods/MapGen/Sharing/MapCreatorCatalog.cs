using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public sealed record MapCreatorCredential(string CreatorId,string TokenHash,bool Moderator=false);
public sealed record MapFavorite(string UserId,Guid MapId,DateTimeOffset CreatedAt);
public sealed record CommunityMapReport(Guid Id,string ReporterId,Guid MapId,string? Version,string Reason,string Details,DateTimeOffset CreatedAt,string Status="Open");
public sealed record MapReportRequest(string? Version,string Reason,string Details);

/// <summary>Small service catalog, persisted atomically independently from immutable archives.</summary>
public sealed class MapCreatorCatalog
{
    public const string ServiceOwner="service-owner";
    public static readonly string[] ReportReasons={"Broken map","Offensive content","Stolen content","Malicious package","Misleading metadata","Other"};
    private readonly string _storage;
    private readonly object _gate=new();
    private readonly MapCreatorCredential[] _creators;
    private readonly List<MapFavorite> _favorites;
    private readonly List<CommunityMapReport> _reports;
    private readonly Dictionary<Guid,string[]> _collaborators;
    public MapCreatorCatalog(string storage,string secret)
    {
        _storage=storage;
        _creators=Load<MapCreatorCredential[]>("creators.json")??Array.Empty<MapCreatorCredential>();
        if(_creators.Length>5000||_creators.Any(c=>string.IsNullOrWhiteSpace(c.CreatorId)||c.CreatorId.Length>128||c.CreatorId==ServiceOwner||!MapCommunityClient.ValidHash(c.TokenHash))
            ||_creators.Select(c=>c.CreatorId).Distinct(StringComparer.Ordinal).Count()!=_creators.Length||_creators.Select(c=>c.TokenHash).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=_creators.Length)
            throw new InvalidDataException("Invalid creator registry.");
        _creators=_creators.Append(new(ServiceOwner,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant(),true)).ToArray();
        _favorites=Load<List<MapFavorite>>("map_favorites.json")??new();_reports=Load<List<CommunityMapReport>>("map_reports.json")??new();_collaborators=Load<Dictionary<Guid,string[]>>("map_collaborators.json")??new();
    }
    private T? Load<T>(string name)
    {string path=Path.Combine(_storage,name);if(!File.Exists(path))return default;if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Creator catalog exceeds storage budget.");return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path),MapPackageReader.JsonOptions);}
    private void Save<T>(string name,T value)=>AtomicFile.Write(Path.Combine(_storage,name),JsonSerializer.SerializeToUtf8Bytes(value,MapPackageReader.JsonOptions));
    public MapCreatorCredential? Authenticate(string? authorization)
    {
        if(authorization==null||!authorization.StartsWith("Bearer ",StringComparison.Ordinal)||authorization.Length>4096)return null;
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
        return _creators.FirstOrDefault(c=>CryptographicOperations.FixedTimeEquals(hash,Convert.FromHexString(c.TokenHash)));
    }
    public bool CanPublish(string creator,CommunityMap map)
    {lock(_gate)return creator==map.OwnerId||_collaborators.GetValueOrDefault(map.MapId,Array.Empty<string>()).Contains(creator,StringComparer.Ordinal);}
    public void SetCollaborators(Guid map,string[] creators)
    {lock(_gate){if(creators.Length>32||creators.Any(id=>!_creators.Any(c=>c.CreatorId==id)))throw new InvalidDataException("Use up to 32 registered creator IDs.");var copy=new Dictionary<Guid,string[]>(_collaborators){[map]=creators.Distinct(StringComparer.Ordinal).ToArray()};Save("map_collaborators.json",copy);_collaborators[map]=copy[map];}}
    public CommunityMap Decorate(CommunityMap map,string? viewer)
    {lock(_gate)return map with{FavoriteCount=_favorites.Count(f=>f.MapId==map.MapId),Favorited=viewer!=null&&_favorites.Any(f=>f.MapId==map.MapId&&f.UserId==viewer)};}
    public void Favorite(string creator,Guid map,bool value)
    {
        lock(_gate)
        {
            var next=_favorites.Where(f=>f.UserId!=creator||f.MapId!=map).ToList();
            if(value){if(next.Count>=100000||next.Count(f=>f.UserId==creator)>=5000)throw new InvalidDataException("Favorite limit reached.");next.Add(new(creator,map,DateTimeOffset.UtcNow));}
            Save("map_favorites.json",next);_favorites.Clear();_favorites.AddRange(next);
        }
    }
    public CommunityMapReport Report(string creator,Guid map,MapReportRequest request)
    {
        if(!ReportReasons.Contains(request.Reason,StringComparer.Ordinal)||request.Details==null||request.Details.Length>4000||request.Version?.Length>64)throw new InvalidDataException("Invalid report reason or details.");
        lock(_gate)
        {
            var existing=_reports.FirstOrDefault(r=>r.ReporterId==creator&&r.MapId==map&&r.Version==request.Version&&r.Status=="Open");if(existing!=null)return existing;
            if(_reports.Count>=100000||_reports.Count(r=>r.ReporterId==creator&&r.CreatedAt>DateTimeOffset.UtcNow.AddDays(-1))>=20)throw new InvalidDataException("Report limit reached.");
            var report=new CommunityMapReport(Guid.NewGuid(),creator,map,request.Version,request.Reason,request.Details,DateTimeOffset.UtcNow);
            Save("map_reports.json",_reports.Append(report).ToArray());_reports.Add(report);return report;
        }
    }
    public CommunityMapReport[] Reports(){lock(_gate)return _reports.OrderByDescending(r=>r.CreatedAt).ToArray();}
    public void SetReportStatus(Guid id,string status)
    {
        if(status is not ("Open" or "Reviewed" or "Resolved" or "Dismissed"))throw new InvalidDataException("Invalid moderation status.");
        lock(_gate){int index=_reports.FindIndex(r=>r.Id==id);if(index<0)throw new InvalidDataException("Unknown report.");var copy=_reports.ToArray();copy[index]=copy[index] with{Status=status};Save("map_reports.json",copy);_reports[index]=copy[index];}
    }
}
