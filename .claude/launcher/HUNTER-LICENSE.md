# Hunter License

Hunter License is the hub profile/career surface for Project Prime.

## Data flow

The launcher authenticates with Supabase Auth using an anonymous account and persists only the refresh token in the normal Project Prime user-data directory. The public publishable key is client-safe and can be overridden with `PROJECT_PRIME_SUPABASE_URL` and `PROJECT_PRIME_SUPABASE_KEY`.

The database bridge is deliberately read-oriented. It creates or syncs the authenticated user's identity rows in the existing EF-managed `prime.players`, `prime.player_profiles`, and `prime.hunter_licenses` tables, then reads career data from:

- `prime.career_participations`
- `prime.accepted_matches`
- `prime.rating_transactions`
- `prime.player_cosmetic_loadouts`

Career totals are **not** posted by the game client. They continue to come from the accepted-match pipeline, which preserves the existing trust/rating boundary.

## Supabase setup

Apply `supabase/migrations/20260921134100_project_prime_hunter_license_bridge.sql`.

Supabase Auth must allow anonymous sign-ins. If it is disabled, the UI stays usable with a local fallback and shows **ENABLE SUPABASE ANONYMOUS SIGN-IN** instead of silently failing.

## UI

`LicenseWorkspace` is the hub-native player identity surface:

- a persistent **License Holder** rail anchors the page with the live hunter model,
  display name, Hunter ID, rating tier, favorite hunter, issue month and secured/guest state;
- Overview leads with authoritative career record, Matches / Win Rate / K-D / Play Time,
  deterministic **Recent Form** from the five newest accepted matches, career highlights
  and the four latest accepted-match cards;
- Stats keeps the full career/combat/objective breakdown;
- Match History shows the 25 most recent accepted matches and their career eligibility;
- Achievements are deterministic milestones derived from authoritative career totals;
- Customization continues to use the existing authenticated cosmetic loadout flow;
- Account / Secure License continues to upgrade or recover the same Supabase identity;
- Emblems and Titles remain catalog foundations until a real identity catalog exists;
- Comparison remains a privacy-safe per-match benchmark until profile visibility rules exist.

The UI never invents accuracy, medals, public rankings or per-hunter career breakdowns that
are not present in the Hunter License snapshot. Recent Form is presentation-only arithmetic
over the accepted match list and cannot write career state.

## Identity note

Anonymous Auth gives an install a persistent Hunter identity without forcing an account-creation wall. Clearing the app's data loses that anonymous credential. A later account-linking flow can upgrade the same Supabase user to a durable external identity without changing the career schema.


## Guest-to-secured identity flow

Every install receives a **Guest License** automatically. The guest is a real
Supabase authenticated user and owns a normal Hunter ID/career, but its only
credential is the refresh token stored in Project Prime's local user-data
directory.

The **Secure License / Account** page can upgrade that same UUID in place:

1. Enter an email and recovery password.
2. Project Prime asks Supabase to link the email. Supabase sends its configured
   email-change verification message.
3. Confirm the address from the email, return to Project Prime, and choose
   **I VERIFIED // FINISH**. Only then is the password sent to Supabase.
4. The license becomes **SECURED LICENSE**. The Hunter ID and all career rows
   are unchanged.
5. On another device, **SIGN IN // RECOVER LICENSE** uses the linked
   email/password and swaps an empty guest session for the existing license.

A guest that already has career history is deliberately prevented from using
the recovery action. Switching identities would strand the only local
credential for that guest. The player must secure the current license first.

### Optional OAuth links

The Account page can also call Supabase manual identity linking for **Google,
GitHub and Discord**. The provider flow opens in the system browser and links
back to the current Supabase user; Project Prime keeps the existing session and
refreshes identity status afterwards.

Supabase requirements:

- **Anonymous Sign-Ins** enabled
- **Manual Linking** enabled under Auth provider settings
- Email auth enabled for email/password recovery
- Any social provider you expose (Google/GitHub/Discord) configured with its
  provider credentials

If manual linking or a provider is not configured, the Account page reports
the Supabase error inline rather than replacing the guest identity.

### Credential safety

Only the Supabase **refresh token** is persisted locally. Project Prime never
writes the email recovery password to disk. A failed refresh caused by a
network timeout or rate limit no longer deletes the stored identity; the local
session is discarded only when Supabase explicitly rejects the refresh token.


### Recovery preserves the server profile

The bridge only seeds `prime.player_profiles` when a Supabase user has no
profile yet. Opening a recovered license on a fresh device therefore cannot
replace its display name/favorite hunter with that device's default launcher
values. Profile editing should be an explicit account/profile action, not a
side effect of reading the license.


## Authoritative career stats

Career persistence is deliberately not a client API.

1. A client obtains a two-hour `career-ticket` from a JWT-protected Supabase
   Edge Function. The ticket contains its Supabase user UUID and the current
   network `ClientId`, HMAC-signed with an Edge-only key.
2. The ticket travels in additive `PacketType.CareerIdentity`. No Supabase
   session token is sent over UDP.
3. The dedicated server records kills/deaths/headshots/objectives from
   `GameState`, plus authority-only damage, assists and longest streaks.
   Client prediction and replay paths are explicitly excluded.
4. At match end the server writes a JSON report to its local
   `career-outbox` first, then posts it to `career-report` in the
   background.
5. `career-report` authenticates the provisioned server, verifies every
   career ticket, strips the tickets, and calls the service-role-only
   `ingest_project_prime_career_match` transaction.
6. That transaction inserts the immutable accepted match, participant history,
   career/map/mode/hunter/weapon aggregates, and, only for eligible continuous
   verified-server matches, PairwiseNormalizedV1 rating transactions.

A started player without a valid career ticket makes the report Practice for
career/rating purposes. This avoids the poisonous alternative where one
unattributed slot changes the standings used to award everyone else's durable
career.

The reporter registry stores only SHA-256 hashes of server keys. Plain reporter
keys live only in the server's private `career.env`.


### Account feedback and guest publishing

Account progress/errors appear above the form. Email verification can start before
entering a password; finishing still requires matching passwords of at least eight
characters and a verified email. Email/password/provider-link actions and Refresh
can retry when profile loading failed. Recovery still requires a loaded, empty
career because it switches identities. Form values survive status updates, with
password confirmation retained independently.

Map publication accepts the automatically created guest identity. It mints a narrow
Community ticket directly and does not depend on career/profile synchronization.
Linking a registered account is optional for publishing and useful for recovering
ownership on another installation. Supabase Manual Linking must be enabled for
social identity linking; a disabled setting cannot be repaired by a client rebuild.
