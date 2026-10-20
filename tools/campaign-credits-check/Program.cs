try
{
    if (args.Length == 2 && args[0] == "--probe") return OwnedProbe.Run(args[1]);
    if (args.Length == 2 && args[0] == "--probe-timeline") return OwnedTimelineProbe.Run(args[1]);
    if (args.Length != 0) throw new ArgumentException("Use no arguments for generated fixtures, --probe OWNED_MPH_ROOT for source metadata, or --probe-timeline OWNED_MPH_ROOT for pure timeline metadata.");
    CreditsFixtures.Run();
    AnimationDurationFixtures.Run();
    CreditsTimelineFixtures.Run();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("CREDITS SOURCE FAIL: " + error);
    return 1;
}
