try
{
    if (args.Length == 2 && args[0] == "--probe") return OwnedProbe.Run(args[1]);
    if (args.Length != 0) throw new ArgumentException("Use no arguments for generated fixtures, or --probe OWNED_MPH_ROOT for read-only metadata admission.");
    CreditsFixtures.Run();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("CREDITS SOURCE FAIL: " + error);
    return 1;
}
