using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Cosmetics;

namespace MphRead.Mods.Accounts;

public readonly record struct AccountCosmeticLoadout(Hunter Hunter, CosmeticLoadout Loadout);

public sealed partial class AccountSession
{
    public async Task<IReadOnlyList<AccountCosmeticLoadout>> GetCosmeticsAsync(
        CancellationToken cancel = default)
    {
        CosmeticLoadoutWire[] response = await SendAsync<CosmeticLoadoutWire[]>(HttpMethod.Get,
            "v1/me/cosmetics", null, await AccessTokenAsync(cancel).ConfigureAwait(false), cancel)
            .ConfigureAwait(false);
        if (response.Length > 8)
            throw new InvalidOperationException("The backend returned too many cosmetic loadouts.");
        var result = new AccountCosmeticLoadout[response.Length];
        var hunters = new HashSet<Hunter>();
        for (int i = 0; i < response.Length; i++)
        {
            result[i] = ValidateCosmeticResponse(response[i]);
            if (!hunters.Add(result[i].Hunter))
                throw new InvalidOperationException("The backend returned duplicate cosmetic loadouts.");
        }
        return result;
    }

    public async Task<AccountCosmeticLoadout> GetCosmeticsAsync(Hunter hunter,
        CancellationToken cancel = default)
    {
        ValidateCosmeticHunter(hunter);
        CosmeticLoadoutWire response = await SendAsync<CosmeticLoadoutWire>(HttpMethod.Get,
            $"v1/me/cosmetics/{hunter.ToString().ToLowerInvariant()}", null,
            await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
        AccountCosmeticLoadout result = ValidateCosmeticResponse(response);
        if (result.Hunter != hunter)
            throw new InvalidOperationException("The backend returned the wrong Hunter cosmetic loadout.");
        return result;
    }

    public async Task<AccountCosmeticLoadout> PutCosmeticsAsync(Hunter hunter,
        CosmeticLoadout loadout, CancellationToken cancel = default)
    {
        ValidateCosmeticHunter(hunter);
        if (!CosmeticCatalog.BuiltIn.TryResolve(loadout, hunter, out _, out _))
            throw new ArgumentException("The cosmetic loadout is not in the official catalog.", nameof(loadout));
        CosmeticLoadoutWire response = await SendAsync<CosmeticLoadoutWire>(HttpMethod.Put,
            $"v1/me/cosmetics/{hunter.ToString().ToLowerInvariant()}", new
            {
                loadout.SkinKey,
                loadout.ArmorEffectKey,
                loadout.DeathEffectKey
            }, await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
        AccountCosmeticLoadout result = ValidateCosmeticResponse(response);
        if (result.Hunter != hunter || result.Loadout != loadout)
            throw new InvalidOperationException("The backend returned a different cosmetic loadout.");
        return result;
    }

    private static AccountCosmeticLoadout ValidateCosmeticResponse(CosmeticLoadoutWire response)
    {
        if (!Enum.TryParse(response.Hunter, ignoreCase: true, out Hunter hunter)
            || hunter is < Hunter.Samus or > Hunter.Guardian)
            throw new InvalidOperationException("The backend returned an invalid cosmetic Hunter.");
        var loadout = new CosmeticLoadout(response.SkinKey, response.ArmorEffectKey,
            response.DeathEffectKey);
        if (!CosmeticCatalog.BuiltIn.TryResolve(loadout, hunter, out _, out _))
            throw new InvalidOperationException("The backend returned an invalid cosmetic loadout.");
        return new AccountCosmeticLoadout(hunter, loadout);
    }

    private static void ValidateCosmeticHunter(Hunter hunter)
    {
        if (hunter is < Hunter.Samus or > Hunter.Guardian)
            throw new ArgumentOutOfRangeException(nameof(hunter));
    }

    private sealed class CosmeticLoadoutWire
    {
        [JsonRequired, JsonPropertyName("hunter")]
        public string Hunter { get; init; } = "";

        [JsonRequired, JsonPropertyName("skinKey")]
        public string SkinKey { get; init; } = "";

        [JsonRequired, JsonPropertyName("armorEffectKey")]
        public string ArmorEffectKey { get; init; } = "";

        [JsonRequired, JsonPropertyName("deathEffectKey")]
        public string DeathEffectKey { get; init; } = "";
    }
}
