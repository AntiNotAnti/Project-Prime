# Social controller contracts

Run with the repository's .NET 10 SDK:

```sh
dotnet run --project tools/social-controller-check/social-controller-check.csproj -p:MphReadRmlUi=true
```

The check uses only injected fake Social services. It never starts presence or
performs production authentication, friend, party, invitation or message writes.
It verifies presentation ownership and immutable service contracts rather than
mocking a server's party seat authority. Run the existing waitlist/network,
Edge handler, frozen Deno and disposable SQL gates separately for those paths.
