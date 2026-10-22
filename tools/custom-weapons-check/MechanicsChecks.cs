using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.CustomWeapons;

internal static class MechanicsChecks
{
    internal const string Json = """
        {"schema":1,"behaviorVersion":1,"behavior":"semi-auto-precision","damage":{"body":65,"head":150},"magazineCapacity":4,"initialReserve":16,"maximumReserve":64,"shotCooldownTicks":75,"reloadTicks":138,"projectileSpeed4096":819200,"projectileRadius4096":41,"range4096":4096000,"lifetimeTicks":6,"hipSpreadMilliDegrees":1200,"scopedSpreadMilliDegrees":0,"maxActiveProjectiles":8,"opticsMilli":[2500,4000],"splash":false,"piercing":false}
        """;
    internal static WeaponMechanics Rules(string json = Json) => WeaponMechanics.Parse(Encoding.UTF8.GetBytes(json));
    internal static void Run(Action<bool,string> check)
    {
        void Bad(byte[] data, string label)
        { try { WeaponMechanics.Parse(data); } catch (FormatException) { check(true,label); return; } throw new Exception("Accepted invalid mechanics: " + label); }
        void Invalid(string data, string label) => Bad(Encoding.UTF8.GetBytes(data),label);
        var rules = Rules();
        check(rules.DefinitionHash == "3ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228","independent canonical schema-1 SHA-256 vector");
        check(rules.BodyDamage == 65 && rules.HeadDamage == 150 && rules.ShotCooldownTicks == 75
            && rules.ReloadTicks == 138 && rules.MagazineCapacity == 4 && rules.InitialReserve == 16,"M7 tuning uses exact 60 Hz units");
        var root = JsonNode.Parse(Json)!.AsObject();
        string reversed = "{" + string.Join(",",root.Reverse().Select(p => System.Text.Json.JsonSerializer.Serialize(p.Key) + ":" + p.Value!.ToJsonString())) + "}";
        check(Rules(reversed).DefinitionHash == rules.DefinitionHash,"canonical hash ignores input order and whitespace");
        check(Rules(Encoding.UTF8.GetString(rules.ToCanonicalBytes())).DefinitionHash == rules.DefinitionHash,"canonical round trip");
        byte[] detached = rules.ToCanonicalBytes(); detached[0] = 0;
        check(rules.ToCanonicalBytes()[0] == (byte)'{' && rules.DefinitionHash == Rules().DefinitionHash,"export cannot mutate validated rules");
        try { ((IList<int>)rules.OpticMagnificationsMilli)[0] = 1234; throw new Exception("Mutable optics"); }
        catch (NotSupportedException) { check(true,"immutable optic storage"); }
        var guid = Guid.Parse("9a776851-f39f-4a76-8c13-c5d88875ef61");
        var named = WeaponDefinition.Create(guid,"M7",rules);
        check(named.Identity == WeaponDefinition.Create(guid,"Renamed listing",rules).Identity,"display metadata excluded from mechanics identity");
        try { new WeaponDefinition(WeaponIdentity.Custom(guid,new string('a',64)),"M7",rules); throw new Exception("Digest mismatch accepted"); }
        catch (ArgumentException) { check(true,"definition identity must match mechanics"); }
        foreach (var (field,value) in new (string,JsonNode?)[]
        {
            ("schema",2),("behaviorVersion",2),("behavior","script"),("magazineCapacity",0),("magazineCapacity",33),
            ("initialReserve",-1),("initialReserve",65),("maximumReserve",513),("shotCooldownTicks",5),("shotCooldownTicks",3601),
            ("reloadTicks",0),("reloadTicks",3601),("projectileSpeed4096",0),("projectileRadius4096",4097),("range4096",0),
            ("lifetimeTicks",0),("lifetimeTicks",601),("lifetimeTicks",1),("hipSpreadMilliDegrees",10001),
            ("scopedSpreadMilliDegrees",1201),("maxActiveProjectiles",9),("splash",true),("piercing",true),
            ("opticsMilli",new JsonArray()),("opticsMilli",new JsonArray(4000,2500)),("opticsMilli",new JsonArray(2500,2500)),
            ("opticsMilli",new JsonArray(999)),("opticsMilli",new JsonArray(8001)),("opticsMilli",new JsonArray(1000,2000,3000,4000,5000)),
            ("schema",null),("reloadTicks","138"),("reloadTicks",138.5)
        })
        {
            var changed = JsonNode.Parse(Json)!.AsObject(); changed[field] = value;
            Invalid(changed.ToJsonString(),"range/type/preset boundary " + field);
        }
        Invalid(Json.Replace("\"body\":65","\"body\":-1"),"negative body damage");
        Invalid(Json.Replace("\"head\":150","\"head\":3001"),"head budget");
        Invalid(Json.Replace("\"schema\":1","\"schema\":1,\"schema\":1"),"duplicate root keys");
        Invalid(Json.Replace("\"body\":65","\"body\":65,\"body\":65"),"duplicate nested keys");
        var missing = JsonNode.Parse(Json)!.AsObject(); missing.Remove("reloadTicks"); Invalid(missing.ToJsonString(),"missing field");
        var unknown = JsonNode.Parse(Json)!.AsObject(); unknown["code"] = "evil.dll"; Invalid(unknown.ToJsonString(),"unknown fields");
        Invalid(Json.Replace("\"body\":65","\"body\":NaN"),"nonfinite JSON");
        Invalid(Json.Replace("\"body\":65","\"body\":1e10000"),"overflow number");
        Invalid(Json + "{}","trailing object"); Invalid("[]","wrong root kind");
        Bad(new byte[WeaponMechanics.MaxJsonBytes+1],"byte cap before parsing");
        var random = new Random(107);
        for (int i=0; i<1000; i++)
        { var bytes = new byte[random.Next(1,128)]; random.NextBytes(bytes); Bad(bytes,"bounded malformed fuzz"); }
    }
}
